using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using Coffer.Core.Backup;
using Coffer.Core.Security;
using Microsoft.Extensions.Logging;

namespace Coffer.Infrastructure.Backup;

/// <summary>
/// Exports the disaster-recovery archive (doc 08, Layer 3): a <c>.zip</c> of the encrypted database
/// (with any <c>-wal</c>/<c>-shm</c> side-files), the encrypted DEK, and a small manifest. Every entry is
/// already encrypted, so the archive is not double-encrypted — it is only useful with the master password
/// or the BIP39 seed.
/// </summary>
public sealed class ArchiveExporter : IArchiveExporter
{
    private readonly IVaultPaths _vaultPaths;
    private readonly ILogger<ArchiveExporter> _logger;

    public ArchiveExporter(IVaultPaths vaultPaths, ILogger<ArchiveExporter> logger)
    {
        ArgumentNullException.ThrowIfNull(vaultPaths);
        ArgumentNullException.ThrowIfNull(logger);
        _vaultPaths = vaultPaths;
        _logger = logger;
    }

    public async Task ExportAsync(string targetZipPath, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetZipPath);

        var database = _vaultPaths.DatabaseFile;
        if (!File.Exists(database))
        {
            throw new InvalidOperationException("There is no database to export.");
        }

        await using var zipStream = new FileStream(targetZipPath, FileMode.Create, FileAccess.Write, FileShare.None);
        using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create))
        {
            await AddFileAsync(archive, database, "coffer.db", ct).ConfigureAwait(false);
            await AddFileIfPresentAsync(archive, database + "-wal", "coffer.db-wal", ct).ConfigureAwait(false);
            await AddFileIfPresentAsync(archive, database + "-shm", "coffer.db-shm", ct).ConfigureAwait(false);
            await AddFileIfPresentAsync(archive, _vaultPaths.EncryptedDekFilePath, "dek.encrypted", ct).ConfigureAwait(false);
            await AddManifestAsync(archive, ct).ConfigureAwait(false);
        }

        _logger.LogInformation("Archive exported to {Path}", targetZipPath);
    }

    private static async Task AddManifestAsync(ZipArchive archive, CancellationToken ct)
    {
        var manifest = JsonSerializer.Serialize(new
        {
            app = "Coffer",
            appVersion = AppVersion(),
            createdAtUtc = DateTime.UtcNow.ToString("O"),
            note = "SQLCipher-encrypted. Restore requires the master password or BIP39 seed.",
        });

        var entry = archive.CreateEntry("manifest.json", CompressionLevel.Fastest);
        await using var entryStream = entry.Open();
        await using var writer = new StreamWriter(entryStream);
        await writer.WriteAsync(manifest.AsMemory(), ct).ConfigureAwait(false);
    }

    private static async Task AddFileIfPresentAsync(ZipArchive archive, string sourcePath, string entryName, CancellationToken ct)
    {
        if (File.Exists(sourcePath))
        {
            await AddFileAsync(archive, sourcePath, entryName, ct).ConfigureAwait(false);
        }
    }

    private static async Task AddFileAsync(ZipArchive archive, string sourcePath, string entryName, CancellationToken ct)
    {
        var entry = archive.CreateEntry(entryName, CompressionLevel.Fastest);
        await using var entryStream = entry.Open();
        await using var fileStream = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        await fileStream.CopyToAsync(entryStream, ct).ConfigureAwait(false);
    }

    private static string AppVersion()
    {
        var entry = Assembly.GetEntryAssembly();
        var informational = entry?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informational))
        {
            return informational;
        }

        return entry?.GetName().Version?.ToString() ?? "unknown";
    }
}
