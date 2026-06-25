using System.Security.Cryptography;
using Coffer.Core.Domain;
using Coffer.Infrastructure.Goals;
using Coffer.Infrastructure.Tests.Persistence;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Coffer.Infrastructure.Tests.Goals;

public class FinancialContextBuilderTests : IDisposable
{
    private readonly string _tempDir;
    private readonly SqliteTestDbContextFactory _factory;

    private Guid _accountId;
    private Guid _sessionId;

    public FinancialContextBuilderTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "Coffer.Tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        var dbPath = Path.Combine(_tempDir, "coffer.db");
        var dek = RandomNumberGenerator.GetBytes(32);
        _factory = new SqliteTestDbContextFactory(dbPath, dek);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_tempDir))
        {
            try
            {
                Directory.Delete(_tempDir, recursive: true);
            }
            catch
            {
                // Best-effort cleanup.
            }
        }

        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task BuildAsync_EmptyVault_ReturnsZeroedNeutralContext()
    {
        await using (var db = _factory.CreateDbContext())
        {
            await db.Database.MigrateAsync();
        }

        var ctx = await new FinancialContextBuilder(_factory).BuildAsync(new DateOnly(2026, 6, 15), CancellationToken.None);

        ctx.MonthlyIncome.Should().Be(0m);
        ctx.MonthlyFixedExpenses.Should().Be(0m);
        ctx.MonthlyVariableAvg.Should().Be(0m);
        ctx.SeasonalityFor(7).Should().Be(1.0m); // neutral stub
        ctx.Today.Should().Be(new DateOnly(2026, 6, 15));
    }

    [Fact]
    public async Task BuildAsync_SplitsFixedFromVariableAndAveragesOverSixMonths()
    {
        // Anchor month is June 2026 (latest tx), so the window is the six complete months Dec–May.
        //   income:  +6000 each month        ⇒ 6000/mo
        //   "Czynsz": -2000 every window month (present 6/6) ⇒ fixed 2000/mo
        //   "Jedzenie": -900 in 3 of 6 months ⇒ variable 2700/6 = 450/mo
        var rent = Guid.NewGuid();
        var food = Guid.NewGuid();
        await SeedAsync(rent, food);

        var ctx = await new FinancialContextBuilder(_factory).BuildAsync(new DateOnly(2026, 6, 15), CancellationToken.None);

        ctx.MonthlyIncome.Should().Be(6000m);
        ctx.MonthlyFixedExpenses.Should().Be(2000m);
        ctx.MonthlyVariableAvg.Should().Be(450m);
        ctx.MonthlyVariableStdDev.Should().BeGreaterThan(0m);
        ctx.CategoryAverages6m.Should().ContainKey("Czynsz").WhoseValue.Should().Be(2000m);
        ctx.CategoryAverages6m.Should().ContainKey("Jedzenie").WhoseValue.Should().Be(450m);
    }

    private async Task SeedAsync(Guid rentCategoryId, Guid foodCategoryId)
    {
        await using var db = _factory.CreateDbContext();
        await db.Database.MigrateAsync();

        _accountId = Guid.NewGuid();
        _sessionId = Guid.NewGuid();

        db.Accounts.Add(new Account
        {
            Id = _accountId,
            Name = "PKO",
            BankCode = "PKO_BP",
            AccountNumber = "PL01",
            Currency = "PLN",
            Type = AccountType.Checking,
            CreatedAt = DateTime.UtcNow,
        });
        db.ImportSessions.Add(new ImportSession
        {
            Id = _sessionId,
            FileName = "seed.csv",
            FileHash = "SEEDHASH",
            BankCode = "PKO_BP",
            PeriodFrom = new DateOnly(2025, 1, 1),
            PeriodTo = new DateOnly(2026, 12, 31),
            ImportedAt = DateTime.UtcNow,
            Status = ImportStatus.Completed,
        });
        db.Categories.Add(new Category { Id = rentCategoryId, Name = "Czynsz" });
        db.Categories.Add(new Category { Id = foodCategoryId, Name = "Jedzenie" });

        // Window months Dec 2025 .. May 2026.
        var windowMonths = new[]
        {
            new DateOnly(2025, 12, 10),
            new DateOnly(2026, 1, 10),
            new DateOnly(2026, 2, 10),
            new DateOnly(2026, 3, 10),
            new DateOnly(2026, 4, 10),
            new DateOnly(2026, 5, 10),
        };

        foreach (var month in windowMonths)
        {
            db.Transactions.Add(Tx(month, 6000m, "Wypłata", null));
            db.Transactions.Add(Tx(month, -2000m, "Czynsz", rentCategoryId));
        }

        // Variable spend in only three of the six months ⇒ stays out of the fixed bucket.
        db.Transactions.Add(Tx(new DateOnly(2026, 1, 15), -900m, "Biedronka", foodCategoryId));
        db.Transactions.Add(Tx(new DateOnly(2026, 3, 15), -900m, "Biedronka", foodCategoryId));
        db.Transactions.Add(Tx(new DateOnly(2026, 5, 15), -900m, "Biedronka", foodCategoryId));

        // A June transaction anchors the window on June 2026; June itself is excluded from it.
        db.Transactions.Add(Tx(new DateOnly(2026, 6, 5), -25m, "Kiosk", null));

        await db.SaveChangesAsync();
    }

    private Transaction Tx(DateOnly date, decimal amount, string description, Guid? categoryId) =>
        new()
        {
            Id = Guid.NewGuid(),
            AccountId = _accountId,
            ImportSessionId = _sessionId,
            Date = date,
            Amount = amount,
            Currency = "PLN",
            Description = description,
            NormalizedDescription = description.ToUpperInvariant(),
            Merchant = description,
            CategoryId = categoryId,
            Hash = Guid.NewGuid().ToString("N"),
            CreatedAt = DateTime.UtcNow,
        };
}
