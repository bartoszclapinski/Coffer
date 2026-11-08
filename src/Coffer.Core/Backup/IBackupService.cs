namespace Coffer.Core.Backup;

/// <summary>
/// Local backup of the encrypted database: a rolling daily snapshot plus on-demand manual snapshots, with
/// retention, and a read-only status. Snapshots are consistent images of the SQLCipher-encrypted database
/// under the same DEK, so they inherit its at-rest protection and restore as a plain file swap (doc 08).
/// </summary>
public interface IBackupService
{
    /// <summary>
    /// Creates today's daily snapshot if one does not already exist, then prunes expired daily/manual and
    /// pre-migration files. Idempotent within a day. No-op (returns <c>Created=false</c>) on a fresh
    /// install with no database yet. A failure is recorded for <see cref="GetStatusAsync"/> and rethrown.
    /// </summary>
    Task<BackupResult> CreateDailySnapshotAsync(DateOnly today, CancellationToken ct);

    /// <summary>
    /// Writes a distinct, timestamped manual snapshot (never overwriting today's daily one), then prunes.
    /// Backs the "Back up now" action. A failure is recorded for <see cref="GetStatusAsync"/> and rethrown.
    /// </summary>
    Task<BackupResult> CreateSnapshotNowAsync(DateOnly today, CancellationToken ct);

    /// <summary>Reads the current backup status from the files on disk.</summary>
    Task<BackupStatus> GetStatusAsync(CancellationToken ct);
}
