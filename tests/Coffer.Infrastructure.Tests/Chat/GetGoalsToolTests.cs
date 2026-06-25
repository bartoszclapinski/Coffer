using System.Security.Cryptography;
using System.Text.Json;
using Coffer.Core.Chat;
using Coffer.Core.Domain;
using Coffer.Core.Goals;
using Coffer.Infrastructure.Chat;
using Coffer.Infrastructure.DependencyInjection;
using Coffer.Infrastructure.Goals;
using Coffer.Infrastructure.Goals.Strategies;
using Coffer.Infrastructure.Persistence;
using Coffer.Infrastructure.Tests.Persistence;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Coffer.Infrastructure.Tests.Chat;

/// <summary>
/// The <see cref="GetGoalsTool"/> over a real SQLCipher database (no mocks). Covers the live engine
/// projection returned for active goals, exclusion of archived goals, and discoverability of the tool
/// through the <c>AddCofferChat</c> registration so <c>ChatService</c> can find it.
/// </summary>
public class GetGoalsToolTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _dbPath;
    private readonly SqliteTestDbContextFactory _factory;

    public GetGoalsToolTests()
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
    public async Task GetGoals_ReturnsActiveGoalWithEngineProjection()
    {
        await MigrateAsync();
        await SeedAsync(Goal("Wakacje", new DateOnly(2027, 7, 1)));

        var json = await Tool().ExecuteAsync("{}", CancellationToken.None);

        var root = Root(json);
        root.GetProperty("count").GetInt32().Should().Be(1);
        var goal = root.GetProperty("goals")[0];
        goal.GetProperty("name").GetString().Should().Be("Wakacje");
        goal.GetProperty("target").GetDecimal().Should().Be(8000m);
        goal.GetProperty("status").GetString().Should().NotBeNullOrEmpty();
        goal.GetProperty("requiredMonthlySaving").GetDecimal().Should().BeGreaterThan(0m);
        goal.TryGetProperty("projectedDate", out _).Should().BeTrue();
    }

    [Fact]
    public async Task GetGoals_ExcludesArchivedGoals()
    {
        await MigrateAsync();
        await SeedAsync(Goal("Active", new DateOnly(2027, 7, 1)));
        await SeedAsync(Goal("Archived", new DateOnly(2027, 7, 1), archived: true));

        var json = await Tool().ExecuteAsync("{}", CancellationToken.None);

        var root = Root(json);
        root.GetProperty("count").GetInt32().Should().Be(1);
        root.GetProperty("goals")[0].GetProperty("name").GetString().Should().Be("Active");
    }

    [Fact]
    public void GetGoals_IsRegisteredAsChatTool_DiscoverableByChatService()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IDbContextFactory<CofferDbContext>>(_factory);
        services.AddCofferChat();
        services.AddCofferGoals();

        using var provider = services.BuildServiceProvider();
        var toolNames = provider.GetServices<IChatTool>().Select(t => t.Name).ToList();

        toolNames.Should().Contain("GetGoals");
    }

    private GetGoalsTool Tool()
    {
        var engine = new GoalFeasibilityEngine(
        [
            new PurchaseGoalStrategy(),
            new LargeExpenseGoalStrategy(),
            new EmergencyFundGoalStrategy(),
            new MortgagePrepaymentGoalStrategy(),
            new InvestmentGoalStrategy(),
            new LongTermGoalStrategy(),
        ]);
        return new GetGoalsTool(
            _factory,
            new GoalsQuery(_factory),
            new FinancialContextBuilder(_factory),
            engine);
    }

    private static JsonElement Root(string json) => JsonDocument.Parse(json).RootElement;

    private async Task MigrateAsync()
    {
        await using var db = _factory.CreateDbContext();
        await db.Database.MigrateAsync();
    }

    private async Task SeedAsync(Goal goal)
    {
        await using var db = _factory.CreateDbContext();
        db.Goals.Add(goal);
        await db.SaveChangesAsync();
    }

    private static Goal Goal(string name, DateOnly targetDate, bool archived = false) => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        Type = GoalType.Purchase,
        TargetAmount = 8000m,
        Currency = "PLN",
        TargetDate = targetDate,
        Priority = Priority.Medium,
        IsArchived = archived,
        CreatedAt = DateTime.UtcNow,
    };
}
