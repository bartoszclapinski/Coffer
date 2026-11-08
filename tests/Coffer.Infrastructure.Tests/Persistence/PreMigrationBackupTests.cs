using System.Security.Cryptography;
using Coffer.Infrastructure.Persistence;
using Coffer.Infrastructure.Security;
using Coffer.Infrastructure.Tests.Backup;
using Coffer.Infrastructure.Tests.Security;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Coffer.Infrastructure.Tests.Persistence;

public class PreMigrationBackupTests : IDisposable
{
    private readonly TestVaultPaths _vaultPaths = new();
    private readonly DekHolder _dekHolder = new();

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        _dekHolder.Clear();
        _vaultPaths.Dispose();
    }

    private PreMigrationBackup NewBackup() => new(_vaultPaths, _dekHolder, NullLogger<PreMigrationBackup>.Instance);

    [Fact]
    public async Task CreateSnapshot_WhenNoDatabaseFile_ReturnsNull()
    {
        var result = await NewBackup().CreateSnapshotAsync(CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task CreateSnapshot_WithDek_WritesAnEncryptedOnlineBackupWhileAConnectionIsOpen()
    {
        var dek = RandomNumberGenerator.GetBytes(32);
        await SqlCipherTestDatabase.CreateAsync(_vaultPaths.DatabaseFile, dek, rows: 2);
        _dekHolder.Set(dek);
        await using var live = SqlCipherTestDatabase.CreateContext(_vaultPaths.DatabaseFile, dek, pooling: true);
        await live.Database.OpenConnectionAsync();
        var liveCount = await live.SchemaInfo.CountAsync();

        var snapshotPath = await NewBackup().CreateSnapshotAsync(CancellationToken.None);

        snapshotPath.Should().Contain(Path.Combine("backups", "pre-migration"));
        SqlCipherTestDatabase.HasPlaintextHeader(snapshotPath!).Should().BeFalse();
        (await SqlCipherTestDatabase.CountRowsAsync(snapshotPath!, dek)).Should().Be(liveCount);
        File.Exists(snapshotPath + "-wal").Should().BeFalse();
    }

    [Fact]
    public async Task CreateSnapshot_WithoutDek_CopiesEncryptedDatabaseBytesIntoRetainedFolder()
    {
        var payload = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };
        await File.WriteAllBytesAsync(_vaultPaths.DatabaseFile, payload);

        var snapshotPath = await NewBackup().CreateSnapshotAsync(CancellationToken.None);

        snapshotPath.Should().NotBeNull();
        File.Exists(snapshotPath).Should().BeTrue();
        snapshotPath.Should().Contain(Path.Combine("backups", "pre-migration"));
        (await File.ReadAllBytesAsync(snapshotPath!)).Should().Equal(payload);
    }

    [Fact]
    public async Task CreateSnapshot_WithoutDek_AlsoCopiesWalAndShmSideFiles()
    {
        await File.WriteAllBytesAsync(_vaultPaths.DatabaseFile, new byte[] { 0x01 });
        await File.WriteAllBytesAsync(_vaultPaths.DatabaseFile + "-wal", new byte[] { 0x02 });
        await File.WriteAllBytesAsync(_vaultPaths.DatabaseFile + "-shm", new byte[] { 0x03 });

        var snapshotPath = await NewBackup().CreateSnapshotAsync(CancellationToken.None);

        File.Exists(snapshotPath + "-wal").Should().BeTrue();
        File.Exists(snapshotPath + "-shm").Should().BeTrue();
    }
}
