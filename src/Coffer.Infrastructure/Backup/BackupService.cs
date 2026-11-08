using System.Text.Json;
using Coffer.Core.Backup;
using Coffer.Core.Security;
using Microsoft.Extensions.Logging;

namespace Coffer.Infrastructure.Backup;

/// <summary>
/// Local backup of the encrypted database (doc 08, Layer 1). Writes a daily snapshot
/// <c>backups/coffer-YYYY-MM-DD.db</c> and, on "Back up now", a distinct manual snapshot
/// <c>backups/coffer-YYYY-MM-DD-manual-HHMMSS.db</c> — both through <see cref="BackupSnapshotWriter"/>
/// (SQLite online backup on keyed connections, so a snapshot never fights the app's pooled connections and is
/// a single consistent file under the same DEK; temp file then move). Retention always keeps the newest
/// <see cref="KeepNewestSnapshots"/> snapshots, then drops daily/manual files older than 30 days and
/// pre-migration files older than 90 days. A failed attempt is recorded in <c>backups/last-failure.json</c>
/// (timestamp + exception type only — no paths, no data) so <see cref="GetStatusAsync"/> can surface it in
/// Settings; the next successful snapshot clears it.
/// </summary>
public sealed class BackupService : IBackupService
{
    /// <summary>How many of the newest snapshots retention keeps regardless of their age.</summary>
    public const int KeepNewestSnapshots = 7;

    private const string BackupFolderName = "backups";
    private const string PreMigrationFolderName = "pre-migration";
    private const string SnapshotPattern = "coffer-*.db";
    private const string FailureFileName = "last-failure.json";
    private const string RestoreMarkerFileName = "restore-pending.json";
    private const int DailyRetentionDays = 30;
    private const int PreMigrationRetentionDays = 90;

    private readonly IVaultPaths _vaultPaths;
    private readonly IDekHolder _dekHolder;
    private readonly ILogger<BackupService> _logger;
    private readonly TimeProvider _timeProvider;

