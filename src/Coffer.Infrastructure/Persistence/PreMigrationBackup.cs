using System.Globalization;
using Coffer.Core.Security;
using Coffer.Infrastructure.Backup;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace Coffer.Infrastructure.Persistence;

/// <summary>
/// Snapshots the SQLCipher database into <c>{LocalAppDataFolder}/backups/pre-migration/</c> before a migration
/// runs (hard rule #8). The startup migration runs after login, so the DEK is held and the snapshot is taken
/// with SQLite's online backup API under the same DEK (one consistent, encrypted file — see
/// <see cref="BackupSnapshotWriter"/>). Should it ever run with the vault locked, it falls back to copying the
/// already-encrypted bytes (and the WAL/SHM side-files) after flushing the connection pools. Either way the
/// snapshot inherits the live database's at-rest protection.
/// </summary>
public sealed class PreMigrationBackup : IPreMigrationBackup
{
    private const string BackupFolderName = "backups";
    private const string PreMigrationFolderName = "pre-migration";

    private readonly IVaultPaths _vaultPaths;
    private readonly IDekHolder _dekHolder;
    private readonly ILogger<PreMigrationBackup> _logger;

    public PreMigrationBackup(IVaultPaths vaultPaths, IDekHolder dekHolder, ILogger<PreMigrationBackup> logger)
    {
        ArgumentNullException.ThrowIfNull(vaultPaths);
        ArgumentNullException.ThrowIfNull(dekHolder);
        ArgumentNullException.ThrowIfNull(logger);

        _vaultPaths = vaultPaths;
        _dekHolder = dekHolder;
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

        var backupDir = Path.Combine(_vaultPaths.LocalAppDataFolder, BackupFolderName, PreMigrationFolderName);
        Directory.CreateDirectory(backupDir);

        // The destination stamp is unique, so no existing file is ever overwritten.
        var stamp = DateTime.UtcNow.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);
        var destination = Path.Combine(backupDir, $"coffer-{stamp}.db");

        var dek = BackupSnapshotWriter.TryGetDek(_dekHolder);
        try
        {
            if (dek is null)
            {
                // Byte-copy fallback: closing the pooled connections triggers SQLite's WAL checkpoint so the
                // main .db is as consolidated as possible; the -wal/-shm copies cover anything outstanding.
                // Safe only because this runs at startup before any migration, with no concurrent writers.
                SqliteConnection.ClearAllPools();
            }

            var method = await BackupSnapshotWriter.WriteSnapshotAsync(source, destination, dek, ct).ConfigureAwait(false);
            _logger.LogInformation("Pre-migration snapshot written to {Path} ({Method})", destination, method);
        }
        finally
        {
            BackupSnapshotWriter.Zero(dek);
        }

        return destination;
    }
}
