using System.Globalization;
using System.Text.Json;
using Coffer.Core.Backup;
using Coffer.Core.Security;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace Coffer.Infrastructure.Backup;

/// <summary>
/// Restores the encrypted database from a local snapshot — daily or manual (doc 08, "Restore flow"). Because a
/// snapshot is the same vault, this is a plain file swap of already-encrypted bytes — no crypto. A restore is
/// staged from the running app (<see cref="StageRestoreAsync"/> writes <c>restore-pending.json</c>) and applied
/// at the next startup (<see cref="ApplyPendingRestoreAsync"/>), before the database is opened, so the swap
/// runs against a closed file. The previous database is copied to <c>backups/pre-restore/</c> first so a
/// restore of the wrong snapshot is reversible. File I/O behind <see cref="IVaultPaths"/>; no schema, no network.
/// </summary>
/// <remarks>
/// Apply runs before login, so no DEK is held and both the safety copy and the swap use
/// <see cref="BackupSnapshotWriter"/>'s byte-copy path (temp file then move). Snapshots written by the online
/// backup are a single <c>.db</c> with no <c>-wal</c>/<c>-shm</c>; the swap drops any live side-file the
/// snapshot lacks, so a stale <c>-wal</c> of the replaced database is never replayed over the restored one.
/// </remarks>
public sealed class RestoreService : IRestoreService
{
    private const string BackupFolderName = "backups";
    private const string PreRestoreFolderName = "pre-restore";
    private const string SnapshotPattern = "coffer-*.db";
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
        var snapshots = new List<(SnapshotInfo Info, DateTime WrittenAtUtc)>();
        if (Directory.Exists(BackupsDir))
        {
            foreach (var path in Directory.EnumerateFiles(BackupsDir, SnapshotPattern))
            {
                var name = Path.GetFileName(path);
                if (BackupRetention.ParseSnapshot(name) is { } parsed)
                {
                    var file = new FileInfo(path);
                    snapshots.Add((
                        new SnapshotInfo(parsed.Date, name, file.Length, parsed.Kind, parsed.Time),
                        file.LastWriteTimeUtc));
                }
            }
        }

        // Newest day first; within a day the most recently written first (a daily name carries no time).
        IReadOnlyList<SnapshotInfo> ordered = snapshots
            .OrderByDescending(s => s.Info.Date)
            .ThenByDescending(s => s.WrittenAtUtc)
            .Select(s => s.Info)
            .ToList();
        return Task.FromResult(ordered);
    }

    public async Task<PendingRestore> StageRestoreAsync(string snapshotFileName, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(snapshotFileName);
        if (ParseSnapshotFileName(snapshotFileName) is not { } parsed)
        {
            throw new ArgumentException("Not a snapshot file name.", nameof(snapshotFileName));
        }

        var snapshotPath = Path.Combine(BackupsDir, snapshotFileName);
        if (!File.Exists(snapshotPath))
        {
            throw new FileNotFoundException($"No snapshot named {snapshotFileName} exists.", snapshotPath);
        }

        var pending = new PendingRestore(parsed.Date, snapshotFileName, DateTime.UtcNow);
        await WriteMarkerAsync(pending, ct).ConfigureAwait(false);
        _logger.LogInformation(
            "Staged restore from snapshot {File}; it will be applied on the next startup", snapshotFileName);
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

        if (ParseSnapshotFileName(pending.SnapshotFileName) is null)
        {
            // A hand-edited or corrupted marker must never point the swap at an arbitrary file. Leave the marker
            // so the failure is visible at startup and the owner can cancel it.
            _logger.LogError("Restore marker names an invalid snapshot; leaving the marker for the owner to cancel");
            throw new InvalidDataException("The restore marker does not name a valid snapshot.");
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
        // the replaced database cannot corrupt the restored one (an online-backup snapshot has none), then
        // copy the snapshot over via a temp file. No DEK exists before login, hence the byte-copy path.
        DeleteQuietly(dbFile + "-wal");
        DeleteQuietly(dbFile + "-shm");
        await BackupSnapshotWriter.WriteSnapshotAsync(snapshotPath, dbFile, dek: null, ct).ConfigureAwait(false);

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
        await BackupSnapshotWriter.WriteSnapshotAsync(dbFile, destination, dek: null, ct).ConfigureAwait(false);
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
        var names = Directory.EnumerateFiles(PreRestoreDir, SnapshotPattern).Select(Path.GetFileName).OfType<string>();
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

    // A bare file name (no directory part) that parses as a daily or manual snapshot name.
    private static SnapshotName? ParseSnapshotFileName(string fileName) =>
        Path.GetFileName(fileName) == fileName ? BackupRetention.ParseSnapshot(fileName) : null;

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
