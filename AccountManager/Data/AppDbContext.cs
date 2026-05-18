using Microsoft.EntityFrameworkCore;
using AccountManager.Models;

namespace AccountManager.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Account> Accounts { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Account>(entity =>
        {
            entity.ToTable("Account");

            entity.HasIndex(a => a.AccountName)
                  .IsUnique()
                  .HasDatabaseName("IX_Account_AccountName");

            entity.Property(a => a.AccountName)
                  .HasColumnName("AccountName")
                  .HasMaxLength(200)
                  .IsRequired();

            entity.Property(a => a.Email)
                  .HasColumnName("Email")
                  .HasMaxLength(320)
                  .IsRequired();

            entity.Property(a => a.Contact)
                  .HasColumnName("Contact")
                  .HasMaxLength(50)
                  .IsRequired(false);

            entity.Property(a => a.CreatedAt)
                  .HasColumnName("CreatedAt")
                  .HasDefaultValueSql("GETUTCDATE()");

            entity.Property(a => a.UpdatedAt)
                  .HasColumnName("UpdatedAt")
                  .HasDefaultValueSql("GETUTCDATE()");
        });
    }
}
