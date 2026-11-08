using System.IO.Compression;
using System.Security.Cryptography;
using Coffer.Infrastructure.Backup;
using Coffer.Infrastructure.Security;
using Coffer.Infrastructure.Tests.Security;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Coffer.Infrastructure.Tests.Backup;

public class ArchiveExporterTests : IDisposable
{
    private readonly TestVaultPaths _vaultPaths = new();
    private readonly DekHolder _dekHolder = new();

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        _dekHolder.Clear();
        _vaultPaths.Dispose();
    }

    private ArchiveExporter NewExporter() => new(_vaultPaths, _dekHolder, NullLogger<ArchiveExporter>.Instance);

    private string Target => Path.Combine(_vaultPaths.LocalAppDataFolder, "archive.zip");

    [Fact]
    public async Task Export_WithDek_WhilePooledConnectionIsOpen_ZipsAConsistentEncryptedSnapshot()
    {
        var dek = RandomNumberGenerator.GetBytes(32);
        await SqlCipherTestDatabase.CreateAsync(_vaultPaths.DatabaseFile, dek, rows: 2);
        _dekHolder.Set(dek);
        await File.WriteAllBytesAsync(_vaultPaths.EncryptedDekFilePath, [7, 7]);

        await using var live = SqlCipherTestDatabase.CreateContext(_vaultPaths.DatabaseFile, dek, pooling: true);
        await live.Database.OpenConnectionAsync();
        await SqlCipherTestDatabase.AddRowsAsync(live, 4, "wal");
        var liveCount = await live.SchemaInfo.CountAsync();

        await NewExporter().ExportAsync(Target, CancellationToken.None);

        using var archive = ZipFile.OpenRead(Target);
        archive.Entries.Select(e => e.FullName).Should().BeEquivalentTo(
            ["coffer.db", "dek.encrypted", "manifest.json"],
            "the online snapshot is one self-contained file with no -wal/-shm");

        var extracted = Path.Combine(_vaultPaths.LocalAppDataFolder, "extracted.db");
        archive.GetEntry("coffer.db")!.ExtractToFile(extracted);
        SqlCipherTestDatabase.HasPlaintextHeader(extracted).Should().BeFalse();
        (await SqlCipherTestDatabase.CountRowsAsync(extracted, dek)).Should().Be(liveCount);
        File.Exists(Target + ".tmp").Should().BeFalse();
    }

    [Fact]
    public async Task Export_WithoutDek_ZipsDatabaseDekSideFilesAndManifest()
    {
        var dbBytes = new byte[] { 1, 2, 3, 4, 5 };
        await File.WriteAllBytesAsync(_vaultPaths.DatabaseFile, dbBytes);
        await File.WriteAllBytesAsync(_vaultPaths.DatabaseFile + "-wal", [9]);
        await File.WriteAllBytesAsync(_vaultPaths.EncryptedDekFilePath, [7, 7]);

        await NewExporter().ExportAsync(Target, CancellationToken.None);

        File.Exists(Target).Should().BeTrue();
        using var archive = ZipFile.OpenRead(Target);
        var names = archive.Entries.Select(e => e.FullName).ToList();
        names.Should().Contain(["coffer.db", "coffer.db-wal", "dek.encrypted", "manifest.json"]);

        await using var dbEntry = archive.GetEntry("coffer.db")!.Open();
        using var ms = new MemoryStream();
        await dbEntry.CopyToAsync(ms);
        ms.ToArray().Should().Equal(dbBytes);
    }

    [Fact]
    public async Task Export_WhenTheSnapshotFails_LeavesThePreviousArchiveByteIdentical()
    {
        var previous = new byte[] { 0x50, 0x4B, 0x05, 0x06, 1, 2, 3 }; // an earlier archive at the same path
        await File.WriteAllBytesAsync(Target, previous);
        await File.WriteAllBytesAsync(_vaultPaths.DatabaseFile, [1, 2, 3]); // not a database
        await File.WriteAllBytesAsync(_vaultPaths.EncryptedDekFilePath, [7, 7]);
        _dekHolder.Set(RandomNumberGenerator.GetBytes(32)); // forces the keyed online backup, which fails

        var act = () => NewExporter().ExportAsync(Target, CancellationToken.None);

        await act.Should().ThrowAsync<Exception>();
        (await File.ReadAllBytesAsync(Target)).Should().Equal(previous);
        File.Exists(Target + ".tmp").Should().BeFalse("the temp archive is deleted on failure");
    }

    [Fact]
    public async Task Export_WithNoDekFile_ThrowsAndLeavesThePreviousArchive()
    {
        var previous = new byte[] { 9, 9, 9 };
        await File.WriteAllBytesAsync(Target, previous);
        await File.WriteAllBytesAsync(_vaultPaths.DatabaseFile, [1, 2, 3]);

        var act = () => NewExporter().ExportAsync(Target, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        (await File.ReadAllBytesAsync(Target)).Should().Equal(previous);
    }

    [Fact]
    public async Task Export_WithNoDatabase_Throws()
    {
        var act = () => NewExporter().ExportAsync(Target, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }
}
