using System.Globalization;
using Coffer.Core.Security;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace Coffer.Infrastructure.Persistence;

/// <summary>
/// Copies the SQLCipher database file (and its WAL/SHM side-files) into
/// <c>{LocalAppDataFolder}/backups/pre-migration/</c> before a migration runs.
/// The copy is of the already-encrypted bytes, so the snapshot inherits the
/// same at-rest protection as the live database.
/// </summary>
public sealed class PreMigrationBackup : IPreMigrationBackup
{
    private const string _backupFolderName = "backups";
    private const string _preMigrationFolderName = "pre-migration";

    private readonly IVaultPaths _vaultPaths;
    private readonly ILogger<PreMigrationBackup> _logger;

    public PreMigrationBackup(IVaultPaths vaultPaths, ILogger<PreMigrationBackup> logger)
    {
        ArgumentNullException.ThrowIfNull(vaultPaths);
        ArgumentNullException.ThrowIfNull(logger);

        _vaultPaths = vaultPaths;
        _logger = logger;
    }

    public async Task<string?> CreateSnapshotAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var source = _vaultPaths.DatabaseFile;
        if (!File.Exists(source))
        {
            _logger.LogInformation("No database file at {Path} — nothing to back up before migration", source);
            return null;
        }

        // Flush the WAL into the main file so the copied .db is self-contained even
        // if the WAL/SHM side-files are not perfectly captured alongside it.
        SqliteConnection.ClearAllPools();

        var backupDir = Path.Combine(_vaultPaths.LocalAppDataFolder, _backupFolderName, _preMigrationFolderName);
        Directory.CreateDirectory(backupDir);

        var stamp = DateTime.UtcNow.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);
        var destination = Path.Combine(backupDir, $"coffer-{stamp}.db");

        await CopyFileAsync(source, destination, ct).ConfigureAwait(false);
        await CopySideFileIfPresentAsync(source + "-wal", destination + "-wal", ct).ConfigureAwait(false);
        await CopySideFileIfPresentAsync(source + "-shm", destination + "-shm", ct).ConfigureAwait(false);

        _logger.LogInformation("Pre-migration snapshot written to {Path}", destination);
        return destination;
    }

    private static async Task CopySideFileIfPresentAsync(string source, string destination, CancellationToken ct)
    {
        if (File.Exists(source))
        {
            await CopyFileAsync(source, destination, ct).ConfigureAwait(false);
        }
    }

    private static async Task CopyFileAsync(string source, string destination, CancellationToken ct)
    {
        await using var sourceStream = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read);
        await using var destinationStream = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        await sourceStream.CopyToAsync(destinationStream, ct).ConfigureAwait(false);
    }
}
