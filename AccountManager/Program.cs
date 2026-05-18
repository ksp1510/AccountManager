using Microsoft.EntityFrameworkCore;
using AccountManager.Data;
using AccountManager.Services;

var builder = WebApplication.CreateBuilder(args);

// ── Services ──────────────────────────────────────────────────────────────────

builder.Services.AddControllersWithViews();

// Entity Framework Core – SQL Server
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("DefaultConnection"),
        sqlOptions => sqlOptions.EnableRetryOnFailure(
            maxRetryCount: 3,
            maxRetryDelay: TimeSpan.FromSeconds(5),
            errorNumbersToAdd: null)));

// Application services
builder.Services.AddSingleton<LogService>();
builder.Services.AddScoped<CsvImportService>();

var app = builder.Build();

// ── Middleware ────────────────────────────────────────────────────────────────

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Account}/{action=Index}/{id?}");

// ── Create database + Account table if they don't exist ──────────────────────

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    try
    {
        // Step 1 — ensure the database itself exists.
        db.Database.EnsureCreated();

        // Step 2 — idempotent DDL: create the Account table only if missing.
        db.Database.ExecuteSqlRaw(@"
            IF NOT EXISTS (
                SELECT 1 FROM INFORMATION_SCHEMA.TABLES
                WHERE TABLE_SCHEMA = 'dbo' AND TABLE_NAME = 'Account'
            )
            BEGIN
                CREATE TABLE [dbo].[Account] (
                    [Id]          INT IDENTITY(1,1)  NOT NULL,
                    [AccountName] NVARCHAR(200)       NOT NULL,
                    [Email]       NVARCHAR(320)       NOT NULL,
                    [Contact]     NVARCHAR(50)            NULL,
                    [CreatedAt]   DATETIME2          NOT NULL
                                  CONSTRAINT [DF_Account_CreatedAt] DEFAULT GETUTCDATE(),
                    [UpdatedAt]   DATETIME2          NOT NULL
                                  CONSTRAINT [DF_Account_UpdatedAt] DEFAULT GETUTCDATE(),
                    CONSTRAINT [PK_Account] PRIMARY KEY CLUSTERED ([Id] ASC)
                );

                CREATE UNIQUE NONCLUSTERED INDEX [IX_Account_AccountName]
                    ON [dbo].[Account] ([AccountName] ASC);
            END
        ");

        logger.LogInformation("Database schema verified/created successfully.");
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Database setup failed. Check your connection string in appsettings.json.");
    }
}

app.Run();