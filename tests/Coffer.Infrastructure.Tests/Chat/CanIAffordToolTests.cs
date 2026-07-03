using System.Security.Cryptography;
using System.Text.Json;
using Coffer.Core.Chat;
using Coffer.Core.Domain;
using Coffer.Infrastructure.Chat;
using Coffer.Infrastructure.DependencyInjection;
using Coffer.Infrastructure.Persistence;
using Coffer.Infrastructure.Planning;
using Coffer.Infrastructure.Tests.Persistence;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Coffer.Infrastructure.Tests.Chat;

/// <summary>
/// The <see cref="CanIAffordTool"/> over a real SQLCipher database (no mocks). Proves it shapes the
/// deterministic <c>AffordabilityEngine</c> verdict as JSON, flags relative/uncertain balances, and is
/// wired into <c>AddCofferChat</c>. It takes no <c>IAiProvider</c>, so a successful run is also evidence
/// that the tool makes zero provider calls — the numbers come from the engine, the assistant narrates.
/// </summary>
public class CanIAffordToolTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _dbPath;
    private readonly SqliteTestDbContextFactory _factory;

    public CanIAffordToolTests()
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
    public async Task AnchoredAccount_ReturnsGroundedVerdict_WithDriver()
    {
        await MigrateAsync();
        var account = await SeedAnchoredAccountAsync(anchorDate: new DateOnly(2026, 1, 1), anchorBalance: 5000m);
        await SeedFlowAsync(Monthly("Salary", FlowDirection.Inflow, 6000m, day: 25));
        await SeedFlowAsync(Monthly("Rent", FlowDirection.Outflow, 2000m, day: 10));

        var args = $$"""{"amount": 1000, "date": "2026-01-05", "accountId": "{{account}}"}""";
        var root = Root(await Tool().ExecuteAsync(args, CancellationToken.None));

        root.GetProperty("canAfford").GetBoolean().Should().BeTrue();
        root.GetProperty("openingBalance").GetDecimal().Should().Be(5000m);
        root.GetProperty("lowestBalance").GetDecimal().Should().Be(2000m);
        root.GetProperty("headroom").GetDecimal().Should().Be(2000m);
        root.GetProperty("nextInflowDate").GetString().Should().Be("2026-01-25");
        root.GetProperty("driver").GetProperty("name").GetString().Should().Be("Rent");
        root.GetProperty("isRelative").GetBoolean().Should().BeFalse();
        root.GetProperty("isUncertain").GetBoolean().Should().BeFalse();
        root.GetProperty("currency").GetString().Should().Be("PLN");
    }

    [Fact]
    public async Task NoAccountId_IsFlaggedRelative()
    {
        await MigrateAsync();
        await SeedFlowAsync(Monthly("Salary", FlowDirection.Inflow, 6000m, day: 25));

        var root = Root(await Tool().ExecuteAsync("""{"amount": 100, "date": "2026-01-05"}""", CancellationToken.None));

        root.GetProperty("isRelative").GetBoolean().Should().BeTrue();
        root.TryGetProperty("canAfford", out _).Should().BeTrue();
    }

    [Fact]
    public async Task MissingAmount_ReturnsError()
    {
        await MigrateAsync();

        var root = Root(await Tool().ExecuteAsync("{}", CancellationToken.None));

        root.TryGetProperty("error", out _).Should().BeTrue();
    }

    [Fact]
    public async Task InvalidAccountId_ReturnsError()
    {
        await MigrateAsync();

        var root = Root(await Tool().ExecuteAsync("""{"amount": 100, "accountId": "not-a-guid"}""", CancellationToken.None));

        root.TryGetProperty("error", out _).Should().BeTrue();
    }

    [Fact]
    public void CanIAfford_IsRegisteredAsChatTool_DiscoverableByChatService()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IDbContextFactory<CofferDbContext>>(_factory);
        services.AddCofferChat();
        services.AddCofferGoals();
        services.AddCofferPlanning();
        services.AddCofferBudgeting(); // GetBudgetStatusTool depends on the budget tracking query.

        using var provider = services.BuildServiceProvider();
        var toolNames = provider.GetServices<IChatTool>().Select(t => t.Name).ToList();

        toolNames.Should().Contain("CanIAfford");
    }

    private CanIAffordTool Tool() => new(
        _factory,
        new RunningBalanceQuery(_factory),
        new BalanceTrustQuery(_factory, new StatementContinuityChecker(_factory)),
        new VariableBurnQuery(_factory),
        new Coffer.Infrastructure.AI.AppSettingsStore(_factory),
        new Core.Planning.AffordabilityEngine(new Core.Planning.CashFlowProjectionEngine()));

    private static JsonElement Root(string json) => JsonDocument.Parse(json).RootElement;

    private async Task MigrateAsync()
    {
        await using var db = _factory.CreateDbContext();
        await db.Database.MigrateAsync();
    }

    private async Task<Guid> SeedAnchoredAccountAsync(DateOnly anchorDate, decimal anchorBalance)
    {
        var id = Guid.NewGuid();
        await using var db = _factory.CreateDbContext();
        db.Accounts.Add(new Account
        {
            Id = id,
            Name = "Test account",
            BankCode = "PKO_BP",
            AccountNumber = "PL00000000000000000000000000",
            Currency = "PLN",
            Type = AccountType.Checking,
            CreatedAt = DateTime.UtcNow,
            AnchorDate = anchorDate,
            AnchorBalance = anchorBalance,
        });
        await db.SaveChangesAsync();
        return id;
    }

    private async Task SeedFlowAsync(RecurringFlow flow)
    {
        await using var db = _factory.CreateDbContext();
        db.RecurringFlows.Add(flow);
        await db.SaveChangesAsync();
    }

    private static RecurringFlow Monthly(string name, FlowDirection direction, decimal amount, int day) => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        Direction = direction,
        IntervalMonths = 1,
        AnchorDayOfMonth = day,
        TypicalAmount = amount,
        Currency = "PLN",
        IsActive = true,
        Source = FlowSource.Manual,
        CreatedAt = DateTime.UtcNow,
    };
}
