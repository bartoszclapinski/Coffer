using System.Security.Cryptography;
using System.Text.Json;
using Coffer.Core.Anomalies;
using Coffer.Core.Chat;
using Coffer.Core.Domain;
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
/// The <see cref="FindAnomaliesTool"/> over a real SQLCipher database (no mocks). Covers the
/// period-overlap filter, exclusion of dismissed alerts, date validation, and discoverability of the
/// tool through the <c>AddCofferChat</c> registration so <c>ChatService</c> can find it.
/// </summary>
public class FindAnomaliesToolTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _dbPath;
    private readonly SqliteTestDbContextFactory _factory;

    public FindAnomaliesToolTests()
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
    public async Task Find_ReturnsActiveAlertsOverlappingRange_NewestFirst()
    {
        await SeedAsync(
            Alert("new-merchant:a", AnomalyType.NewMerchant, new DateOnly(2026, 6, 5), detectedDaysAgo: 2),
            Alert("high-amount:b", AnomalyType.HighAmountInCategory, new DateOnly(2026, 6, 20), detectedDaysAgo: 1));

        var json = await new FindAnomaliesTool(_factory)
            .ExecuteAsync("""{"from":"2026-06-01","to":"2026-06-30"}""", CancellationToken.None);

        var anomalies = Root(json).GetProperty("anomalies");
        anomalies.GetArrayLength().Should().Be(2);
        anomalies[0].GetProperty("type").GetString().Should().Be("HighAmountInCategory");
        Root(json).GetProperty("count").GetInt32().Should().Be(2);
    }

    [Fact]
    public async Task Find_ExcludesAlertsOutsideRange()
    {
        await SeedAsync(
            Alert("new-merchant:in", AnomalyType.NewMerchant, new DateOnly(2026, 6, 10)),
            Alert("new-merchant:out", AnomalyType.NewMerchant, new DateOnly(2026, 8, 10)));

        var json = await new FindAnomaliesTool(_factory)
            .ExecuteAsync("""{"from":"2026-06-01","to":"2026-06-30"}""", CancellationToken.None);

        var anomalies = Root(json).GetProperty("anomalies");
        anomalies.GetArrayLength().Should().Be(1);
        anomalies[0].GetProperty("title").GetString().Should().Be("new-merchant:in");
    }

    [Fact]
    public async Task Find_ExcludesDismissedAlerts()
    {
        await SeedAsync(
            Alert("new-merchant:kept", AnomalyType.NewMerchant, new DateOnly(2026, 6, 10)),
            Alert("new-merchant:gone", AnomalyType.NewMerchant, new DateOnly(2026, 6, 11), status: AlertStatus.Dismissed));

        var json = await new FindAnomaliesTool(_factory)
            .ExecuteAsync("""{"from":"2026-06-01","to":"2026-06-30"}""", CancellationToken.None);

        Root(json).GetProperty("anomalies").GetArrayLength().Should().Be(1);
        Root(json).GetProperty("anomalies")[0].GetProperty("title").GetString().Should().Be("new-merchant:kept");
    }

    [Theory]
    [InlineData("""{"from":"2026-06-30","to":"2026-06-01"}""")] // from after to
    [InlineData("""{"to":"2026-06-01"}""")] // missing from
    [InlineData("""{"from":"not-a-date","to":"2026-06-01"}""")] // unparseable
    public async Task Find_InvalidDates_ReturnsErrorPayload(string args)
    {
        await SeedAsync();

        var json = await new FindAnomaliesTool(_factory).ExecuteAsync(args, CancellationToken.None);

        Root(json).TryGetProperty("error", out _).Should().BeTrue();
    }

    [Fact]
    public void FindAnomalies_IsRegisteredAsChatTool_DiscoverableByChatService()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IDbContextFactory<CofferDbContext>>(_factory);
        services.AddCofferChat();

        using var provider = services.BuildServiceProvider();
        var toolNames = provider.GetServices<IChatTool>().Select(t => t.Name).ToList();

        toolNames.Should().Contain("FindAnomalies");
    }

    private static JsonElement Root(string json) => JsonDocument.Parse(json).RootElement;

    private async Task SeedAsync(params Alert[] alerts)
    {
        await using var db = _factory.CreateDbContext();
        await db.Database.MigrateAsync();

        if (alerts.Length > 0)
        {
            db.Alerts.AddRange(alerts);
            await db.SaveChangesAsync();
        }
    }

    private static Alert Alert(
        string title,
        AnomalyType type,
        DateOnly period,
        AlertStatus status = AlertStatus.New,
        int detectedDaysAgo = 0) =>
        new()
        {
            Id = Guid.NewGuid(),
            DetectedAt = DateTime.UtcNow.AddDays(-detectedDaysAgo),
            Type = type,
            Signature = title,
            Title = title,
            Description = "opis",
            Status = status,
            RelatedAmount = 100m,
            PeriodFrom = period,
            PeriodTo = period,
        };
}
