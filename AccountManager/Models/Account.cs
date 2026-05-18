using System.ComponentModel.DataAnnotations;

namespace AccountManager.Models;

/// <summary>
/// Represents a row in the Account table.
/// </summary>
public class Account
{
    [Key]
    public int Id { get; set; }

    [Required]
    [MaxLength(200)]
    public string AccountName { get; set; } = string.Empty;

    [Required]
    [MaxLength(320)]
    public string Email { get; set; } = string.Empty;

    [MaxLength(50)]
    public string? Contact { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
