namespace Coffer.Core.Backup;

/// <summary>
/// Local backup of the encrypted database: a rolling daily snapshot with retention, plus a read-only
/// status. Snapshots are copies of the already-SQLCipher-encrypted file, so they inherit its at-rest
/// protection — no re-encryption. The full restore side arrives in a later sprint (doc 08).
/// </summary>
public interface IBackupService
{
    /// <summary>
    /// Creates today's snapshot if one does not already exist, then prunes expired daily and
    /// pre-migration files. Idempotent within a day. No-op (returns <c>Created=false</c>) on a fresh
    /// install with no database yet.
    /// </summary>
    Task<BackupResult> CreateDailySnapshotAsync(DateOnly today, CancellationToken ct);

    /// <summary>
    /// Forces (re)writing today's snapshot even if one exists, then prunes. Backs the "Backup now" action.
    /// </summary>
    Task<BackupResult> CreateSnapshotNowAsync(DateOnly today, CancellationToken ct);

    /// <summary>Reads the current backup status from the files on disk.</summary>
    Task<BackupStatus> GetStatusAsync(CancellationToken ct);
}
