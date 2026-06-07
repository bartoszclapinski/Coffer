using System.Security.Cryptography;
using Coffer.Core.Dashboard;
using Coffer.Core.Domain;
using Coffer.Infrastructure.Dashboard;
using Coffer.Infrastructure.Tests.Persistence;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Coffer.Infrastructure.Tests.Dashboard;

public class DashboardQueryTests : IDisposable
{
    private static readonly DateOnly _asOf = new(2026, 6, 15);

    private readonly string _tempDir;
    private readonly string _dbPath;
    private readonly byte[] _dek;
    private readonly SqliteTestDbContextFactory _factory;

    private Guid _accountId;
    private Guid _accountEur;
    private Guid _groceries;
    private Guid _fuel;
    private Guid _sessionId;

    public DashboardQueryTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "Coffer.Tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _dbPath = Path.Combine(_tempDir, "coffer.db");
        _dek = RandomNumberGenerator.GetBytes(32);
        _factory = new SqliteTestDbContextFactory(_dbPath, _dek);
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
    }

    [Fact]
    public async Task GetSnapshot_EmptyVault_ReturnsZeroedSnapshotWithoutData()
    {
        await using (var db = _factory.CreateDbContext())
        {
            await db.Database.MigrateAsync();
        }

        var snapshot = await NewQuery().GetSnapshotAsync(Filter(), CancellationToken.None);

        snapshot.HasData.Should().BeFalse();
        snapshot.CurrentMonth.Spend.Should().Be(0m);
        snapshot.CurrentMonth.Income.Should().Be(0m);
        snapshot.CurrentMonth.Net.Should().Be(0m);
        snapshot.TopCategories.Should().BeEmpty();
        snapshot.RecentTransactions.Should().BeEmpty();
        snapshot.DailySpend.Should().HaveCount(30);
        snapshot.MonthlySpend.Should().HaveCount(12);
        snapshot.DailySpend.Should().OnlyContain(p => p.Total == 0m);
        snapshot.MonthlySpend.Should().OnlyContain(p => p.Total == 0m);
    }

    [Fact]
    public async Task GetSnapshot_CurrentMonthKpis_AreComputedServerSide()
    {
        await SeedAsync();
        await AddAsync(_groceries, new DateOnly(2026, 6, 3), -120.50m);
        await AddAsync(_fuel, new DateOnly(2026, 6, 10), -200m);
        await AddAsync(null, new DateOnly(2026, 6, 12), 5000m); // income
        await AddAsync(_groceries, new DateOnly(2026, 5, 30), -999m); // previous month, excluded

        var snapshot = await NewQuery().GetSnapshotAsync(Filter(), CancellationToken.None);

        snapshot.HasData.Should().BeTrue();
        snapshot.CurrentMonth.Month.Should().Be(new DateOnly(2026, 6, 1));
        snapshot.CurrentMonth.Spend.Should().Be(320.50m);
        snapshot.CurrentMonth.Income.Should().Be(5000m);
        snapshot.CurrentMonth.Net.Should().Be(4679.50m);
        snapshot.CurrentMonth.TransactionCount.Should().Be(3);
        snapshot.CurrentMonth.Currency.Should().Be("PLN");
    }

    [Fact]
    public async Task GetSnapshot_CategoryBreakdown_RanksSpendAndLabelsUncategorised()
    {
        await SeedAsync();
        await AddAsync(_fuel, new DateOnly(2026, 6, 4), -300m);
        await AddAsync(_groceries, new DateOnly(2026, 6, 5), -100m);
        await AddAsync(null, new DateOnly(2026, 6, 6), -100m); // uncategorised spend

        var snapshot = await NewQuery().GetSnapshotAsync(Filter(), CancellationToken.None);

        snapshot.TopCategories.Should().HaveCount(3);
        snapshot.TopCategories[0].Name.Should().Be("Paliwo");
        snapshot.TopCategories[0].Total.Should().Be(300m);
        snapshot.TopCategories[0].Share.Should().BeApproximately(0.6, 0.0001);
        snapshot.TopCategories.Should().ContainSingle(s => s.CategoryId == null && s.Name == "Bez kategorii");
    }

    [Fact]
    public async Task GetSnapshot_DailyTrend_BucketsByDayWithZeroGaps()
    {
        await SeedAsync();
        await AddAsync(_groceries, _asOf, -40m);
        await AddAsync(_groceries, _asOf.AddDays(-2), -10m);
        await AddAsync(_groceries, _asOf, -60m); // same day → summed

        var snapshot = await NewQuery().GetSnapshotAsync(Filter(), CancellationToken.None);

        snapshot.DailySpend.Should().HaveCount(30);
        snapshot.DailySpend[^1].Date.Should().Be(_asOf);
        snapshot.DailySpend[^1].Total.Should().Be(100m);
        snapshot.DailySpend.Single(p => p.Date == _asOf.AddDays(-2)).Total.Should().Be(10m);
        snapshot.DailySpend.Single(p => p.Date == _asOf.AddDays(-1)).Total.Should().Be(0m);
    }

    [Fact]
    public async Task GetSnapshot_MonthlyTrend_BucketsByCalendarMonthOverTwelveMonths()
    {
        await SeedAsync();
        await AddAsync(_groceries, new DateOnly(2026, 6, 1), -50m);
        await AddAsync(_groceries, new DateOnly(2026, 4, 15), -30m);
        await AddAsync(_groceries, new DateOnly(2025, 7, 1), -20m); // oldest still in the 12-month window

        var snapshot = await NewQuery().GetSnapshotAsync(Filter(), CancellationToken.None);

        snapshot.MonthlySpend.Should().HaveCount(12);
        snapshot.MonthlySpend[^1].Date.Should().Be(new DateOnly(2026, 6, 1));
        snapshot.MonthlySpend[^1].Total.Should().Be(50m);
        snapshot.MonthlySpend.Single(p => p.Date == new DateOnly(2026, 4, 1)).Total.Should().Be(30m);
        snapshot.MonthlySpend[0].Date.Should().Be(new DateOnly(2025, 7, 1));
        snapshot.MonthlySpend[0].Total.Should().Be(20m);
    }

    [Fact]
    public async Task GetSnapshot_ScopesToDisplayCurrency()
    {
        await SeedAsync();
        await AddAsync(_groceries, new DateOnly(2026, 6, 3), -100m);
        await AddAsync(_groceries, new DateOnly(2026, 6, 4), -999m, accountId: _accountEur, currency: "EUR");

        var snapshot = await NewQuery().GetSnapshotAsync(Filter(), CancellationToken.None);

        snapshot.CurrentMonth.Spend.Should().Be(100m, "EUR rows are outside the PLN display scope");
    }

    [Fact]
    public async Task GetSnapshot_RecentTransactions_NewestFirstCappedAtEight()
    {
        await SeedAsync();
        for (var i = 0; i < 10; i++)
        {
            await AddAsync(_groceries, _asOf.AddDays(-i), -(i + 1));
        }

        var snapshot = await NewQuery().GetSnapshotAsync(Filter(), CancellationToken.None);

        snapshot.RecentTransactions.Should().HaveCount(8);
        snapshot.RecentTransactions[0].Date.Should().Be(_asOf);
    }

    private DashboardQuery NewQuery() => new(_factory);

    private static DashboardFilter Filter() => new(Currency: "PLN", AccountId: null, AsOf: _asOf);

    private async Task SeedAsync()
    {
        await using var db = _factory.CreateDbContext();
        await db.Database.MigrateAsync();

        _accountId = Guid.NewGuid();
        _accountEur = Guid.NewGuid();
        _groceries = Guid.NewGuid();
        _fuel = Guid.NewGuid();
        _sessionId = Guid.NewGuid();

        db.Accounts.AddRange(
            new Account { Id = _accountId, Name = "PKO", BankCode = "PKO_BP", AccountNumber = "PL01", Currency = "PLN", Type = AccountType.Checking, CreatedAt = DateTime.UtcNow },
            new Account { Id = _accountEur, Name = "EUR", BankCode = "PKO_BP", AccountNumber = "PL02", Currency = "EUR", Type = AccountType.Checking, CreatedAt = DateTime.UtcNow });
        db.Categories.AddRange(
            new Category { Id = _groceries, Name = "Spożywcze", Color = "#34C759" },
            new Category { Id = _fuel, Name = "Paliwo", Color = "#FF9500" });
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
        await db.SaveChangesAsync();
    }

    private async Task AddAsync(
        Guid? categoryId,
        DateOnly date,
        decimal amount,
        Guid? accountId = null,
        string currency = "PLN")
    {
        await using var db = _factory.CreateDbContext();
        db.Transactions.Add(new Transaction
        {
            Id = Guid.NewGuid(),
            AccountId = accountId ?? _accountId,
            ImportSessionId = _sessionId,
            CategoryId = categoryId,
            Date = date,
            Amount = amount,
            Currency = currency,
            Description = "tx",
            NormalizedDescription = "TX",
            Hash = Guid.NewGuid().ToString("N"),
            CreatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
    }
}
