using System.IO.Compression;
using Coffer.Infrastructure.Backup;
using Coffer.Infrastructure.Tests.Security;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Coffer.Infrastructure.Tests.Backup;

public class ArchiveExporterTests : IDisposable
{
    private readonly TestVaultPaths _vaultPaths = new();

    public void Dispose() => _vaultPaths.Dispose();

    private ArchiveExporter NewExporter() => new(_vaultPaths, NullLogger<ArchiveExporter>.Instance);

    [Fact]
    public async Task Export_ZipsDatabaseDekSideFilesAndManifest()
    {
        var dbBytes = new byte[] { 1, 2, 3, 4, 5 };
        await File.WriteAllBytesAsync(_vaultPaths.DatabaseFile, dbBytes);
        await File.WriteAllBytesAsync(_vaultPaths.DatabaseFile + "-wal", [9]);
        await File.WriteAllBytesAsync(_vaultPaths.EncryptedDekFilePath, [7, 7]);
        var target = Path.Combine(_vaultPaths.LocalAppDataFolder, "archive.zip");

        await NewExporter().ExportAsync(target, CancellationToken.None);

        File.Exists(target).Should().BeTrue();
        using var archive = ZipFile.OpenRead(target);
        var names = archive.Entries.Select(e => e.FullName).ToList();
        names.Should().Contain(["coffer.db", "coffer.db-wal", "dek.encrypted", "manifest.json"]);

        await using var dbEntry = archive.GetEntry("coffer.db")!.Open();
        using var ms = new MemoryStream();
        await dbEntry.CopyToAsync(ms);
        ms.ToArray().Should().Equal(dbBytes);
    }

    [Fact]
    public async Task Export_WithNoDatabase_Throws()
    {
        var target = Path.Combine(_vaultPaths.LocalAppDataFolder, "archive.zip");

        var act = () => NewExporter().ExportAsync(target, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }
}
