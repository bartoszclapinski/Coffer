using System.Globalization;
using Coffer.Core.Backup;
using Coffer.Core.Security;
using Microsoft.Extensions.Logging;

namespace Coffer.Infrastructure.Backup;

/// <summary>
/// Local daily backup of the encrypted database (doc 08, Layer 1). Writes a dated snapshot
/// <c>backups/coffer-YYYY-MM-DD.db</c> atomically (a <c>.tmp</c> copy renamed on success), prunes daily
/// files older than 30 days and pre-migration files older than 90 days, and reports status from the files
/// on disk. Deliberately does not force a WAL checkpoint (it can run while the app is live) — the
/// <c>-wal</c>/<c>-shm</c> side-files are copied alongside so the snapshot is still consistent.
/// </summary>
public sealed class BackupService : IBackupService
{
    private const string BackupFolderName = "backups";
    private const string PreMigrationFolderName = "pre-migration";
    private const string DailyPattern = "coffer-*.db";
    private const int DailyRetentionDays = 30;
    private const int PreMigrationRetentionDays = 90;

    private readonly IVaultPaths _vaultPaths;
    private readonly ILogger<BackupService> _logger;

    public BackupService(IVaultPaths vaultPaths, ILogger<BackupService> logger)
    {
        ArgumentNullException.ThrowIfNull(vaultPaths);
        ArgumentNullException.ThrowIfNull(logger);
        _vaultPaths = vaultPaths;
        _logger = logger;
    }

    private string BackupsDir => Path.Combine(_vaultPaths.LocalAppDataFolder, BackupFolderName);

    private string PreMigrationDir => Path.Combine(BackupsDir, PreMigrationFolderName);

    public async Task<BackupResult> CreateDailySnapshotAsync(DateOnly today, CancellationToken ct)
    {
        var target = DailyPath(today);
        if (File.Exists(target))
        {
            Prune(today);
            return new BackupResult(false, target);
        }

        return await WriteAndPruneAsync(today, target, ct).ConfigureAwait(false);
    }

    public Task<BackupResult> CreateSnapshotNowAsync(DateOnly today, CancellationToken ct) =>
        WriteAndPruneAsync(today, DailyPath(today), ct);

    public Task<BackupStatus> GetStatusAsync(CancellationToken ct)
    {
        var dailyDates = EnumerateDailyDates().ToList();

        DateTime? lastPreMigration = null;
        if (Directory.Exists(PreMigrationDir))
        {
            var stamps = Directory.EnumerateFiles(PreMigrationDir, DailyPattern)
                .Select(File.GetLastWriteTimeUtc)
                .ToList();
            if (stamps.Count > 0)
            {
                lastPreMigration = stamps.Max();
            }
        }

        var status = new BackupStatus(
            dailyDates.Count == 0 ? null : dailyDates.Max(),
            dailyDates.Count,
            lastPreMigration);
        return Task.FromResult(status);
    }

    private async Task<BackupResult> WriteAndPruneAsync(DateOnly today, string target, CancellationToken ct)
    {
        var source = _vaultPaths.DatabaseFile;
        if (!File.Exists(source))
        {
            _logger.LogInformation("No database file at {Path} — nothing to snapshot", source);
            return new BackupResult(false, null);
        }

        Directory.CreateDirectory(BackupsDir);

        // Write to a temp set first, then move into place so an interrupted copy never leaves a
        // half-written file mistaken for a good snapshot. Move the main .db last, so its presence
        // implies the whole set landed.
        var tmp = target + ".tmp";
        DeleteQuietly(tmp);
        DeleteQuietly(tmp + "-wal");
        DeleteQuietly(tmp + "-shm");

        await BackupSnapshotWriter.CopyDatabaseAsync(source, tmp, ct).ConfigureAwait(false);

        MoveIntoPlace(tmp + "-wal", target + "-wal");
        MoveIntoPlace(tmp + "-shm", target + "-shm");
        File.Move(tmp, target, overwrite: true);

        _logger.LogInformation("Daily snapshot written to {Path}", target);
        Prune(today);
        return new BackupResult(true, target);
    }

    private void Prune(DateOnly today)
    {
        PruneFolder(BackupsDir, today, DailyRetentionDays, BackupRetention.ParseDailyDate);
        PruneFolder(PreMigrationDir, today, PreMigrationRetentionDays, BackupRetention.ParsePreMigrationDate);
    }

    private void PruneFolder(string dir, DateOnly today, int keepDays, Func<string, DateOnly?> parseDate)
    {
        if (!Directory.Exists(dir))
        {
            return;
        }

        var names = Directory.EnumerateFiles(dir, DailyPattern).Select(Path.GetFileName).OfType<string>();
        foreach (var name in BackupRetention.SelectExpired(names, today, keepDays, parseDate))
        {
            var path = Path.Combine(dir, name);
            DeleteQuietly(path);
            DeleteQuietly(path + "-wal");
            DeleteQuietly(path + "-shm");
        }
    }

    private IEnumerable<DateOnly> EnumerateDailyDates()
    {
        if (!Directory.Exists(BackupsDir))
        {
            yield break;
        }

        foreach (var path in Directory.EnumerateFiles(BackupsDir, DailyPattern))
        {
            if (BackupRetention.ParseDailyDate(Path.GetFileName(path)) is { } date)
            {
                yield return date;
            }
        }
    }

    private string DailyPath(DateOnly today) =>
        Path.Combine(BackupsDir, $"coffer-{today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}.db");

    private void MoveIntoPlace(string source, string destination)
    {
        if (File.Exists(source))
        {
            File.Move(source, destination, overwrite: true);
        }
        else
        {
            // No side-file in this snapshot — drop any stale one so the set stays consistent.
            DeleteQuietly(destination);
        }
    }

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
            _logger.LogWarning(ex, "Could not delete backup file {Path}", path);
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(ex, "Could not delete backup file {Path}", path);
        }
    }
}
