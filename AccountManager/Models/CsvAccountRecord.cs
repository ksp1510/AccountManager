using CsvHelper.Configuration.Attributes;

namespace AccountManager.Models;

/// <summary>
/// Maps directly to the CSV columns; used only during import.
/// </summary>
public class CsvAccountRecord
{
    [Name("Account Name")]
    public string AccountName { get; set; } = string.Empty;

    [Name("Email")]
    public string Email { get; set; } = string.Empty;

    [Name("Contact")]
    public string? Contact { get; set; }
}
