using Coffer.Infrastructure.Backup;
using Coffer.Infrastructure.Tests.Security;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Coffer.Infrastructure.Tests.Backup;

public class BackupServiceTests : IDisposable
{
    private static readonly DateOnly _today = new(2026, 7, 3);

    private readonly TestVaultPaths _vaultPaths = new();

    public void Dispose() => _vaultPaths.Dispose();

    private BackupService NewService() => new(_vaultPaths, NullLogger<BackupService>.Instance);

    private string BackupsDir => Path.Combine(_vaultPaths.LocalAppDataFolder, "backups");

    private string DailyPath(DateOnly date) => Path.Combine(BackupsDir, $"coffer-{date:yyyy-MM-dd}.db");

    private async Task SeedDatabaseAsync(params byte[] bytes) =>
        await File.WriteAllBytesAsync(_vaultPaths.DatabaseFile, bytes);

    [Fact]
    public async Task CreateDailySnapshot_WritesDatedCopyOfTheEncryptedFile()
    {
        var payload = new byte[] { 1, 2, 3, 4 };
        await SeedDatabaseAsync(payload);

        var result = await NewService().CreateDailySnapshotAsync(_today, CancellationToken.None);

        result.Created.Should().BeTrue();
        var expected = DailyPath(_today);
        result.Path.Should().Be(expected);
        File.Exists(expected).Should().BeTrue();
        (await File.ReadAllBytesAsync(expected)).Should().Equal(payload);
        File.Exists(expected + ".tmp").Should().BeFalse("the temp file is renamed away on success");
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

    [Fact]
    public async Task CreateDailySnapshot_PrunesDailyFilesOlderThan30Days()
    {
        await SeedDatabaseAsync(1);
        Directory.CreateDirectory(BackupsDir);
        var old = DailyPath(new DateOnly(2026, 5, 1)); // > 30 days before 2026-07-03
        await File.WriteAllBytesAsync(old, [0xAB]);
        await File.WriteAllBytesAsync(old + "-wal", [0xCD]);

        await NewService().CreateDailySnapshotAsync(_today, CancellationToken.None);

        File.Exists(old).Should().BeFalse("the aged-out daily snapshot is pruned");
        File.Exists(old + "-wal").Should().BeFalse("its side-file goes with it");
        File.Exists(DailyPath(_today)).Should().BeTrue();
    }

    [Fact]
    public async Task CreateSnapshotNow_RefreshesTodaysSnapshot()
    {
        await SeedDatabaseAsync(1, 1);
        await NewService().CreateSnapshotNowAsync(_today, CancellationToken.None);

        await SeedDatabaseAsync(2, 2, 2);
        var result = await NewService().CreateSnapshotNowAsync(_today, CancellationToken.None);

        result.Created.Should().BeTrue();
        (await File.ReadAllBytesAsync(DailyPath(_today))).Should().Equal(2, 2, 2);
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

    [Fact]
    public async Task GetStatus_ReportsLastDailyDateAndCount()
    {
        Directory.CreateDirectory(BackupsDir);
        await File.WriteAllBytesAsync(DailyPath(new DateOnly(2026, 7, 1)), [1]);
        await File.WriteAllBytesAsync(DailyPath(new DateOnly(2026, 7, 3)), [1]);

        var status = await NewService().GetStatusAsync(CancellationToken.None);

        status.LastDailySnapshot.Should().Be(new DateOnly(2026, 7, 3));
        status.DailyCount.Should().Be(2);
    }
}
