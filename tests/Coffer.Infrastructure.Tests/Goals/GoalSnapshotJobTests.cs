using System.Security.Cryptography;
using Coffer.Core.Domain;
using Coffer.Core.Goals;
using Coffer.Infrastructure.Goals;
using Coffer.Infrastructure.Goals.Strategies;
using Coffer.Infrastructure.Tests.Persistence;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Coffer.Infrastructure.Tests.Goals;

/// <summary>
/// The daily snapshot job over a real SQLCipher database, with a fake report generator standing in
/// for the LLM. Covers idempotency within a day (a second run is a no-op), one snapshot per active
/// goal, exclusion of archived goals, and that a single advisor report is persisted for the day.
/// </summary>
public class GoalSnapshotJobTests : IDisposable
{
    private static readonly DateOnly _today = new(2026, 6, 10);

    private readonly string _tempDir;
    private readonly string _dbPath;
    private readonly SqliteTestDbContextFactory _factory;

    public GoalSnapshotJobTests()
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
    public async Task Run_WritesOneSnapshotPerActiveGoal_AndOneReport()
    {
        await MigrateAsync();
        await SeedAsync(Goal("Wakacje"));
        await SeedAsync(Goal("Auto"));
        await SeedAsync(Goal("Stare", archived: true));

        var written = await Job().RunAsync(_today, CancellationToken.None);

        written.Should().Be(2, "archived goals are skipped");

        await using var db = _factory.CreateDbContext();
        (await db.GoalSnapshots.CountAsync(s => s.Date == _today)).Should().Be(2);
        (await db.AdvisorReports.CountAsync(r => r.Date == _today)).Should().Be(1);
    }

    [Fact]
    public async Task Run_IsIdempotentWithinADay()
    {
        await MigrateAsync();
        await SeedAsync(Goal("Wakacje"));
        await SeedAsync(Goal("Auto"));

        await Job().RunAsync(_today, CancellationToken.None);
        var second = await Job().RunAsync(_today, CancellationToken.None);

        second.Should().Be(0, "a day that already has snapshots is a no-op");

        await using var db = _factory.CreateDbContext();
        (await db.GoalSnapshots.CountAsync(s => s.Date == _today)).Should().Be(2, "the second run must not duplicate snapshots");
        (await db.AdvisorReports.CountAsync(r => r.Date == _today)).Should().Be(1);
    }

    [Fact]
    public async Task Run_NoActiveGoals_WritesNothing()
    {
        await MigrateAsync();

        var written = await Job().RunAsync(_today, CancellationToken.None);

        written.Should().Be(0);

        await using var db = _factory.CreateDbContext();
        (await db.GoalSnapshots.CountAsync()).Should().Be(0);
        (await db.AdvisorReports.CountAsync()).Should().Be(0);
    }

    private GoalSnapshotJob Job()
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
        return new GoalSnapshotJob(
            _factory,
            new GoalsQuery(_factory),
            new FinancialContextBuilder(_factory),
            engine,
            new FakeReportGenerator(),
            NullLogger<GoalSnapshotJob>.Instance);
    }

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

    private static Goal Goal(string name, bool archived = false) => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        Type = GoalType.Purchase,
        TargetAmount = 8000m,
        Currency = "PLN",
        TargetDate = new DateOnly(2027, 7, 1),
        Priority = Priority.Medium,
        IsArchived = archived,
        CreatedAt = DateTime.UtcNow,
    };

    private sealed class FakeReportGenerator : IAdvisorReportGenerator
    {
        public Task<AdvisorReport> GenerateAsync(
            IReadOnlyList<GoalFeasibilityResult> results,
            FinancialContext context,
            IReadOnlyList<CategorySpending> categorySpending,
            DateOnly date,
            CancellationToken ct) =>
            Task.FromResult(new AdvisorReport
            {
                Id = Guid.NewGuid(),
                Date = date,
                GeneratedAt = DateTime.UtcNow,
                GeneratedByAi = false,
                Entries = [],
            });
    }
}
