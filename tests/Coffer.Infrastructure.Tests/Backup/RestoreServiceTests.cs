using System.Security.Cryptography;
using System.Text.Json;
using Coffer.Core.Backup;
using Coffer.Infrastructure.Backup;
using Coffer.Infrastructure.Security;
using Coffer.Infrastructure.Tests.Security;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;

namespace Coffer.Infrastructure.Tests.Backup;

public class RestoreServiceTests : IDisposable
{
    private readonly TestVaultPaths _vaultPaths = new();

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        _vaultPaths.Dispose();
    }

    private RestoreService NewService() => new(_vaultPaths, NullLogger<RestoreService>.Instance);

    private string BackupsDir => Path.Combine(_vaultPaths.LocalAppDataFolder, "backups");

    private string PreRestoreDir => Path.Combine(BackupsDir, "pre-restore");

    private string MarkerPath => Path.Combine(_vaultPaths.LocalAppDataFolder, "restore-pending.json");

    private static string DailyName(DateOnly date) => $"coffer-{date:yyyy-MM-dd}.db";

    private string SnapshotPath(DateOnly date) => Path.Combine(BackupsDir, DailyName(date));

    private async Task SeedSnapshotAsync(DateOnly date, byte[] db, byte[]? wal = null)
    {
        Directory.CreateDirectory(BackupsDir);
        await File.WriteAllBytesAsync(SnapshotPath(date), db);
        if (wal is not null)
        {
            await File.WriteAllBytesAsync(SnapshotPath(date) + "-wal", wal);
        }
    }

    private Task SeedDatabaseAsync(params byte[] bytes) =>
        File.WriteAllBytesAsync(_vaultPaths.DatabaseFile, bytes);

    [Fact]
    public async Task ListSnapshots_ReturnsDatedSnapshotsNewestFirstWithSizes()
    {
        await SeedSnapshotAsync(new DateOnly(2026, 7, 1), [1, 2, 3]);
        await SeedSnapshotAsync(new DateOnly(2026, 7, 3), [9]);
        // Junk names and the pre-migration subfolder must be ignored.
        await File.WriteAllBytesAsync(Path.Combine(BackupsDir, "notes.db"), [0]);
        Directory.CreateDirectory(Path.Combine(BackupsDir, "pre-migration"));
        await File.WriteAllBytesAsync(
            Path.Combine(BackupsDir, "pre-migration", "coffer-20260701T120000Z.db"), [0]);

        var snapshots = await NewService().ListSnapshotsAsync(CancellationToken.None);

        snapshots.Should().HaveCount(2);
        snapshots[0].Date.Should().Be(new DateOnly(2026, 7, 3));
        snapshots[0].SizeBytes.Should().Be(1);
        snapshots[1].Date.Should().Be(new DateOnly(2026, 7, 1));
        snapshots[1].FileName.Should().Be("coffer-2026-07-01.db");
        snapshots[1].SizeBytes.Should().Be(3);
    }

    [Fact]
    public async Task ListSnapshots_WithNoBackupsFolder_IsEmpty()
    {
        var snapshots = await NewService().ListSnapshotsAsync(CancellationToken.None);

        snapshots.Should().BeEmpty();
    }

    [Fact]
    public async Task StageRestore_ForAMissingSnapshot_Throws()
    {
        var act = () => NewService().StageRestoreAsync("coffer-2026-07-01.db", CancellationToken.None);

        await act.Should().ThrowAsync<FileNotFoundException>();
        File.Exists(MarkerPath).Should().BeFalse("nothing is staged when the snapshot does not exist");
    }

    [Fact]
    public async Task StageRestore_WritesAMarker_ThatGetPendingReadsBack()
    {
        var date = new DateOnly(2026, 7, 1);
        await SeedSnapshotAsync(date, [1]);

        var staged = await NewService().StageRestoreAsync(DailyName(date), CancellationToken.None);

        staged.SnapshotDate.Should().Be(date);
        staged.SnapshotFileName.Should().Be("coffer-2026-07-01.db");
        File.Exists(MarkerPath).Should().BeTrue();

        var pending = await NewService().GetPendingRestoreAsync(CancellationToken.None);
        pending.Should().NotBeNull();
        pending!.SnapshotDate.Should().Be(date);
        pending.SnapshotFileName.Should().Be("coffer-2026-07-01.db");
        pending.RequestedAtUtc.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task GetPendingRestore_WithNoMarker_IsNull()
    {
        (await NewService().GetPendingRestoreAsync(CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task GetPendingRestore_WithMalformedMarker_IsNull()
    {
        await File.WriteAllTextAsync(MarkerPath, "{ this is not valid json");

        (await NewService().GetPendingRestoreAsync(CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task CancelPendingRestore_RemovesTheMarker()
    {
        var date = new DateOnly(2026, 7, 1);
        await SeedSnapshotAsync(date, [1]);
        await NewService().StageRestoreAsync(DailyName(date), CancellationToken.None);

        await NewService().CancelPendingRestoreAsync(CancellationToken.None);

        File.Exists(MarkerPath).Should().BeFalse();
        (await NewService().GetPendingRestoreAsync(CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task ApplyPendingRestore_WithNoMarker_IsANoOp()
    {
        var result = await NewService().ApplyPendingRestoreAsync(CancellationToken.None);

        result.Applied.Should().BeFalse();
        result.RestoredFrom.Should().BeNull();
    }

    [Fact]
    public async Task ApplyPendingRestore_SwapsSnapshotIntoPlaceAndKeepsASafetyCopy()
    {
        // Snapshot holds the "old" state; the live DB has since been mutated to "new".
        var date = new DateOnly(2026, 7, 1);
        var oldBytes = new byte[] { 1, 1, 1 };
        var newBytes = new byte[] { 2, 2, 2, 2 };
        await SeedSnapshotAsync(date, oldBytes);
        await SeedDatabaseAsync(newBytes);
        await NewService().StageRestoreAsync(DailyName(date), CancellationToken.None);

        var result = await NewService().ApplyPendingRestoreAsync(CancellationToken.None);

        result.Applied.Should().BeTrue();
        result.RestoredFrom.Should().Be(date);
        (await File.ReadAllBytesAsync(_vaultPaths.DatabaseFile)).Should().Equal(oldBytes,
            "the live database is now the snapshot's content (the mutation is undone)");

        File.Exists(MarkerPath).Should().BeFalse("the marker is cleared once the restore is applied");

        result.SafetyCopyPath.Should().NotBeNull();
        File.Exists(result.SafetyCopyPath!).Should().BeTrue();
        Path.GetDirectoryName(result.SafetyCopyPath!).Should().Be(PreRestoreDir);
        (await File.ReadAllBytesAsync(result.SafetyCopyPath!)).Should().Equal(newBytes,
            "the safety copy preserves the pre-restore (mutated) state so the restore is reversible");
    }

    [Fact]
    public async Task ApplyPendingRestore_RemovesAStaleLiveWalTheSnapshotLacks()
    {
        var date = new DateOnly(2026, 7, 1);
        await SeedSnapshotAsync(date, [1]); // snapshot has no -wal
        await SeedDatabaseAsync(2);
        await File.WriteAllBytesAsync(_vaultPaths.DatabaseFile + "-wal", [0xAB]); // stale live -wal
        await NewService().StageRestoreAsync(DailyName(date), CancellationToken.None);

        await NewService().ApplyPendingRestoreAsync(CancellationToken.None);

        File.Exists(_vaultPaths.DatabaseFile + "-wal").Should()
            .BeFalse("a stale -wal from the replaced database would corrupt the restored one");
    }

    [Fact]
    public async Task ApplyPendingRestore_RestoresTheSnapshotsWalWhenItHasOne()
    {
        var date = new DateOnly(2026, 7, 1);
        await SeedSnapshotAsync(date, [1], wal: [0xEE]);
        await SeedDatabaseAsync(2);
        await NewService().StageRestoreAsync(DailyName(date), CancellationToken.None);

        await NewService().ApplyPendingRestoreAsync(CancellationToken.None);

        File.Exists(_vaultPaths.DatabaseFile + "-wal").Should().BeTrue();
        (await File.ReadAllBytesAsync(_vaultPaths.DatabaseFile + "-wal")).Should().Equal(0xEE);
    }

    [Fact]
    public async Task ApplyPendingRestore_IsIdempotent_SecondRunIsANoOp()
    {
        var date = new DateOnly(2026, 7, 1);
        await SeedSnapshotAsync(date, [1, 1]);
        await SeedDatabaseAsync(2, 2);
        await NewService().StageRestoreAsync(DailyName(date), CancellationToken.None);

        (await NewService().ApplyPendingRestoreAsync(CancellationToken.None)).Applied.Should().BeTrue();
        var second = await NewService().ApplyPendingRestoreAsync(CancellationToken.None);

        second.Applied.Should().BeFalse("the marker was cleared, so there is nothing left to apply");
    }

    [Fact]
    public async Task ApplyPendingRestore_WhenStagedSnapshotVanished_ThrowsAndKeepsTheMarker()
    {
        var date = new DateOnly(2026, 7, 1);
        await SeedSnapshotAsync(date, [1]);
        await NewService().StageRestoreAsync(DailyName(date), CancellationToken.None);
        File.Delete(SnapshotPath(date)); // snapshot removed after staging

        var act = () => NewService().ApplyPendingRestoreAsync(CancellationToken.None);

        await act.Should().ThrowAsync<FileNotFoundException>();
        File.Exists(MarkerPath).Should().BeTrue("the marker stays so the owner can see the failure and cancel");
    }

    [Fact]
    public async Task ListSnapshots_IncludesManualSnapshots_NewestFirst()
    {
        await SeedSnapshotAsync(new DateOnly(2026, 7, 1), [1]);
        var manual = Path.Combine(BackupsDir, "coffer-2026-07-03-manual-101500.db");
        await File.WriteAllBytesAsync(manual, [1, 2]);

        var snapshots = await NewService().ListSnapshotsAsync(CancellationToken.None);

        snapshots.Should().HaveCount(2);
        snapshots[0].FileName.Should().Be("coffer-2026-07-03-manual-101500.db");
        snapshots[0].Kind.Should().Be(SnapshotKind.Manual);
        snapshots[0].Time.Should().Be(new TimeOnly(10, 15, 0));
        snapshots[1].Kind.Should().Be(SnapshotKind.Daily);
    }

    [Fact]
    public async Task StageRestore_OfAManualSnapshot_RecordsItsFileNameAndDate()
    {
        Directory.CreateDirectory(BackupsDir);
        await File.WriteAllBytesAsync(Path.Combine(BackupsDir, "coffer-2026-07-03-manual-101500.db"), [1]);

        var staged = await NewService().StageRestoreAsync("coffer-2026-07-03-manual-101500.db", CancellationToken.None);

        staged.SnapshotFileName.Should().Be("coffer-2026-07-03-manual-101500.db");
        staged.SnapshotDate.Should().Be(new DateOnly(2026, 7, 3));
    }

    [Theory]
    [InlineData("notes.db")]
    [InlineData("../coffer-2026-07-01.db")]
    [InlineData("pre-migration/coffer-20260701T120000Z.db")]
    public async Task StageRestore_WithANonSnapshotName_Throws(string name)
    {
        var act = () => NewService().StageRestoreAsync(name, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>();
        File.Exists(MarkerPath).Should().BeFalse();
    }

    [Fact]
    public async Task ApplyPendingRestore_WithATamperedMarkerPath_ThrowsAndKeepsTheMarker()
    {
        await SeedDatabaseAsync(2);
        var tampered = new PendingRestore(new DateOnly(2026, 7, 1), "..\\coffer.db", DateTime.UtcNow);
        await File.WriteAllTextAsync(MarkerPath, JsonSerializer.Serialize(tampered));

        var act = () => NewService().ApplyPendingRestoreAsync(CancellationToken.None);

        await act.Should().ThrowAsync<InvalidDataException>();
        File.Exists(MarkerPath).Should().BeTrue();
        (await File.ReadAllBytesAsync(_vaultPaths.DatabaseFile)).Should().Equal(2);
    }

    [Fact]
    public async Task ApplyPendingRestore_OfAnOnlineBackupSnapshot_RestoresTheOlderStateUnderTheSameDek()
    {
        // End to end: a real SQLCipher vault, an online-backup snapshot (single file, no side-files), later
        // writes, then a restore that must bring back exactly the snapshot's rows and open with the same DEK.
        var dek = RandomNumberGenerator.GetBytes(32);
        await SqlCipherTestDatabase.CreateAsync(_vaultPaths.DatabaseFile, dek, rows: 3);
        var dekHolder = new DekHolder();
        dekHolder.Set(dek);
        var today = new DateOnly(2026, 7, 3);
        await new BackupService(_vaultPaths, dekHolder, NullLogger<BackupService>.Instance)
            .CreateDailySnapshotAsync(today, CancellationToken.None);
        var snapshotCount = await SqlCipherTestDatabase.CountRowsAsync(_vaultPaths.DatabaseFile, dek);

        await using (var db = SqlCipherTestDatabase.CreateContext(_vaultPaths.DatabaseFile, dek, pooling: false))
        {
            await SqlCipherTestDatabase.AddRowsAsync(db, 4, "after-snapshot");
        }

        await NewService().StageRestoreAsync(DailyName(today), CancellationToken.None);
        var result = await NewService().ApplyPendingRestoreAsync(CancellationToken.None);

        result.Applied.Should().BeTrue();
        File.Exists(_vaultPaths.DatabaseFile + "-wal").Should().BeFalse();
        (await SqlCipherTestDatabase.CountRowsAsync(_vaultPaths.DatabaseFile, dek)).Should().Be(snapshotCount);
        (await SqlCipherTestDatabase.CountRowsAsync(result.SafetyCopyPath!, dek)).Should().Be(snapshotCount + 4,
            "the safety copy holds the pre-restore state");
        dekHolder.Clear();
    }
}
