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
/// The <see cref="GetCashFlowProjectionTool"/> over a real SQLCipher database (no mocks). Covers the
/// dated projection returned for active flows, exclusion of inactive flows, the empty-flows shape, the
/// horizon clamp, and discoverability of the tool through the <c>AddCofferChat</c> registration so
/// <c>ChatService</c> can find it. The numbers come from the deterministic engine — the tool only
/// surfaces them.
/// </summary>
public class GetCashFlowProjectionToolTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _dbPath;
    private readonly SqliteTestDbContextFactory _factory;

    public GetCashFlowProjectionToolTests()
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
    public async Task GetCashFlowProjection_ReturnsDatedProjection_ForActiveFlows()
    {
        await MigrateAsync();
        await SeedAsync(Monthly("Rata", FlowDirection.Outflow, 500m, day: 15));

        var json = await Tool().ExecuteAsync("""{"horizonDays": 120}""", CancellationToken.None);

        var root = Root(json);
        root.GetProperty("horizonDays").GetInt32().Should().Be(120);
        root.GetProperty("currency").GetString().Should().Be("PLN");
        root.GetProperty("eventCount").GetInt32().Should().BeGreaterThan(0);
        var ev = root.GetProperty("events")[0];
        ev.GetProperty("name").GetString().Should().Be("Rata");
        ev.GetProperty("direction").GetString().Should().Be("Outflow");
        ev.TryGetProperty("date", out _).Should().BeTrue();
        ev.TryGetProperty("balanceAfter", out _).Should().BeTrue();
        ev.TryGetProperty("accrualPeriod", out _).Should().BeTrue();
    }

    [Fact]
    public async Task GetCashFlowProjection_NoFlows_ReturnsEmptyProjection()
    {
        await MigrateAsync();

        var json = await Tool().ExecuteAsync("{}", CancellationToken.None);

        var root = Root(json);
        root.GetProperty("eventCount").GetInt32().Should().Be(0);
        root.GetProperty("events").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task GetCashFlowProjection_ExcludesInactiveFlows()
    {
        await MigrateAsync();
        await SeedAsync(Monthly("Disabled", FlowDirection.Outflow, 500m, day: 15, active: false));

        var json = await Tool().ExecuteAsync("{}", CancellationToken.None);

        Root(json).GetProperty("eventCount").GetInt32().Should().Be(0);
    }

    [Fact]
    public async Task GetCashFlowProjection_ClampsHorizonToMax()
    {
        await MigrateAsync();

        var json = await Tool().ExecuteAsync("""{"horizonDays": 9999}""", CancellationToken.None);

        Root(json).GetProperty("horizonDays").GetInt32().Should().Be(365);
    }

    [Fact]
    public void GetCashFlowProjection_IsRegisteredAsChatTool_DiscoverableByChatService()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IDbContextFactory<CofferDbContext>>(_factory);
        services.AddCofferChat();
        services.AddCofferGoals();
        services.AddCofferPlanning();

        using var provider = services.BuildServiceProvider();
        var toolNames = provider.GetServices<IChatTool>().Select(t => t.Name).ToList();

        toolNames.Should().Contain("GetCashFlowProjection");
    }

    private GetCashFlowProjectionTool Tool() => new(
        _factory,
        new RecurringFlowRepository(_factory),
        new RunningBalanceQuery(_factory),
        new Core.Planning.CashFlowProjectionEngine());

    private static JsonElement Root(string json) => JsonDocument.Parse(json).RootElement;

    private async Task MigrateAsync()
    {
        await using var db = _factory.CreateDbContext();
        await db.Database.MigrateAsync();
    }

    private async Task SeedAsync(RecurringFlow flow)
    {
        await using var db = _factory.CreateDbContext();
        db.RecurringFlows.Add(flow);
        await db.SaveChangesAsync();
    }

    private static RecurringFlow Monthly(
        string name, FlowDirection direction, decimal amount, int day, bool active = true) => new()
        {
            Id = Guid.NewGuid(),
            Name = name,
            Direction = direction,
            IntervalMonths = 1,
            AnchorDayOfMonth = day,
            TypicalAmount = amount,
            Currency = "PLN",
            IsActive = active,
            Source = FlowSource.Manual,
            CreatedAt = DateTime.UtcNow,
        };
}
