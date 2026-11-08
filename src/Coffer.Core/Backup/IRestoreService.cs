namespace Coffer.Core.Backup;

/// <summary>
/// Guided restore from a local snapshot (doc 08, "Restore flow"). A snapshot is the <em>same vault</em>
/// (same DEK, same master password), so restore is a file swap of already-encrypted bytes — no key handling,
/// no re-encryption, no password re-entry. Restore is two-phase: <see cref="StageRestoreAsync"/> records a
/// marker from the running app; <see cref="ApplyPendingRestoreAsync"/> performs the swap at the next startup,
/// before the database is opened, against a closed file. The current database is copied aside first so a
/// restore is reversible.
/// </summary>
public interface IRestoreService
{
    /// <summary>The available snapshots (daily and manual), newest first.</summary>
    Task<IReadOnlyList<SnapshotInfo>> ListSnapshotsAsync(CancellationToken ct);

    /// <summary>
    /// Stages a restore of the snapshot named <paramref name="snapshotFileName"/> (a name from
    /// <see cref="ListSnapshotsAsync"/>) by writing the pending-restore marker. Does not touch the database.
    /// Throws <see cref="ArgumentException"/> for a name that is not a snapshot name, and
    /// <see cref="System.IO.FileNotFoundException"/> if that snapshot does not exist.
    /// </summary>
    Task<PendingRestore> StageRestoreAsync(string snapshotFileName, CancellationToken ct);

    /// <summary>The staged restore, or <c>null</c> if none is staged (or the marker is unreadable).</summary>
    Task<PendingRestore?> GetPendingRestoreAsync(CancellationToken ct);

    /// <summary>Discards a staged restore without applying it.</summary>
    Task CancelPendingRestoreAsync(CancellationToken ct);

    /// <summary>
    /// Applies a staged restore: copies the current database aside (a reversible safety copy), swaps the
    /// snapshot's file set into place, then clears the marker. A no-op returning <c>Applied=false</c> when
    /// nothing is staged. Must run before the database is opened (an open SQLite file cannot be overwritten).
    /// The marker is cleared only after the database is in place, so an interrupted apply simply re-runs on
    /// the next startup.
    /// </summary>
    Task<RestoreResult> ApplyPendingRestoreAsync(CancellationToken ct);
}
