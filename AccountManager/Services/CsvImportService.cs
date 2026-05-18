using System.Globalization;
using System.Text.RegularExpressions;
using CsvHelper;
using CsvHelper.Configuration;
using Microsoft.EntityFrameworkCore;
using AccountManager.Data;
using AccountManager.Models;

namespace AccountManager.Services;

public class ImportResult
{
    public int TotalRows         { get; set; }
    public int Inserted          { get; set; }
    public int Updated           { get; set; }
    public int ContactsUpserted  { get; set; }
    public int Skipped           { get; set; }
    public List<string> Errors   { get; set; } = [];
}

public class CsvImportService
{
    // RFC-5321 compliant email regex
    private static readonly Regex EmailRegex = new(
        @"^[a-zA-Z0-9.!#$%&'*+/=?^_`{|}~-]+@[a-zA-Z0-9](?:[a-zA-Z0-9-]{0,61}[a-zA-Z0-9])?(?:\.[a-zA-Z0-9](?:[a-zA-Z0-9-]{0,61}[a-zA-Z0-9])?)*\.[a-zA-Z]{2,}$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private readonly AppDbContext _db;
    private readonly LogService   _log;

    public CsvImportService(AppDbContext db, LogService log)
    {
        _db  = db;
        _log = log;
    }

    /// <summary>
    /// Reads the CSV at <paramref name="filePath"/>, validates each row,
    /// and upserts into the Account table.
    /// </summary>
    public async Task<ImportResult> ImportAsync(string filePath)
    {
        var result = new ImportResult();

        if (!File.Exists(filePath))
        {
            var msg = $"CSV file not found: {filePath}";
            await _log.LogErrorAsync(msg);
            result.Errors.Add(msg);
            return result;
        }

        List<CsvAccountRecord> records;

        // ── 1. Parse CSV ─────────────────────────────────────────────────────
        try
        {
            var config = new CsvConfiguration(CultureInfo.InvariantCulture)
            {
                HasHeaderRecord     = true,
                TrimOptions         = TrimOptions.Trim,
                MissingFieldFound   = null,
                BadDataFound        = null
            };

            using var reader = new StreamReader(filePath);
            using var csv    = new CsvReader(reader, config);
            records = csv.GetRecords<CsvAccountRecord>().ToList();
        }
        catch (Exception ex)
        {
            var msg = $"Failed to parse CSV '{filePath}': {ex.Message}";
            await _log.LogErrorAsync(msg);
            result.Errors.Add(msg);
            return result;
        }

        result.TotalRows = records.Count;

        // ── 2. Process each row ───────────────────────────────────────────────
        foreach (var (rec, rowIndex) in records.Select((r, i) => (r, i + 2))) // +2 = header + 1-based
        {
            var context = $"Row {rowIndex}, AccountName='{rec.AccountName}'";

            // Validate AccountName
            if (string.IsNullOrWhiteSpace(rec.AccountName))
            {
                var msg = $"{context} – AccountName is empty, skipping.";
                await _log.LogErrorAsync(msg);
                result.Errors.Add(msg);
                result.Skipped++;
                continue;
            }

            // Validate Email
            if (string.IsNullOrWhiteSpace(rec.Email) || !EmailRegex.IsMatch(rec.Email.Trim()))
            {
                var msg = $"{context} – Invalid or missing email '{rec.Email}', skipping.";
                await _log.LogErrorAsync(msg);
                result.Errors.Add(msg);
                result.Skipped++;
                continue;
            }

            var normalizedEmail   = rec.Email.Trim().ToLowerInvariant();
            var normalizedContact = string.IsNullOrWhiteSpace(rec.Contact) ? null : rec.Contact.Trim();

            try
            {
                // ── 3. Upsert via LINQ ────────────────────────────────────────
                var existing = await _db.Accounts
                    .FirstOrDefaultAsync(a => a.AccountName == rec.AccountName.Trim());

                if (existing is null)
                {
                    // INSERT
                    var newAccount = new Account
                    {
                        AccountName = rec.AccountName.Trim(),
                        Email       = normalizedEmail,
                        Contact     = normalizedContact,
                        CreatedAt   = DateTime.UtcNow,
                        UpdatedAt   = DateTime.UtcNow
                    };

                    _db.Accounts.Add(newAccount);
                    await _db.SaveChangesAsync();

                    await _log.LogSuccessAsync(
                        $"{context} – Inserted. Email='{normalizedEmail}', Contact='{normalizedContact ?? "NULL"}'");

                    result.Inserted++;

                    if (normalizedContact is not null)
                    {
                        await _log.LogUpsertedContactAsync(rec.AccountName.Trim(), null, normalizedContact);
                        result.ContactsUpserted++;
                    }
                }
                else
                {
                    // UPDATE – always refresh email; upsert contact only when CSV has a value
                    bool changed          = false;
                    string? oldContact    = existing.Contact;
                    bool contactChanged   = false;

                    if (existing.Email != normalizedEmail)
                    {
                        existing.Email = normalizedEmail;
                        changed = true;
                    }

                    if (normalizedContact is not null && existing.Contact != normalizedContact)
                    {
                        existing.Contact  = normalizedContact;
                        changed           = true;
                        contactChanged    = true;
                    }

                    if (changed)
                    {
                        existing.UpdatedAt = DateTime.UtcNow;
                        await _db.SaveChangesAsync();

                        await _log.LogSuccessAsync(
                            $"{context} – Updated. Email='{normalizedEmail}', Contact='{existing.Contact ?? "NULL"}'");

                        result.Updated++;

                        if (contactChanged)
                        {
                            await _log.LogUpsertedContactAsync(rec.AccountName.Trim(), oldContact, normalizedContact);
                            result.ContactsUpserted++;
                        }
                    }
                    else
                    {
                        await _log.LogSuccessAsync($"{context} – No changes detected, skipping save.");
                    }
                }
            }
            catch (Exception ex)
            {
                var msg = $"{context} – DB error: {ex.Message}";
                await _log.LogErrorAsync(msg);
                result.Errors.Add(msg);
                result.Skipped++;
            }
        }

        return result;
    }

    // ── Email validation helper (exposed for controller use) ──────────────────
    public static bool IsValidEmail(string email)
        => !string.IsNullOrWhiteSpace(email) && EmailRegex.IsMatch(email.Trim());
}
