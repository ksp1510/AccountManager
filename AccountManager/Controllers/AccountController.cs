using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using AccountManager.Data;
using AccountManager.Models;
using AccountManager.Services;

namespace AccountManager.Controllers;

public class AccountController : Controller
{
    private readonly AppDbContext    _db;
    private readonly CsvImportService _importService;
    private readonly LogService      _log;
    private readonly IConfiguration  _config;

    public AccountController(
        AppDbContext db,
        CsvImportService importService,
        LogService log,
        IConfiguration config)
    {
        _db            = db;
        _importService = importService;
        _log           = log;
        _config        = config;
    }

    // ── GET /Account ──────────────────────────────────────────────────────────
    /// <summary>Lists all accounts and shows the import form.</summary>
    public async Task<IActionResult> Index()
    {
        var accounts = await _db.Accounts
            .OrderBy(a => a.Id)
            .ToListAsync();

        ViewBag.DefaultCsvPath = _config["CsvImport:DefaultFilePath"] ?? string.Empty;
        ViewBag.SuccessLogPath = _log.SuccessLogPath;
        ViewBag.ErrorLogPath   = _log.ErrorLogPath;
        ViewBag.UpsertLogPath  = _log.UpsertedContactLogPath;

        return View(accounts);
    }

    // ── POST /Account/Import ──────────────────────────────────────────────────
    /// <summary>Runs the CSV import from the supplied file path.</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Import(string csvFilePath)
    {
        if (string.IsNullOrWhiteSpace(csvFilePath))
        {
            TempData["Error"] = "Please provide a CSV file path.";
            return RedirectToAction(nameof(Index));
        }

        await _log.LogSuccessAsync($"Import started. File='{csvFilePath}'");

        var result = await _importService.ImportAsync(csvFilePath);

        TempData["ImportResult"] =
            $"Import complete — Total: {result.TotalRows} | " +
            $"Inserted: {result.Inserted} | " +
            $"Updated: {result.Updated} | " +
            $"Contacts added: {result.ContactsUpserted} | " +
            $"Skipped: {result.Skipped}";

        if (result.Errors.Count > 0)
            TempData["ImportErrors"] = string.Join(" | ", result.Errors.Take(5));

        await _log.LogSuccessAsync(
            $"Import finished. Inserted={result.Inserted}, Updated={result.Updated}, " +
            $"ContactsUpserted={result.ContactsUpserted}, Skipped={result.Skipped}");

        return RedirectToAction(nameof(Index));
    }

    // ── GET /Account/Find ─────────────────────────────────────────────────────
    /// <summary>Shows the find-by-name search form.</summary>
    public IActionResult Find(string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return View(new List<Account>());

        // LINQ search: case-insensitive contains
        var results = _db.Accounts
            .Where(a => a.AccountName.Contains(query))
            .OrderBy(a => a.AccountName)
            .ToList();

        ViewBag.Query = query;
        return View(results);
    }

    // ── GET /Account/Details/{id} ─────────────────────────────────────────────
    public async Task<IActionResult> Details(int id)
    {
        var account = await _db.Accounts.FindAsync(id);
        if (account is null) return NotFound();
        return View(account);
    }

    // ── GET /Account/Edit/{id} ────────────────────────────────────────────────
    public async Task<IActionResult> Edit(int id)
    {
        var account = await _db.Accounts.FindAsync(id);
        if (account is null) return NotFound();
        return View(account);
    }

    // ── POST /Account/Edit/{id} ───────────────────────────────────────────────
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, Account model)
    {
        if (id != model.Id) return BadRequest();

        // Email validation
        if (!CsvImportService.IsValidEmail(model.Email))
        {
            ModelState.AddModelError("Email", "Please enter a valid email address.");
            return View(model);
        }

        if (!ModelState.IsValid) return View(model);

        var existing = await _db.Accounts.FindAsync(id);
        if (existing is null) return NotFound();

        string? oldContact = existing.Contact;

        existing.AccountName = model.AccountName.Trim();
        existing.Email       = model.Email.Trim().ToLowerInvariant();
        existing.Contact     = string.IsNullOrWhiteSpace(model.Contact) ? null : model.Contact.Trim();
        existing.UpdatedAt   = DateTime.UtcNow;

        await _db.SaveChangesAsync();
        await _log.LogSuccessAsync(
            $"Manual edit – Id={id}, AccountName='{existing.AccountName}', Email='{existing.Email}'");

        if (existing.Contact != oldContact)
            await _log.LogUpsertedContactAsync(existing.AccountName, oldContact, existing.Contact);

        return RedirectToAction(nameof(Index));
    }

    // ── GET /Account/Logs ─────────────────────────────────────────────────────
    /// <summary>Displays the three log file contents.</summary>
    public IActionResult Logs()
    {
        ViewBag.SuccessLog = ReadLog(_log.SuccessLogPath);
        ViewBag.ErrorLog   = ReadLog(_log.ErrorLogPath);
        ViewBag.UpsertLog  = ReadLog(_log.UpsertedContactLogPath);
        return View();
    }

    private static string ReadLog(string path)
    {
        if (!System.IO.File.Exists(path)) return "(no entries yet)";
        var lines = System.IO.File.ReadAllLines(path);
        // Return last 100 lines, newest-first
        return string.Join(Environment.NewLine, lines.Reverse().Take(100));
    }
}
