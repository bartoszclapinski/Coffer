using System.Security.Cryptography;
using Coffer.Core.Domain;
using Coffer.Core.Goals;
using Coffer.Infrastructure.Goals;
using Coffer.Infrastructure.Tests.Persistence;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Coffer.Infrastructure.Tests.Goals;

public class GoalsQueryTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _dbPath;
    private readonly SqliteTestDbContextFactory _factory;

    public GoalsQueryTests()
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
    public async Task GetActive_ExcludesArchivedGoals()
    {
        await MigrateAsync();
        await SeedAsync(Goal("Active", Priority.Medium, new DateOnly(2027, 1, 1)));
        await SeedAsync(Goal("Archived", Priority.Medium, new DateOnly(2027, 1, 1), archived: true));

        var active = await new GoalsQuery(_factory).GetActiveAsync(CancellationToken.None);

        active.Should().ContainSingle();
        active[0].Name.Should().Be("Active");
    }

    [Fact]
    public async Task GetActive_IncludesContributions()
    {
        await MigrateAsync();
        var goal = Goal("Wakacje", Priority.High, new DateOnly(2027, 7, 1));
        goal.Contributions.Add(new GoalContribution
        {
            Id = Guid.NewGuid(),
            GoalId = goal.Id,
            Amount = 500m,
            Date = new DateOnly(2026, 6, 1),
            Source = ContributionSource.Manual,
        });
        await SeedAsync(goal);

        var active = await new GoalsQuery(_factory).GetActiveAsync(CancellationToken.None);

        active.Should().ContainSingle();
        active[0].Contributions.Should().ContainSingle();
        active[0].Contributions[0].Amount.Should().Be(500m);
    }

    [Fact]
    public async Task GetActive_OrdersByPriorityThenTargetDate()
    {
        await MigrateAsync();
        await SeedAsync(Goal("LowEarly", Priority.Low, new DateOnly(2026, 1, 1)));
        await SeedAsync(Goal("HighLate", Priority.High, new DateOnly(2028, 1, 1)));
        await SeedAsync(Goal("HighEarly", Priority.High, new DateOnly(2027, 1, 1)));

        var active = await new GoalsQuery(_factory).GetActiveAsync(CancellationToken.None);

        active.Select(g => g.Name).Should().ContainInOrder("HighEarly", "HighLate", "LowEarly");
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

    private static Goal Goal(string name, Priority priority, DateOnly targetDate, bool archived = false) => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        Type = GoalType.Purchase,
        TargetAmount = 8000m,
        Currency = "PLN",
        TargetDate = targetDate,
        Priority = priority,
        IsArchived = archived,
        CreatedAt = DateTime.UtcNow,
    };
}
