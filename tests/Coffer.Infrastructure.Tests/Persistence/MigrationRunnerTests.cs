using System.Security.Cryptography;
using Coffer.Infrastructure.Persistence;
using Coffer.Infrastructure.Persistence.Encryption;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Coffer.Infrastructure.Tests.Persistence;

public class MigrationRunnerTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _dbPath;
    private readonly byte[] _dek;

    public MigrationRunnerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "Coffer.Tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _dbPath = Path.Combine(_tempDir, "coffer.db");
        _dek = RandomNumberGenerator.GetBytes(32);
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
    public async Task Run_WithPendingMigrations_InvokesBackupCallbackBeforeMigrate()
    {
        var order = new List<string>();

        await using var db = CreateContext();
        var runner = new MigrationRunner(
            db,
            NullLogger<MigrationRunner>.Instance,
            _ =>
            {
                order.Add("backup");
                return Task.CompletedTask;
            });

        var result = await runner.RunPendingMigrationsAsync(CancellationToken.None);
        order.Add("after-run");

        order.Should().StartWith("backup");
        result.Status.Should().Be(MigrationStatus.Migrated);
    }

    [Fact]
    public async Task Run_WhenNoPendingMigrations_DoesNotInvokeBackupCallback()
    {
        // First run applies InitialCreate.
        await using (var db = CreateContext())
        {
            var runner = new MigrationRunner(db, NullLogger<MigrationRunner>.Instance);
            await runner.RunPendingMigrationsAsync(CancellationToken.None);
        }

        SqliteConnection.ClearAllPools();

        var callbackInvoked = false;

        await using (var db = CreateContext())
        {
            var runner = new MigrationRunner(
                db,
                NullLogger<MigrationRunner>.Instance,
                _ =>
                {
                    callbackInvoked = true;
                    return Task.CompletedTask;
                });

            var result = await runner.RunPendingMigrationsAsync(CancellationToken.None);
            result.Status.Should().Be(MigrationStatus.UpToDate);
        }

        callbackInvoked.Should().BeFalse();
    }

    [Fact]
    public async Task Run_AfterSuccessfulMigration_AppendsSchemaInfoEntry()
    {
        await using (var db = CreateContext())
        {
            var runner = new MigrationRunner(db, NullLogger<MigrationRunner>.Instance);
            await runner.RunPendingMigrationsAsync(CancellationToken.None);
        }

        SqliteConnection.ClearAllPools();

        await using (var db = CreateContext())
        {
            var entries = await db.SchemaInfo.ToListAsync();
            entries.Should().HaveCount(1);
            entries[0].Version.Should().NotBeNullOrWhiteSpace();
            entries[0].MigratedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));
        }
    }

    [Fact]
    public async Task Run_ReturnsMigratedResult_WithAppliedMigrationNames()
    {
        await using var db = CreateContext();
        var runner = new MigrationRunner(db, NullLogger<MigrationRunner>.Instance);

        var result = await runner.RunPendingMigrationsAsync(CancellationToken.None);

        result.Status.Should().Be(MigrationStatus.Migrated);
        result.AppliedMigrations.Should().NotBeEmpty();
    }

    private CofferDbContext CreateContext()
    {
        // Pooling=False — see CofferDbContextEncryptionTests for rationale.
        var options = new DbContextOptionsBuilder<CofferDbContext>()
            .UseSqlite($"Data Source={_dbPath};Pooling=False;")
            .AddInterceptors(new SqlCipherKeyInterceptor(_dek))
            .Options;
        return new CofferDbContext(options);
    }
}
