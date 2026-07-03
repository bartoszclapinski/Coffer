using System.Security.Cryptography;
using System.Text.Json;
using Coffer.Core.Budgeting;
using Coffer.Core.Domain;
using Coffer.Infrastructure.Budgeting;
using Coffer.Infrastructure.Chat;
using Coffer.Infrastructure.DependencyInjection;
using Coffer.Infrastructure.Persistence;
using Coffer.Infrastructure.Tests.Persistence;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Coffer.Infrastructure.Tests.Chat;

/// <summary>
/// The <see cref="GetBudgetStatusTool"/> over a real SQLCipher database (no mocks). Covers the live
/// engine status returned for a budgeted category, the unbudgeted (uncategorised) line, and
/// discoverability of the tool through the <c>AddCofferChat</c> registration so <c>ChatService</c> can
/// find it.
/// </summary>
public class GetBudgetStatusToolTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _dbPath;
    private readonly SqliteTestDbContextFactory _factory;

    private Guid _accountId;
    private Guid _sessionId;

    public GetBudgetStatusToolTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "Coffer.Tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _dbPath = Path.Combine(_tempDir, "coffer.db");
        var dek = RandomNumberGenerator.GetBytes(32);
        _factory = new SqliteTestDbContextFactory(_dbPath, dek);
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
    public async Task GetBudgetStatus_ReturnsOverZoneAndUnbudgetedBucket()
    {
        await SeedAsync();

        var json = await Tool().ExecuteAsync("{}", CancellationToken.None);

        var root = Root(json);
        root.GetProperty("month").GetString().Should().Be("2026-06");
        root.GetProperty("count").GetInt32().Should().Be(1);

        var budget = root.GetProperty("budgets")[0];
        budget.GetProperty("category").GetString().Should().Be("Spożywcze");
        budget.GetProperty("limit").GetDecimal().Should().Be(100m);
        budget.GetProperty("spent").GetDecimal().Should().Be(150m);
        budget.GetProperty("zone").GetString().Should().Be("Over");

        var unbudgeted = root.GetProperty("unbudgeted");
        unbudgeted.GetArrayLength().Should().Be(1);
        unbudgeted[0].GetProperty("category").GetString().Should().Be("Bez kategorii");
        unbudgeted[0].GetProperty("spent").GetDecimal().Should().Be(30m);
    }

    [Fact]
    public async Task GetBudgetStatus_ReturnsEmpty_WhenNoBudgetsSet()
    {
        await MigrateAsync();

        var json = await Tool().ExecuteAsync("{}", CancellationToken.None);

        var root = Root(json);
        root.GetProperty("count").GetInt32().Should().Be(0);
        root.GetProperty("budgets").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public void GetBudgetStatus_IsRegisteredAsChatTool_DiscoverableByChatService()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IDbContextFactory<CofferDbContext>>(_factory);
        services.AddCofferChat();
        services.AddCofferGoals(); // GetGoalsTool (also in the chat menu) depends on the goals engine.
        services.AddCofferPlanning(); // GetCashFlowProjectionTool / CanIAffordTool depend on the planning spine.
        services.AddCofferBudgeting();

        using var provider = services.BuildServiceProvider();
        var toolNames = provider.GetServices<IChatTool>().Select(t => t.Name).ToList();

        toolNames.Should().Contain("GetBudgetStatus");
    }

    private GetBudgetStatusTool Tool() =>
        new(_factory, new BudgetTrackingQuery(_factory, new BudgetTrackingEngine()));

    private static JsonElement Root(string json) => JsonDocument.Parse(json).RootElement;

    private async Task MigrateAsync()
    {
        await using var db = _factory.CreateDbContext();
        await db.Database.MigrateAsync();
    }

    private async Task SeedAsync()
    {
        await using var db = _factory.CreateDbContext();
        await db.Database.MigrateAsync();

        _accountId = Guid.NewGuid();
        _sessionId = Guid.NewGuid();
        var categoryId = Guid.NewGuid();

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
        db.Categories.Add(new Category { Id = categoryId, Name = "Spożywcze", Color = "#0F0" });
        db.CategoryBudgets.Add(new CategoryBudget
        {
            Id = Guid.NewGuid(),
            CategoryId = categoryId,
            LimitAmount = 100m,
            Currency = "PLN",
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
        });

        // Budgeted category spends 150 zł this month (over the 100 zł limit); an uncategorised 30 zł debit
        // must surface as an unbudgeted line rather than hide.
        db.Transactions.Add(Tx(new DateOnly(2026, 6, 5), -80m, categoryId));
        db.Transactions.Add(Tx(new DateOnly(2026, 6, 12), -70m, categoryId));
        db.Transactions.Add(Tx(new DateOnly(2026, 6, 19), -30m, categoryId: null));
        await db.SaveChangesAsync();
    }

    private Transaction Tx(DateOnly date, decimal amount, Guid? categoryId) => new()
    {
        Id = Guid.NewGuid(),
        AccountId = _accountId,
        ImportSessionId = _sessionId,
        Date = date,
        Amount = amount,
        Currency = "PLN",
        Description = "TX",
        NormalizedDescription = "TX",
        Merchant = null,
        CategoryId = categoryId,
        Hash = Guid.NewGuid().ToString("N"),
        CreatedAt = DateTime.UtcNow,
    };
}
