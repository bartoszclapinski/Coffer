using System.Globalization;
using System.Text.Json;
using Coffer.Core.Backup;
using Coffer.Core.Security;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace Coffer.Infrastructure.Backup;

/// <summary>
/// Restores the encrypted database from a local daily snapshot (doc 08, "Restore flow"). Because a snapshot
/// is the same vault, this is a plain file swap of already-encrypted bytes — no crypto. A restore is staged
/// from the running app (<see cref="StageRestoreAsync"/> writes <c>restore-pending.json</c>) and applied at
/// the next startup (<see cref="ApplyPendingRestoreAsync"/>), before the database is opened, so the swap runs
/// against a closed file. The previous database is copied to <c>backups/pre-restore/</c> first so a restore
/// of the wrong snapshot is reversible. File I/O behind <see cref="IVaultPaths"/>; no schema, no network.
/// </summary>
public sealed class RestoreService : IRestoreService
{
    private const string BackupFolderName = "backups";
    private const string PreRestoreFolderName = "pre-restore";
    private const string DailyPattern = "coffer-*.db";
    private const string MarkerFileName = "restore-pending.json";
    private const int PreRestoreRetentionDays = 90;

    private static readonly JsonSerializerOptions _markerJson = new() { WriteIndented = true };

    private readonly IVaultPaths _vaultPaths;
    private readonly ILogger<RestoreService> _logger;

    public RestoreService(IVaultPaths vaultPaths, ILogger<RestoreService> logger)
    {
        ArgumentNullException.ThrowIfNull(vaultPaths);
        ArgumentNullException.ThrowIfNull(logger);
        _vaultPaths = vaultPaths;
        _logger = logger;
    }

    private string BackupsDir => Path.Combine(_vaultPaths.LocalAppDataFolder, BackupFolderName);

    private string PreRestoreDir => Path.Combine(BackupsDir, PreRestoreFolderName);

    private string MarkerPath => Path.Combine(_vaultPaths.LocalAppDataFolder, MarkerFileName);

    public Task<IReadOnlyList<SnapshotInfo>> ListSnapshotsAsync(CancellationToken ct)
    {
        var snapshots = new List<SnapshotInfo>();
        if (Directory.Exists(BackupsDir))
        {
            foreach (var path in Directory.EnumerateFiles(BackupsDir, DailyPattern))
            {
                var name = Path.GetFileName(path);
                if (BackupRetention.ParseDailyDate(name) is { } date)
                {
                    snapshots.Add(new SnapshotInfo(date, name, new FileInfo(path).Length));
                }
            }
        }

        IReadOnlyList<SnapshotInfo> ordered = snapshots.OrderByDescending(s => s.Date).ToList();
        return Task.FromResult(ordered);
    }

    public async Task<PendingRestore> StageRestoreAsync(DateOnly snapshotDate, CancellationToken ct)
    {
        var fileName = SnapshotFileName(snapshotDate);
        var snapshotPath = Path.Combine(BackupsDir, fileName);
        if (!File.Exists(snapshotPath))
        {
            throw new FileNotFoundException(
                $"No snapshot exists for {snapshotDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}.",
                snapshotPath);
        }

        var pending = new PendingRestore(snapshotDate, fileName, DateTime.UtcNow);
        await WriteMarkerAsync(pending, ct).ConfigureAwait(false);
        _logger.LogInformation(
            "Staged restore from snapshot {Date}; it will be applied on the next startup", snapshotDate);
        return pending;
    }

    public async Task<PendingRestore?> GetPendingRestoreAsync(CancellationToken ct)
    {
        if (!File.Exists(MarkerPath))
        {
            return null;
        }

        try
        {
            var json = await File.ReadAllTextAsync(MarkerPath, ct).ConfigureAwait(false);
            return JsonSerializer.Deserialize<PendingRestore>(json, _markerJson);
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            _logger.LogWarning(ex, "Restore marker at {Path} is unreadable; ignoring it", MarkerPath);
            return null;
        }
    }

