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
    private const string _expectedInitialMigration = "20260516142523_InitialCreate";

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
        await using var db = CreateContext();

        IReadOnlyList<string> pendingDuringBackup = Array.Empty<string>();
        IReadOnlyList<string> appliedDuringBackup = Array.Empty<string>();

        var runner = new MigrationRunner(
            db,
            NullLogger<MigrationRunner>.Instance,
            async ct =>
            {
                pendingDuringBackup = (await db.Database.GetPendingMigrationsAsync(ct)).ToList();
                appliedDuringBackup = (await db.Database.GetAppliedMigrationsAsync(ct)).ToList();
            });

        var result = await runner.RunPendingMigrationsAsync(CancellationToken.None);

        pendingDuringBackup.Should().NotBeEmpty(
            "the backup callback must run while migrations are still pending");
        appliedDuringBackup.Should().BeEmpty(
            "the backup callback must run before any migration is applied");
        result.Status.Should().Be(MigrationStatus.Migrated);
    }

    [Fact]
    public async Task Run_WhenNoPendingMigrations_DoesNotInvokeBackupCallback()
    {
        await using (var db = CreateContext())
        {
            var firstRunner = new MigrationRunner(db, NullLogger<MigrationRunner>.Instance);
            await firstRunner.RunPendingMigrationsAsync(CancellationToken.None);
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
    public async Task Run_AfterSuccessfulMigration_AppendsSchemaInfoEntryWithExactVersion()
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
            entries[0].Version.Should().Be(_expectedInitialMigration);
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
        result.AppliedMigrations.Should().ContainSingle().Which.Should().Be(_expectedInitialMigration);
    }

    [Fact]
    public async Task Run_RecordsAppVersionFromInjectedProvider()
    {
        await using var db = CreateContext();
        var runner = new MigrationRunner(
            db,
            NullLogger<MigrationRunner>.Instance,
            preMigrationBackup: null,
            appVersionProvider: () => "1.2.3-test");

        await runner.RunPendingMigrationsAsync(CancellationToken.None);

        var entry = await db.SchemaInfo.SingleAsync();
        entry.AppVersion.Should().Be("1.2.3-test");
    }

    [Fact]
    public async Task Run_WhenBackupCallbackThrows_DoesNotApplyAnyMigration()
    {
        await using var db = CreateContext();
        var runner = new MigrationRunner(
            db,
            NullLogger<MigrationRunner>.Instance,
            _ => throw new InvalidOperationException("backup failed"));

        var act = async () => await runner.RunPendingMigrationsAsync(CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("backup failed");

        var pending = await db.Database.GetPendingMigrationsAsync();
        pending.Should().NotBeEmpty(
            "no migration should be applied when the pre-migration backup callback throws");
        var applied = await db.Database.GetAppliedMigrationsAsync();
        applied.Should().BeEmpty();
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
