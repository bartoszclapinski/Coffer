using System.Security.Cryptography;
using Coffer.Core.Domain;
using Coffer.Core.Transactions;
using Coffer.Infrastructure.Persistence;
using Coffer.Infrastructure.Tests.Persistence;
using Coffer.Infrastructure.Transactions;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Coffer.Infrastructure.Tests.Transactions;

public class GetTransactionsQueryTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _dbPath;
    private readonly byte[] _dek;
    private readonly SqliteTestDbContextFactory _factory;

    private Guid _accountA;
    private Guid _accountB;
    private Guid _categoryId;
    private Guid _sessionId;

    public GetTransactionsQueryTests()
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
    public async Task Execute_DefaultWindow_ExcludesOlderThanSixMonths()
    {
        await SeedAsync();
        var query = new GetTransactionsQuery(_factory);
        var recent = DateOnly.FromDateTime(DateTime.UtcNow).AddMonths(-1);
        var old = DateOnly.FromDateTime(DateTime.UtcNow).AddMonths(-9);

        await AddTransactionAsync(_accountA, recent, -10m, "Recent within window");
        await AddTransactionAsync(_accountA, old, -20m, "Old outside window");

        var result = await query.ExecuteAsync(new TransactionQueryFilter(), CancellationToken.None);

        result.Should().ContainSingle();
        result[0].Description.Should().Be("Recent within window");
    }

    [Fact]
    public async Task Execute_NewestFirst()
    {
        await SeedAsync();
        var query = new GetTransactionsQuery(_factory);
        var baseDate = DateOnly.FromDateTime(DateTime.UtcNow).AddMonths(-1);

        await AddTransactionAsync(_accountA, baseDate.AddDays(-2), -1m, "Older");
        await AddTransactionAsync(_accountA, baseDate, -2m, "Newer");

        var result = await query.ExecuteAsync(new TransactionQueryFilter(), CancellationToken.None);

        result.Select(r => r.Description).Should().Equal("Newer", "Older");
    }

    [Fact]
    public async Task Execute_FilterByAccount_NarrowsResults()
    {
        await SeedAsync();
        var query = new GetTransactionsQuery(_factory);
        var date = DateOnly.FromDateTime(DateTime.UtcNow).AddMonths(-1);

        await AddTransactionAsync(_accountA, date, -1m, "On A");
        await AddTransactionAsync(_accountB, date, -2m, "On B");

        var result = await query.ExecuteAsync(
            new TransactionQueryFilter(AccountId: _accountB), CancellationToken.None);

        result.Should().ContainSingle();
        result[0].Description.Should().Be("On B");
        result[0].AccountName.Should().Be("Account B");
    }

    [Fact]
    public async Task Execute_FilterByCategory_NarrowsResults()
    {
        await SeedAsync();
        var query = new GetTransactionsQuery(_factory);
        var date = DateOnly.FromDateTime(DateTime.UtcNow).AddMonths(-1);

        await AddTransactionAsync(_accountA, date, -1m, "Categorised", _categoryId);
        await AddTransactionAsync(_accountA, date, -2m, "Uncategorised");

        var result = await query.ExecuteAsync(
            new TransactionQueryFilter(CategoryId: _categoryId), CancellationToken.None);

        result.Should().ContainSingle();
        result[0].Description.Should().Be("Categorised");
        result[0].CategoryName.Should().Be("Groceries");
    }

    [Fact]
    public async Task Execute_FilterBySearch_MatchesDescriptionOrMerchant()
    {
        await SeedAsync();
        var query = new GetTransactionsQuery(_factory);
        var date = DateOnly.FromDateTime(DateTime.UtcNow).AddMonths(-1);

        await AddTransactionAsync(_accountA, date, -1m, "Biedronka zakupy");
        await AddTransactionAsync(_accountA, date, -2m, "Apteka", merchant: "Biedronka pharmacy");
        await AddTransactionAsync(_accountA, date, -3m, "Paliwo Orlen");

        var result = await query.ExecuteAsync(
            new TransactionQueryFilter(Search: "Biedronka"), CancellationToken.None);

        result.Should().HaveCount(2);
        result.Select(r => r.Description).Should().Contain(["Biedronka zakupy", "Apteka"]);
    }

    [Fact]
    public async Task GetAccounts_ReturnsNonArchivedOrderedByName()
    {
        await SeedAsync();
        var query = new GetTransactionsQuery(_factory);

        var accounts = await query.GetAccountsAsync(CancellationToken.None);

        accounts.Select(a => a.Name).Should().Equal("Account A", "Account B");
    }

    private async Task SeedAsync()
    {
        await using var db = _factory.CreateDbContext();
        await db.Database.MigrateAsync();

        _accountA = Guid.NewGuid();
        _accountB = Guid.NewGuid();
        _categoryId = Guid.NewGuid();
        _sessionId = Guid.NewGuid();

        db.Accounts.AddRange(
            new Account { Id = _accountA, Name = "Account A", BankCode = "PKO_BP", AccountNumber = "PL01", Currency = "PLN", Type = AccountType.Checking, CreatedAt = DateTime.UtcNow },
            new Account { Id = _accountB, Name = "Account B", BankCode = "PKO_BP", AccountNumber = "PL02", Currency = "PLN", Type = AccountType.Savings, CreatedAt = DateTime.UtcNow });
        db.Categories.Add(new Category { Id = _categoryId, Name = "Groceries", Color = "#534AB7" });
        db.ImportSessions.Add(new ImportSession
        {
            Id = _sessionId,
            FileName = "seed.csv",
            FileHash = "SEEDHASH",
            BankCode = "PKO_BP",
            PeriodFrom = new DateOnly(2026, 1, 1),
            PeriodTo = new DateOnly(2026, 1, 31),
            ImportedAt = DateTime.UtcNow,
            Status = ImportStatus.Completed,
        });
        await db.SaveChangesAsync();
    }

    private async Task AddTransactionAsync(
        Guid accountId,
        DateOnly date,
        decimal amount,
        string description,
        Guid? categoryId = null,
        string? merchant = null)
    {
        await using var db = _factory.CreateDbContext();
        db.Transactions.Add(new Transaction
        {
            Id = Guid.NewGuid(),
            AccountId = accountId,
            ImportSessionId = _sessionId,
            CategoryId = categoryId,
            Date = date,
            Amount = amount,
            Currency = "PLN",
            Description = description,
            NormalizedDescription = description.ToUpperInvariant(),
            Merchant = merchant,
            Hash = Guid.NewGuid().ToString("N"),
            CreatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
    }
}
