using System.Globalization;
using Coffer.Core.Security;
using Coffer.Infrastructure.Backup;
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
    private const string BackupFolderName = "backups";
    private const string PreMigrationFolderName = "pre-migration";

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

        // Close pooled connections before copying. Closing the last open connection is
        // what triggers SQLite's WAL checkpoint, so the main .db is as consolidated as
        // possible; the -wal/-shm copies below cover anything still outstanding.
        SqliteConnection.ClearAllPools();

        var backupDir = Path.Combine(_vaultPaths.LocalAppDataFolder, BackupFolderName, PreMigrationFolderName);
        Directory.CreateDirectory(backupDir);

        var stamp = DateTime.UtcNow.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);
        var destination = Path.Combine(backupDir, $"coffer-{stamp}.db");

        // The three files are copied separately (non-atomically). This is safe only
        // because the snapshot runs at startup before any migration, with no concurrent
        // writers — do not reuse this mid-session where the WAL can change underneath us.
        // The destination stamp is unique, so no existing file is ever overwritten.
        await BackupSnapshotWriter.CopyDatabaseAsync(source, destination, ct).ConfigureAwait(false);

        _logger.LogInformation("Pre-migration snapshot written to {Path}", destination);
        return destination;
    }
}
