using System.Security.Cryptography;
using System.Text.Json;
using Coffer.Core.Domain;
using Coffer.Infrastructure.Chat;
using Coffer.Infrastructure.Tests.Persistence;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Coffer.Infrastructure.Tests.Chat;

/// <summary>
/// The four read-only chat tools, exercised over a real SQLCipher database (no mocks, no real API
/// calls). Covers server-side sums, the category breakdown, the monthly trend, Polish category-name
/// resolution, unknown-category handling, date validation, and PLN currency scoping.
/// </summary>
public class ChatToolsTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _dbPath;
    private readonly byte[] _dek;
    private readonly SqliteTestDbContextFactory _factory;

    private Guid _accountId;
    private Guid _accountEur;
    private Guid _groceries;
    private Guid _fuel;
    private Guid _sessionId;

    public ChatToolsTests()
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

        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task GetTotalSpent_SumsDebitsInRange_AsPositiveMagnitude()
    {
        await SeedAsync();
        await AddAsync(_groceries, new DateOnly(2026, 6, 3), -120.50m);
        await AddAsync(_fuel, new DateOnly(2026, 6, 10), -200m);
        await AddAsync(null, new DateOnly(2026, 6, 12), 5000m); // income excluded
        await AddAsync(_groceries, new DateOnly(2026, 5, 30), -999m); // out of range excluded

        var json = await new GetTotalSpentTool(_factory)
            .ExecuteAsync("""{"from":"2026-06-01","to":"2026-06-30"}""", CancellationToken.None);

        Root(json).GetProperty("totalSpent").GetDecimal().Should().Be(320.50m);
        Root(json).GetProperty("currency").GetString().Should().Be("PLN");
    }

    [Fact]
    public async Task GetTotalSpent_FiltersByCategoryName_CaseInsensitive()
    {
        await SeedAsync();
        await AddAsync(_fuel, new DateOnly(2026, 6, 4), -300m);
        await AddAsync(_groceries, new DateOnly(2026, 6, 5), -100m);

        var json = await new GetTotalSpentTool(_factory)
            .ExecuteAsync("""{"from":"2026-06-01","to":"2026-06-30","category":"paliwo"}""", CancellationToken.None);

        Root(json).GetProperty("totalSpent").GetDecimal().Should().Be(300m);
    }

    [Fact]
    public async Task GetTotalSpent_UnknownCategory_ReturnsZeroNotError()
    {
        await SeedAsync();
        await AddAsync(_fuel, new DateOnly(2026, 6, 4), -300m);

        var json = await new GetTotalSpentTool(_factory)
            .ExecuteAsync("""{"from":"2026-06-01","to":"2026-06-30","category":"Nieistniejąca"}""", CancellationToken.None);

        Root(json).TryGetProperty("error", out _).Should().BeFalse();
        Root(json).GetProperty("totalSpent").GetDecimal().Should().Be(0m);
    }

    [Fact]
    public async Task GetTotalSpent_ScopesToPlnDisplayCurrency()
    {
        await SeedAsync();
        await AddAsync(_groceries, new DateOnly(2026, 6, 3), -100m);
        await AddAsync(_groceries, new DateOnly(2026, 6, 4), -999m, accountId: _accountEur, currency: "EUR");

        var json = await new GetTotalSpentTool(_factory)
            .ExecuteAsync("""{"from":"2026-06-01","to":"2026-06-30"}""", CancellationToken.None);

        Root(json).GetProperty("totalSpent").GetDecimal().Should().Be(100m, "EUR rows are outside the PLN scope");
    }

    [Theory]
    [InlineData("""{"from":"2026-06-30","to":"2026-06-01"}""")] // from after to
    [InlineData("""{"to":"2026-06-01"}""")] // missing from
    [InlineData("""{"from":"not-a-date","to":"2026-06-01"}""")] // unparseable
    public async Task GetTotalSpent_InvalidDates_ReturnsErrorPayload(string args)
    {
        await SeedAsync();

        var json = await new GetTotalSpentTool(_factory).ExecuteAsync(args, CancellationToken.None);

        Root(json).TryGetProperty("error", out _).Should().BeTrue();
    }

    [Fact]
    public async Task GetSpendingByCategory_RanksSpendAndLabelsUncategorised()
    {
        await SeedAsync();
        await AddAsync(_fuel, new DateOnly(2026, 6, 4), -300m);
        await AddAsync(_groceries, new DateOnly(2026, 6, 5), -100m);
        await AddAsync(null, new DateOnly(2026, 6, 6), -50m); // uncategorised

        var json = await new GetSpendingByCategoryTool(_factory)
            .ExecuteAsync("""{"from":"2026-06-01","to":"2026-06-30"}""", CancellationToken.None);

        var categories = Root(json).GetProperty("categories");
        categories.GetArrayLength().Should().Be(3);
        categories[0].GetProperty("category").GetString().Should().Be("Paliwo");
        categories[0].GetProperty("total").GetDecimal().Should().Be(300m);
        var labels = categories.EnumerateArray().Select(c => c.GetProperty("category").GetString()).ToList();
        labels.Should().Contain("Bez kategorii");
    }

    [Fact]
    public async Task GetMonthlyTrend_ReturnsContiguousSeriesWithCurrentMonthLast()
    {
        await SeedAsync();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var thisMonth = new DateOnly(today.Year, today.Month, 10);
        await AddAsync(_groceries, thisMonth, -75m);

        var json = await new GetMonthlyTrendTool(_factory)
            .ExecuteAsync("""{"category":"Spożywcze","months":6}""", CancellationToken.None);

        var months = Root(json).GetProperty("months");
        months.GetArrayLength().Should().Be(6);
        months[5].GetProperty("month").GetString().Should().Be($"{today.Year:D4}-{today.Month:D2}");
        months[5].GetProperty("total").GetDecimal().Should().Be(75m);
    }

    [Fact]
    public async Task GetMonthlyTrend_UnknownCategory_ReturnsAllZeroSeries()
    {
        await SeedAsync();

        var json = await new GetMonthlyTrendTool(_factory)
            .ExecuteAsync("""{"category":"Nieistniejąca","months":3}""", CancellationToken.None);

        var months = Root(json).GetProperty("months");
        months.GetArrayLength().Should().Be(3);
        months.EnumerateArray().Should().OnlyContain(m => m.GetProperty("total").GetDecimal() == 0m);
    }

    [Fact]
    public async Task GetTransactions_NewestFirst_RespectsLimitAndFilters()
    {
        await SeedAsync();
        await AddAsync(_fuel, new DateOnly(2026, 6, 1), -10m, merchant: "ORLEN");
        await AddAsync(_groceries, new DateOnly(2026, 6, 2), -20m, merchant: "BIEDRONKA");
        await AddAsync(_fuel, new DateOnly(2026, 6, 3), -30m, merchant: "BP");

        var json = await new GetTransactionsTool(_factory)
            .ExecuteAsync("""{"from":"2026-06-01","to":"2026-06-30","limit":2}""", CancellationToken.None);

        var txs = Root(json).GetProperty("transactions");
        txs.GetArrayLength().Should().Be(2);
        txs[0].GetProperty("date").GetString().Should().Be("2026-06-03");
        Root(json).GetProperty("count").GetInt32().Should().Be(2);
    }

    [Fact]
    public async Task GetTransactions_MerchantPattern_FiltersBySubstring()
    {
        await SeedAsync();
        await AddAsync(_fuel, new DateOnly(2026, 6, 1), -10m, merchant: "ORLEN STACJA");
        await AddAsync(_groceries, new DateOnly(2026, 6, 2), -20m, merchant: "BIEDRONKA");

        var json = await new GetTransactionsTool(_factory)
            .ExecuteAsync("""{"from":"2026-06-01","to":"2026-06-30","merchantPattern":"ORLEN"}""", CancellationToken.None);

        var txs = Root(json).GetProperty("transactions");
        txs.GetArrayLength().Should().Be(1);
        txs[0].GetProperty("merchant").GetString().Should().Contain("ORLEN");
    }

    [Fact]
    public async Task GetTransactions_ClampsLimitToMax()
    {
        await SeedAsync();
        for (var i = 0; i < 60; i++)
        {
            await AddAsync(_groceries, new DateOnly(2026, 6, 1).AddDays(i % 28), -(i + 1));
        }

        var json = await new GetTransactionsTool(_factory)
            .ExecuteAsync("""{"from":"2026-06-01","to":"2026-07-31","limit":999}""", CancellationToken.None);

        Root(json).GetProperty("transactions").GetArrayLength().Should().Be(50);
    }

    private static JsonElement Root(string json) => JsonDocument.Parse(json).RootElement;

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
        string currency = "PLN",
        string? merchant = null)
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
            Description = merchant ?? "tx",
            NormalizedDescription = (merchant ?? "TX").ToUpperInvariant(),
            Merchant = merchant,
            Hash = Guid.NewGuid().ToString("N"),
            CreatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
    }
}