    public BackupService(
        IVaultPaths vaultPaths,
        IDekHolder dekHolder,
        ILogger<BackupService> logger,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(vaultPaths);
        ArgumentNullException.ThrowIfNull(dekHolder);
        ArgumentNullException.ThrowIfNull(logger);
        _vaultPaths = vaultPaths;
        _dekHolder = dekHolder;
        _logger = logger;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    private string BackupsDir => Path.Combine(_vaultPaths.LocalAppDataFolder, BackupFolderName);

    private string PreMigrationDir => Path.Combine(BackupsDir, PreMigrationFolderName);

    private string FailurePath => Path.Combine(BackupsDir, FailureFileName);

    public async Task<BackupResult> CreateDailySnapshotAsync(DateOnly today, CancellationToken ct)
    {
        var target = Path.Combine(BackupsDir, BackupRetention.DailyFileName(today));
        if (File.Exists(target))
        {
            Prune(today);
            return new BackupResult(false, target);
        }

        return await WriteAndPruneAsync(today, target, ct).ConfigureAwait(false);
    }

    public Task<BackupResult> CreateSnapshotNowAsync(DateOnly today, CancellationToken ct)
    {
        // Date and time come from one clock read, so a snapshot taken across midnight is not named a day early.
        var now = _timeProvider.GetLocalNow().DateTime;
        var day = DateOnly.FromDateTime(now);
        var target = Path.Combine(BackupsDir, BackupRetention.ManualFileName(day, TimeOnly.FromDateTime(now)));
        return WriteAndPruneAsync(day, target, ct);
    }

    public Task<BackupStatus> GetStatusAsync(CancellationToken ct)
    {
        DateOnly? lastDaily = null;
        DateTime? newestAtUtc = null;
        var count = 0;
        if (Directory.Exists(BackupsDir))
        {
            foreach (var path in Directory.EnumerateFiles(BackupsDir, SnapshotPattern))
            {
                if (BackupRetention.ParseSnapshot(Path.GetFileName(path)) is not { } parsed)
                {
                    continue;
                }

                count++;
                if (parsed.Kind == SnapshotKind.Daily && (lastDaily is null || parsed.Date > lastDaily))
                {
                    lastDaily = parsed.Date;
                }

                var writtenAtUtc = File.GetLastWriteTimeUtc(path);
                if (newestAtUtc is null || writtenAtUtc > newestAtUtc)
                {
                    newestAtUtc = writtenAtUtc;
                }
            }
        }

        DateTime? lastPreMigration = null;
        if (Directory.Exists(PreMigrationDir))
        {
            var stamps = Directory.EnumerateFiles(PreMigrationDir, SnapshotPattern)
                .Select(File.GetLastWriteTimeUtc)
                .ToList();
            if (stamps.Count > 0)
            {
                lastPreMigration = stamps.Max();
            }
        }

        var status = new BackupStatus(lastDaily, count, lastPreMigration, newestAtUtc, ReadLastFailureAtUtc());
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

        var dek = BackupSnapshotWriter.TryGetDek(_dekHolder);
        SnapshotMethod method;
        try
        {
            method = await BackupSnapshotWriter.WriteSnapshotAsync(source, target, dek, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Recorded so Settings can show the failure, then rethrown so the caller logs/reports it as before.
            RecordFailure(ex);
            throw;
        }
        finally
        {
            BackupSnapshotWriter.Zero(dek);
        }

        ClearFailure();
        _logger.LogInformation("Snapshot written to {Path} ({Method})", target, method);
        Prune(today);
        return new BackupResult(true, target);
    }

    private void Prune(DateOnly today)
    {
        if (Directory.Exists(BackupsDir))
        {
            var names = Directory.EnumerateFiles(BackupsDir, SnapshotPattern).Select(Path.GetFileName).OfType<string>();
            var staged = ReadStagedRestoreFileName();
            DeleteSets(
                BackupsDir,
                BackupRetention.SelectExpiredSnapshots(names, today, DailyRetentionDays, KeepNewestSnapshots)
                    .Where(name => !string.Equals(name, staged, StringComparison.OrdinalIgnoreCase)));
        }

        if (Directory.Exists(PreMigrationDir))
        {
            var names = Directory.EnumerateFiles(PreMigrationDir, SnapshotPattern).Select(Path.GetFileName).OfType<string>();
            DeleteSets(
                PreMigrationDir,
                BackupRetention.SelectExpired(
                    names, today, PreMigrationRetentionDays, BackupRetention.ParsePreMigrationDate));
        }
    }

    /// <summary>
    /// The snapshot a staged restore will swap in on the next start (from <c>restore-pending.json</c>), or null.
    /// Retention must never delete it, or the restore the owner asked for would fail at startup.
    /// </summary>
    private string? ReadStagedRestoreFileName()
    {
        var marker = Path.Combine(_vaultPaths.LocalAppDataFolder, RestoreMarkerFileName);
        if (!File.Exists(marker))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(marker));
            return doc.RootElement.TryGetProperty("SnapshotFileName", out var name)
                && name.ValueKind == JsonValueKind.String
                ? name.GetString()
                : null;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Restore marker is unreadable; pruning without excluding a staged snapshot");
            return null;
        }
    }

    private void DeleteSets(string dir, IEnumerable<string> names)
    {
        foreach (var name in names)
        {
            var path = Path.Combine(dir, name);
            DeleteQuietly(path);
            DeleteQuietly(path + "-wal");
            DeleteQuietly(path + "-shm");
        }
    }

    private void RecordFailure(Exception ex)
    {
        try
        {
            Directory.CreateDirectory(BackupsDir);
            var record = new FailureRecord(_timeProvider.GetUtcNow().UtcDateTime, ex.GetType().Name);
            File.WriteAllText(FailurePath, JsonSerializer.Serialize(record));
        }
        catch (Exception writeEx) when (writeEx is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(writeEx, "Could not record the backup failure");
        }
    }

    private void ClearFailure() => DeleteQuietly(FailurePath);

    private DateTime? ReadLastFailureAtUtc()
    {
        if (!File.Exists(FailurePath))
        {
            return null;
        }

        try
        {
            var record = JsonSerializer.Deserialize<FailureRecord>(File.ReadAllText(FailurePath));
            return record is null ? null : DateTime.SpecifyKind(record.FailedAtUtc, DateTimeKind.Utc);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Backup failure record at {Path} is unreadable; ignoring it", FailurePath);
            return null;
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

    private sealed record FailureRecord(DateTime FailedAtUtc, string Reason);
}
