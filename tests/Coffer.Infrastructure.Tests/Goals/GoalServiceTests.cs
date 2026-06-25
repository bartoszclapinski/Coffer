using System.Security.Cryptography;
using Coffer.Core.Domain;
using Coffer.Core.Goals;
using Coffer.Infrastructure.Goals;
using Coffer.Infrastructure.Tests.Persistence;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Coffer.Infrastructure.Tests.Goals;

public class GoalServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _dbPath;
    private readonly SqliteTestDbContextFactory _factory;

    public GoalServiceTests()
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
    }

    [Fact]
    public async Task Create_PersistsGoalWithUtcTimestamp()
    {
        await MigrateAsync();
        var service = new GoalService(_factory);

        var id = await service.CreateAsync(
            new NewGoal("Wakacje", GoalType.Purchase, 8000m, "PLN", new DateOnly(2027, 7, 1), Priority.High, "notatka"),
            CancellationToken.None);

        await using var db = _factory.CreateDbContext();
        var stored = await db.Goals.SingleAsync(g => g.Id == id);
        stored.Name.Should().Be("Wakacje");
        stored.Currency.Should().Be("PLN");
        stored.Notes.Should().Be("notatka");
        stored.IsArchived.Should().BeFalse();
        stored.CreatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task Update_CopiesScalarFields_AndLeavesContributionsUntouched()
    {
        await MigrateAsync();
        var service = new GoalService(_factory);
        var id = await service.CreateAsync(
            new NewGoal("Old", GoalType.Purchase, 5000m, "PLN", new DateOnly(2027, 1, 1), Priority.Low, null),
            CancellationToken.None);
        await service.AddContributionAsync(id, 300m, new DateOnly(2026, 6, 1), CancellationToken.None);

        await service.UpdateAsync(
            new Goal
            {
                Id = id,
                Name = "New",
                Type = GoalType.EmergencyFund,
                TargetAmount = 9000m,
                Currency = "PLN",
                TargetDate = new DateOnly(2028, 1, 1),
                Priority = Priority.High,
                Notes = "edited",
            },
            CancellationToken.None);

        await using var db = _factory.CreateDbContext();
        var stored = await db.Goals.Include(g => g.Contributions).SingleAsync(g => g.Id == id);
        stored.Name.Should().Be("New");
        stored.Type.Should().Be(GoalType.EmergencyFund);
        stored.TargetAmount.Should().Be(9000m);
        stored.Priority.Should().Be(Priority.High);
        stored.Contributions.Should().ContainSingle("editing must never disturb a goal's contributions");
        stored.Contributions[0].Amount.Should().Be(300m);
    }

    [Fact]
    public async Task Archive_SoftDeletesGoalButKeepsRow()
    {
        await MigrateAsync();
        var service = new GoalService(_factory);
        var id = await service.CreateAsync(
            new NewGoal("Wakacje", GoalType.Purchase, 8000m, "PLN", new DateOnly(2027, 7, 1), Priority.High, null),
            CancellationToken.None);

        await service.ArchiveAsync(id, CancellationToken.None);

        await using var db = _factory.CreateDbContext();
        var stored = await db.Goals.SingleAsync(g => g.Id == id);
        stored.IsArchived.Should().BeTrue();
    }

    [Fact]
    public async Task AddContribution_StoresManualContribution()
    {
        await MigrateAsync();
        var service = new GoalService(_factory);
        var id = await service.CreateAsync(
            new NewGoal("Wakacje", GoalType.Purchase, 8000m, "PLN", new DateOnly(2027, 7, 1), Priority.High, null),
            CancellationToken.None);

        await service.AddContributionAsync(id, 500m, new DateOnly(2026, 6, 1), CancellationToken.None);

        await using var db = _factory.CreateDbContext();
        var contribution = await db.GoalContributions.SingleAsync(c => c.GoalId == id);
        contribution.Amount.Should().Be(500m);
        contribution.Source.Should().Be(ContributionSource.Manual);
        contribution.TransactionId.Should().BeNull();
    }

    [Fact]
    public async Task AddContribution_UnknownGoal_IsNoOp()
    {
        await MigrateAsync();
        var service = new GoalService(_factory);

        await service.AddContributionAsync(Guid.NewGuid(), 500m, new DateOnly(2026, 6, 1), CancellationToken.None);

        await using var db = _factory.CreateDbContext();
        (await db.GoalContributions.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task RemoveContribution_DeletesTheRow()
    {
        await MigrateAsync();
        var service = new GoalService(_factory);
        var id = await service.CreateAsync(
            new NewGoal("Wakacje", GoalType.Purchase, 8000m, "PLN", new DateOnly(2027, 7, 1), Priority.High, null),
            CancellationToken.None);
        await service.AddContributionAsync(id, 500m, new DateOnly(2026, 6, 1), CancellationToken.None);

        Guid contributionId;
        await using (var db = _factory.CreateDbContext())
        {
            contributionId = (await db.GoalContributions.SingleAsync(c => c.GoalId == id)).Id;
        }

        await service.RemoveContributionAsync(contributionId, CancellationToken.None);

        await using var verify = _factory.CreateDbContext();
        (await verify.GoalContributions.CountAsync()).Should().Be(0);
    }

    private async Task MigrateAsync()
    {
        await using var db = _factory.CreateDbContext();
        await db.Database.MigrateAsync();
    }
}
