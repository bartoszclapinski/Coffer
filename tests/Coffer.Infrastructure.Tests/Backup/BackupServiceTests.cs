using System.Security.Cryptography;
using Coffer.Core.Backup;
using Coffer.Infrastructure.Backup;
using Coffer.Infrastructure.Security;
using Coffer.Infrastructure.Tests.Security;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Coffer.Infrastructure.Tests.Backup;

public class BackupServiceTests : IDisposable
{
    private static readonly DateOnly _today = new(2026, 7, 3);

    private readonly TestVaultPaths _vaultPaths = new();
    private readonly DekHolder _dekHolder = new();
    private readonly FixedTimeProvider _clock = new(new DateTimeOffset(2026, 7, 3, 14, 5, 9, TimeSpan.Zero));

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        _dekHolder.Clear();
        _vaultPaths.Dispose();
    }

    private BackupService NewService() =>
        new(_vaultPaths, _dekHolder, NullLogger<BackupService>.Instance, _clock);

    private string BackupsDir => Path.Combine(_vaultPaths.LocalAppDataFolder, "backups");

    private string DailyPath(DateOnly date) => Path.Combine(BackupsDir, $"coffer-{date:yyyy-MM-dd}.db");

    private async Task SeedDatabaseAsync(params byte[] bytes) =>
        await File.WriteAllBytesAsync(_vaultPaths.DatabaseFile, bytes);

    private async Task<byte[]> SeedEncryptedDatabaseAsync(int rows)
    {
        var dek = RandomNumberGenerator.GetBytes(32);
        await SqlCipherTestDatabase.CreateAsync(_vaultPaths.DatabaseFile, dek, rows);
        _dekHolder.Set(dek);
        return dek;
    }

    // ---- Online backup (DEK held) -------------------------------------------------------------------

    [Fact]
    public async Task CreateDailySnapshot_WhilePooledConnectionIsOpen_Succeeds()
    {
        var dek = await SeedEncryptedDatabaseAsync(rows: 3);

        // A pooled app connection is open and holds the file (as it does once any screen has loaded).
        await using var live = SqlCipherTestDatabase.CreateContext(_vaultPaths.DatabaseFile, dek, pooling: true);
        await live.Database.OpenConnectionAsync();
        (await live.SchemaInfo.CountAsync()).Should().BeGreaterThan(0);

        var result = await NewService().CreateDailySnapshotAsync(_today, CancellationToken.None);

        result.Created.Should().BeTrue();
        File.Exists(DailyPath(_today)).Should().BeTrue();
    }

    [Fact]
    public async Task CreateDailySnapshot_OpensWithTheSameDek_IncludesWalRows_AndIsNotPlaintext()
    {
        var dek = await SeedEncryptedDatabaseAsync(rows: 3);

        // Rows committed through a still-open pooled connection sit in the -wal, not yet in the main file.
        await using var live = SqlCipherTestDatabase.CreateContext(_vaultPaths.DatabaseFile, dek, pooling: true);
        await live.Database.OpenConnectionAsync();
        await SqlCipherTestDatabase.AddRowsAsync(live, 5, "wal");
        var liveCount = await live.SchemaInfo.CountAsync();

        await NewService().CreateDailySnapshotAsync(_today, CancellationToken.None);

        var snapshot = DailyPath(_today);
        (await SqlCipherTestDatabase.CountRowsAsync(snapshot, dek)).Should().Be(liveCount,
            "the online backup copies a consistent image including committed WAL frames");
        SqlCipherTestDatabase.HasPlaintextHeader(snapshot).Should().BeFalse("the snapshot stays SQLCipher-encrypted");
        File.Exists(snapshot + "-wal").Should().BeFalse("the snapshot is a single self-contained file");
        File.Exists(snapshot + "-shm").Should().BeFalse();
        File.Exists(snapshot + ".tmp").Should().BeFalse("the temp file is moved away on success");
    }

    [Fact]
    public async Task CreateDailySnapshot_DoesNotLeaveAHandleOnTheSnapshot()
    {
        await SeedEncryptedDatabaseAsync(rows: 1);

        await NewService().CreateDailySnapshotAsync(_today, CancellationToken.None);

        // Deleting right away proves no (pooled) backup connection still holds the file.
        var act = () => File.Delete(DailyPath(_today));
        act.Should().NotThrow();
    }

    [Fact]
    public async Task CreateDailySnapshot_DropsAStaleSideFileAtTheTarget()
    {
        await SeedEncryptedDatabaseAsync(rows: 1);
        Directory.CreateDirectory(BackupsDir);
        await File.WriteAllBytesAsync(DailyPath(_today) + "-wal", [0xAB]); // left by an old byte-copy snapshot

        await NewService().CreateDailySnapshotAsync(_today, CancellationToken.None);

        File.Exists(DailyPath(_today) + "-wal").Should().BeFalse("a stale -wal would be replayed over the snapshot");
    }

    [Fact]
    public async Task CreateSnapshotNow_WritesADistinctManualSnapshot_AndKeepsTheDaily()
    {
        var dek = await SeedEncryptedDatabaseAsync(rows: 2);
        await NewService().CreateDailySnapshotAsync(_today, CancellationToken.None);
        var dailyBytes = await File.ReadAllBytesAsync(DailyPath(_today));

        var result = await NewService().CreateSnapshotNowAsync(_today, CancellationToken.None);

        result.Created.Should().BeTrue();
        var manual = Path.Combine(BackupsDir, "coffer-2026-07-03-manual-140509.db");
        result.Path.Should().Be(manual);
        File.Exists(manual).Should().BeTrue();
        (await File.ReadAllBytesAsync(DailyPath(_today))).Should().Equal(dailyBytes,
            "a manual snapshot never overwrites today's daily one");
        (await SqlCipherTestDatabase.CountRowsAsync(manual, dek)).Should()
            .Be(await SqlCipherTestDatabase.CountRowsAsync(_vaultPaths.DatabaseFile, dek));

        var listed = await new RestoreService(_vaultPaths, NullLogger<RestoreService>.Instance)
            .ListSnapshotsAsync(CancellationToken.None);
        listed.Select(s => s.FileName).Should().BeEquivalentTo(
            ["coffer-2026-07-03.db", "coffer-2026-07-03-manual-140509.db"]);
        listed.Single(s => s.Kind == SnapshotKind.Manual).Time.Should().Be(new TimeOnly(14, 5, 9));
    }

    [Fact]
    public async Task CreateSnapshot_WhenItFails_RecordsTheFailure_AndASuccessClearsIt()
    {
        await SeedDatabaseAsync(1, 2, 3); // not a database — the keyed online backup must fail
        _dekHolder.Set(RandomNumberGenerator.GetBytes(32));
        var service = NewService();

        var act = () => service.CreateSnapshotNowAsync(_today, CancellationToken.None);

        await act.Should().ThrowAsync<Exception>();
        var failed = await service.GetStatusAsync(CancellationToken.None);
        failed.LastFailureAtUtc.Should().Be(_clock.GetUtcNow().UtcDateTime);
        failed.DailyCount.Should().Be(0);
        Directory.EnumerateFiles(BackupsDir, "*.tmp").Should().BeEmpty("a failed attempt leaves no temp file");

        SqliteConnection.ClearAllPools();
        File.Delete(_vaultPaths.DatabaseFile);
        _dekHolder.Clear();
        await SeedEncryptedDatabaseAsync(rows: 1);
        await service.CreateSnapshotNowAsync(_today, CancellationToken.None);

        (await service.GetStatusAsync(CancellationToken.None)).LastFailureAtUtc.Should().BeNull();
    }

    // ---- Byte-copy fallback (no DEK held) -----------------------------------------------------------

    [Fact]
    public async Task CreateDailySnapshot_WithoutDek_CopiesTheEncryptedBytes()
    {
        var payload = new byte[] { 1, 2, 3, 4 };
        await SeedDatabaseAsync(payload);

        var result = await NewService().CreateDailySnapshotAsync(_today, CancellationToken.None);

        result.Created.Should().BeTrue();
        var expected = DailyPath(_today);
        result.Path.Should().Be(expected);
        (await File.ReadAllBytesAsync(expected)).Should().Equal(payload);
        File.Exists(expected + ".tmp").Should().BeFalse("the temp file is renamed away on success");
    }

    [Fact]
    public async Task CreateDailySnapshot_WithoutDek_ToleratesAnOpenWriterHandle()
    {
        await SeedDatabaseAsync(5, 6, 7);
        // Mimic SQLite's own handle: open for read+write, sharing read+write.
        await using var writer = new FileStream(
            _vaultPaths.DatabaseFile, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);

        var result = await NewService().CreateDailySnapshotAsync(_today, CancellationToken.None);

        result.Created.Should().BeTrue();
    }

    [Fact]
    public async Task CreateDailySnapshot_IsIdempotentWithinTheDay()
    {
        await SeedDatabaseAsync(9);

        (await NewService().CreateDailySnapshotAsync(_today, CancellationToken.None)).Created.Should().BeTrue();
        (await NewService().CreateDailySnapshotAsync(_today, CancellationToken.None)).Created
            .Should().BeFalse("today's snapshot already exists");

        Directory.EnumerateFiles(BackupsDir, "coffer-*.db").Should().ContainSingle();
    }

    [Fact]
    public async Task CreateDailySnapshot_WithNoDatabase_DoesNothing()
    {
        var result = await NewService().CreateDailySnapshotAsync(_today, CancellationToken.None);

        result.Created.Should().BeFalse();
        result.Path.Should().BeNull();
    }

    // ---- Retention ----------------------------------------------------------------------------------

    [Fact]
    public async Task CreateDailySnapshot_PrunesOldSnapshotsBeyondTheNewestSeven()
    {
        await SeedDatabaseAsync(1);
        Directory.CreateDirectory(BackupsDir);
        // Seven recent snapshots fill the floor, so the old one is subject to the 30-day rule.
        for (var day = 1; day <= 7; day++)
        {
            await File.WriteAllBytesAsync(DailyPath(new DateOnly(2026, 6, 25).AddDays(day)), [1]);
        }

        var old = DailyPath(new DateOnly(2026, 5, 1)); // > 30 days before 2026-07-03
        await File.WriteAllBytesAsync(old, [0xAB]);
        await File.WriteAllBytesAsync(old + "-wal", [0xCD]);

        await NewService().CreateDailySnapshotAsync(_today, CancellationToken.None);

        File.Exists(old).Should().BeFalse("the aged-out daily snapshot is pruned");
        File.Exists(old + "-wal").Should().BeFalse("its side-file goes with it");
        File.Exists(DailyPath(_today)).Should().BeTrue();
    }

    [Fact]
    public async Task CreateDailySnapshot_NeverPrunesTheSnapshotStagedForRestore()
    {
        await SeedDatabaseAsync(1);
        Directory.CreateDirectory(BackupsDir);
        for (var day = 1; day <= 7; day++)
        {
            await File.WriteAllBytesAsync(DailyPath(new DateOnly(2026, 6, 25).AddDays(day)), [1]);
        }

        var staged = DailyPath(new DateOnly(2026, 5, 1)); // aged out and outside the newest seven
        await File.WriteAllBytesAsync(staged, [0xAB]);
        await new RestoreService(_vaultPaths, NullLogger<RestoreService>.Instance)
            .StageRestoreAsync(Path.GetFileName(staged), CancellationToken.None);

        await NewService().CreateDailySnapshotAsync(_today, CancellationToken.None);

        File.Exists(staged).Should().BeTrue("the restore staged for the next start must still find its snapshot");
    }

    [Fact]
    public async Task CreateDailySnapshot_WhenEverySnapshotIsOld_KeepsTheNewestSeven()
    {
        // The app was not opened for months and its database is gone: only old snapshots exist.
        Directory.CreateDirectory(BackupsDir);
        var names = Enumerable.Range(1, 9).Select(day => DailyPath(new DateOnly(2026, 3, day))).ToList();
        foreach (var name in names)
        {
            await File.WriteAllBytesAsync(name, [1]);
        }

        await SeedDatabaseAsync(2);
        await NewService().CreateDailySnapshotAsync(_today, CancellationToken.None);

        // Today's new snapshot + the 6 newest old ones = 7 kept.
        Directory.EnumerateFiles(BackupsDir, "coffer-*.db").Should().HaveCount(BackupService.KeepNewestSnapshots);
        File.Exists(DailyPath(new DateOnly(2026, 3, 4))).Should().BeTrue();
        File.Exists(DailyPath(new DateOnly(2026, 3, 3))).Should().BeFalse();
    }

    [Fact]
    public async Task Prune_RemovesPreMigrationFilesOlderThan90Days()
    {
        await SeedDatabaseAsync(1);
        var preDir = Path.Combine(BackupsDir, "pre-migration");
        Directory.CreateDirectory(preDir);
        var oldPre = Path.Combine(preDir, "coffer-20260325T120000Z.db");  // ~100 days back — expired
        var recentPre = Path.Combine(preDir, "coffer-20260701T120000Z.db"); // 2 days back — kept
        await File.WriteAllBytesAsync(oldPre, [1]);
        await File.WriteAllBytesAsync(recentPre, [2]);

        await NewService().CreateDailySnapshotAsync(_today, CancellationToken.None);

        File.Exists(oldPre).Should().BeFalse();
        File.Exists(recentPre).Should().BeTrue();
    }

    // ---- Status -------------------------------------------------------------------------------------

    [Fact]
    public async Task GetStatus_ReportsLastDailyDate_CountOfAllSnapshots_AndNewestWriteTime()
    {
        Directory.CreateDirectory(BackupsDir);
        await File.WriteAllBytesAsync(DailyPath(new DateOnly(2026, 7, 1)), [1]);
        await File.WriteAllBytesAsync(DailyPath(new DateOnly(2026, 7, 3)), [1]);
        var manual = Path.Combine(BackupsDir, "coffer-2026-07-03-manual-101500.db");
        await File.WriteAllBytesAsync(manual, [1]);
        var newest = new DateTime(2026, 7, 3, 10, 15, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(DailyPath(new DateOnly(2026, 7, 1)), newest.AddDays(-2));
        File.SetLastWriteTimeUtc(DailyPath(new DateOnly(2026, 7, 3)), newest.AddHours(-3));
        File.SetLastWriteTimeUtc(manual, newest);

        var status = await NewService().GetStatusAsync(CancellationToken.None);

        status.LastDailySnapshot.Should().Be(new DateOnly(2026, 7, 3));
        status.DailyCount.Should().Be(3);
        status.NewestSnapshotAtUtc.Should().Be(newest);
        status.LastFailureAtUtc.Should().BeNull();
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;

        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }
}
