using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using Coffer.Core.Backup;
using Coffer.Core.Security;
using Microsoft.Extensions.Logging;

namespace Coffer.Infrastructure.Backup;

/// <summary>
/// Exports the disaster-recovery archive (doc 08, Layer 3): a <c>.zip</c> of the encrypted database, the
/// encrypted DEK, and a small manifest. Every entry is already encrypted, so the archive is not
/// double-encrypted — it is only useful with the master password or the BIP39 seed.
/// </summary>
/// <remarks>
/// The database entry comes from a consistent snapshot taken through <see cref="BackupSnapshotWriter"/>
/// (online backup under the same DEK — one self-contained <c>coffer.db</c>; only when the vault is locked does
/// it fall back to a byte copy that carries any <c>-wal</c>/<c>-shm</c> entries along). The archive is written
/// atomically: it is built as <c>target.tmp</c>, re-opened to verify the expected entries are present and
/// non-empty, and only then moved over the target — a failure deletes the temp file and leaves any previous
/// archive at the target untouched.
/// </remarks>
public sealed class ArchiveExporter : IArchiveExporter
{
    private const string StagingFolderName = "archive-staging";
    private const string StagingFileName = "coffer.db";
    private const string DatabaseEntry = "coffer.db";
    private const string DekEntry = "dek.encrypted";
    private const string ManifestEntry = "manifest.json";

    private readonly IVaultPaths _vaultPaths;
    private readonly IDekHolder _dekHolder;
    private readonly ILogger<ArchiveExporter> _logger;

    public ArchiveExporter(IVaultPaths vaultPaths, IDekHolder dekHolder, ILogger<ArchiveExporter> logger)
    {
        ArgumentNullException.ThrowIfNull(vaultPaths);
        ArgumentNullException.ThrowIfNull(dekHolder);
        ArgumentNullException.ThrowIfNull(logger);
        _vaultPaths = vaultPaths;
        _dekHolder = dekHolder;
        _logger = logger;
    }

    private string StagingDir => Path.Combine(_vaultPaths.LocalAppDataFolder, "backups", StagingFolderName);

    public async Task ExportAsync(string targetZipPath, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetZipPath);

        var database = _vaultPaths.DatabaseFile;
        if (!File.Exists(database))
        {
            throw new InvalidOperationException("There is no database to export.");
        }

        var dekFile = _vaultPaths.EncryptedDekFilePath;
        if (!File.Exists(dekFile))
        {
            // Without the wrapped DEK the database in the archive could never be opened again.
            throw new InvalidOperationException("There is no encrypted DEK to export.");
        }

        var tmpZip = targetZipPath + ".tmp";
        var staging = Path.Combine(StagingDir, StagingFileName);
        var dek = BackupSnapshotWriter.TryGetDek(_dekHolder);
        try
        {
            ResetStagingDir();
            var method = await BackupSnapshotWriter.WriteSnapshotAsync(database, staging, dek, ct).ConfigureAwait(false);

            DeleteIfExists(tmpZip);
            await WriteArchiveAsync(tmpZip, staging, dekFile, ct).ConfigureAwait(false);
            VerifyArchive(tmpZip, expectSideFiles: File.Exists(staging + "-wal"));

            File.Move(tmpZip, targetZipPath, overwrite: true);
            _logger.LogInformation("Archive exported to {Path} ({Method})", targetZipPath, method);
        }
        catch
        {
            DeleteQuietly(tmpZip);
            throw;
        }
        finally
        {
            BackupSnapshotWriter.Zero(dek);
            CleanUpStagingDir();
        }
    }

    private static async Task WriteArchiveAsync(string zipPath, string staging, string dekFile, CancellationToken ct)
    {
        await using var zipStream = new FileStream(zipPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        using var archive = new ZipArchive(zipStream, ZipArchiveMode.Create);
        await AddFileAsync(archive, staging, DatabaseEntry, ct).ConfigureAwait(false);
        await AddFileIfPresentAsync(archive, staging + "-wal", DatabaseEntry + "-wal", ct).ConfigureAwait(false);
        await AddFileIfPresentAsync(archive, staging + "-shm", DatabaseEntry + "-shm", ct).ConfigureAwait(false);
        await AddFileAsync(archive, dekFile, DekEntry, ct).ConfigureAwait(false);
        await AddManifestAsync(archive, ct).ConfigureAwait(false);
    }

    private static void VerifyArchive(string zipPath, bool expectSideFiles)
    {
        using var archive = ZipFile.OpenRead(zipPath);
        RequireNonEmpty(archive, DatabaseEntry);
        RequireNonEmpty(archive, DekEntry);
        RequireNonEmpty(archive, ManifestEntry);
        // A checkpointed -wal is legitimately empty, so the side-file only has to be present.
        if (expectSideFiles && archive.GetEntry(DatabaseEntry + "-wal") is null)
        {
            throw new InvalidDataException($"The archive is missing the '{DatabaseEntry}-wal' entry.");
        }
    }

    private static void RequireNonEmpty(ZipArchive archive, string entryName)
    {
        if (archive.GetEntry(entryName) is not { Length: > 0 })
        {
            throw new InvalidDataException($"The archive is missing a non-empty '{entryName}' entry.");
        }
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

        var entry = archive.CreateEntry(ManifestEntry, CompressionLevel.Fastest);
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
        await using var fileStream = new FileStream(
            sourcePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        await fileStream.CopyToAsync(entryStream, ct).ConfigureAwait(false);
    }

    private void ResetStagingDir()
    {
        CleanUpStagingDir();
        Directory.CreateDirectory(StagingDir);
    }

    private void CleanUpStagingDir()
    {
        try
        {
            if (Directory.Exists(StagingDir))
            {
                Directory.Delete(StagingDir, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Could not clean up the archive staging folder {Path}", StagingDir);
        }
    }

    private void DeleteQuietly(string path)
    {
        try
        {
            DeleteIfExists(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Could not delete the temporary archive {Path}", path);
        }
    }

    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
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
