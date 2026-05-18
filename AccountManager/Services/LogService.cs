namespace AccountManager.Services;

/// <summary>
/// Writes to three separate rotating log files:
///   logs/success.log   – every successful DB operation
///   logs/error.log     – validation failures and exceptions
///   logs/upserted_contact.log – rows where Contact was inserted/updated
/// </summary>
public class LogService
{
    private readonly string _logDirectory;
    private readonly string _successLog;
    private readonly string _errorLog;
    private readonly string _upsertedContactLog;

    private static readonly SemaphoreSlim _lock = new(1, 1);

    public LogService(IConfiguration config)
    {
        _logDirectory = config["Logging:LogDirectory"]
                        ?? Path.Combine(Directory.GetCurrentDirectory(), "Logs");

        Directory.CreateDirectory(_logDirectory);

        _successLog        = Path.Combine(_logDirectory, "success.log");
        _errorLog          = Path.Combine(_logDirectory, "error.log");
        _upsertedContactLog = Path.Combine(_logDirectory, "upserted_contact.log");
    }

    // ── Public helpers ──────────────────────────────────────────────────────

    public Task LogSuccessAsync(string message)
        => WriteAsync(_successLog, "SUCCESS", message);

    public Task LogErrorAsync(string message)
        => WriteAsync(_errorLog, "ERROR", message);

    public Task LogUpsertedContactAsync(string accountName, string? oldContact, string? newContact)
    {
        var msg = $"Account='{accountName}' | OldContact='{oldContact ?? "NULL"}' -> NewContact='{newContact ?? "NULL"}'";
        return WriteAsync(_upsertedContactLog, "UPSERT_CONTACT", msg);
    }

    // ── Internal ─────────────────────────────────────────────────────────────

    private async Task WriteAsync(string filePath, string level, string message)
    {
        var line = $"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC] [{level}] {message}{Environment.NewLine}";

        await _lock.WaitAsync();
        try
        {
            await File.AppendAllTextAsync(filePath, line);
        }
        finally
        {
            _lock.Release();
        }
    }

    public string SuccessLogPath        => _successLog;
    public string ErrorLogPath          => _errorLog;
    public string UpsertedContactLogPath => _upsertedContactLog;
}
