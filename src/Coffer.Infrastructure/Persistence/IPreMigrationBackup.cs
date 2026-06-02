namespace Coffer.Infrastructure.Persistence;

/// <summary>
/// Creates the mandatory pre-migration snapshot of the encrypted database
/// (hard rule #8). The full backup/restore system arrives in a later sprint
/// (doc 08); this minimal contract just guarantees a recoverable copy exists
/// before any schema change is applied.
/// </summary>
public interface IPreMigrationBackup
{
    /// <summary>
    /// Copies the current encrypted database (and its WAL/SHM side-files, if
    /// present) into a retained backup folder. On a fresh install with no
    /// database file yet, this is a no-op and returns <c>null</c>.
    /// </summary>
    /// <returns>The path of the snapshot created, or <c>null</c> if there was nothing to back up.</returns>
    Task<string?> CreateSnapshotAsync(CancellationToken ct);
}
