using Coffer.Infrastructure.Persistence;
using Coffer.Infrastructure.Tests.Security;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Coffer.Infrastructure.Tests.Persistence;

public class PreMigrationBackupTests : IDisposable
{
    private readonly TestVaultPaths _vaultPaths = new();

    public void Dispose() => _vaultPaths.Dispose();

    [Fact]
    public async Task CreateSnapshot_WhenNoDatabaseFile_ReturnsNull()
    {
        var backup = new PreMigrationBackup(_vaultPaths, NullLogger<PreMigrationBackup>.Instance);

        var result = await backup.CreateSnapshotAsync(CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task CreateSnapshot_CopiesEncryptedDatabaseBytesIntoRetainedFolder()
    {
        var payload = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };
        await File.WriteAllBytesAsync(_vaultPaths.DatabaseFile, payload);

        var backup = new PreMigrationBackup(_vaultPaths, NullLogger<PreMigrationBackup>.Instance);

        var snapshotPath = await backup.CreateSnapshotAsync(CancellationToken.None);

        snapshotPath.Should().NotBeNull();
        File.Exists(snapshotPath).Should().BeTrue();
        snapshotPath.Should().Contain(Path.Combine("backups", "pre-migration"));
        (await File.ReadAllBytesAsync(snapshotPath!)).Should().Equal(payload);
    }

    [Fact]
    public async Task CreateSnapshot_AlsoCopiesWalAndShmSideFiles()
    {
        await File.WriteAllBytesAsync(_vaultPaths.DatabaseFile, new byte[] { 0x01 });
        await File.WriteAllBytesAsync(_vaultPaths.DatabaseFile + "-wal", new byte[] { 0x02 });
        await File.WriteAllBytesAsync(_vaultPaths.DatabaseFile + "-shm", new byte[] { 0x03 });

        var backup = new PreMigrationBackup(_vaultPaths, NullLogger<PreMigrationBackup>.Instance);

        var snapshotPath = await backup.CreateSnapshotAsync(CancellationToken.None);

        File.Exists(snapshotPath + "-wal").Should().BeTrue();
        File.Exists(snapshotPath + "-shm").Should().BeTrue();
    }
}