    public Task CancelPendingRestoreAsync(CancellationToken ct)
    {
        DeleteQuietly(MarkerPath);
        return Task.CompletedTask;
    }

    public async Task<RestoreResult> ApplyPendingRestoreAsync(CancellationToken ct)
    {
        var pending = await GetPendingRestoreAsync(ct).ConfigureAwait(false);
        if (pending is null)
        {
            return new RestoreResult(false, null, null);
        }

        var snapshotPath = Path.Combine(BackupsDir, pending.SnapshotFileName);
        if (!File.Exists(snapshotPath))
        {
            // The staged snapshot vanished (external delete/tamper). Leave the marker so the failure is
            // visible at startup and the owner can cancel it, rather than silently dropping the request.
            _logger.LogError(
                "Staged restore snapshot {Path} is missing; leaving the marker for the owner to cancel",
                snapshotPath);
            throw new FileNotFoundException("The snapshot staged for restore no longer exists.", snapshotPath);
        }

        // No live DB connections should exist this early in startup, but close any pools defensively so the
        // file is free to overwrite (matches PreMigrationBackup).
        SqliteConnection.ClearAllPools();

        var dbFile = _vaultPaths.DatabaseFile;
        string? safetyCopyPath = null;
        if (File.Exists(dbFile))
        {
            safetyCopyPath = await CreateSafetyCopyAsync(dbFile, ct).ConfigureAwait(false);
        }

        // Restore the snapshot's exact on-disk set: drop any live side-file first so a stale -wal/-shm from
        // the replaced database cannot corrupt the restored one, then copy the snapshot over.
        DeleteQuietly(dbFile + "-wal");
        DeleteQuietly(dbFile + "-shm");
        await BackupSnapshotWriter.CopyDatabaseAsync(snapshotPath, dbFile, ct).ConfigureAwait(false);

        // Only now, with the database in place, clear the marker — an interrupted apply simply re-runs.
        DeleteQuietly(MarkerPath);
        PruneSafetyCopies();

        _logger.LogInformation(
            "Restored database from snapshot {Date} ({File})", pending.SnapshotDate, pending.SnapshotFileName);
        return new RestoreResult(true, pending.SnapshotDate, safetyCopyPath);
    }

    private async Task<string> CreateSafetyCopyAsync(string dbFile, CancellationToken ct)
    {
        Directory.CreateDirectory(PreRestoreDir);
        var stamp = DateTime.UtcNow.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);
        var destination = Path.Combine(PreRestoreDir, $"coffer-{stamp}.db");
        await BackupSnapshotWriter.CopyDatabaseAsync(dbFile, destination, ct).ConfigureAwait(false);
        _logger.LogInformation("Pre-restore safety copy written to {Path}", destination);
        return destination;
    }

    private void PruneSafetyCopies()
    {
        if (!Directory.Exists(PreRestoreDir))
        {
            return;
        }

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var names = Directory.EnumerateFiles(PreRestoreDir, DailyPattern).Select(Path.GetFileName).OfType<string>();
        foreach (var name in BackupRetention.SelectExpired(
            names, today, PreRestoreRetentionDays, BackupRetention.ParsePreMigrationDate))
        {
            var path = Path.Combine(PreRestoreDir, name);
            DeleteQuietly(path);
            DeleteQuietly(path + "-wal");
            DeleteQuietly(path + "-shm");
        }
    }

    private async Task WriteMarkerAsync(PendingRestore pending, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(pending, _markerJson);
        var tmp = MarkerPath + ".tmp";
        await File.WriteAllTextAsync(tmp, json, ct).ConfigureAwait(false);
        File.Move(tmp, MarkerPath, overwrite: true);
    }

    private static string SnapshotFileName(DateOnly date) =>
        $"coffer-{date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}.db";

    private void DeleteQuietly(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException ex)
        {
            _logger.LogWarning(ex, "Could not delete file {Path}", path);
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(ex, "Could not delete file {Path}", path);
        }
    }
}
