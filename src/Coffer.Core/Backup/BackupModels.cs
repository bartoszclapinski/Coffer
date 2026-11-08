namespace Coffer.Core.Backup;

/// <summary>
/// A read-only snapshot of the local backup state, derived from the files on disk (so it is available
/// before the database can be opened). <see cref="LastDailySnapshot"/> is the newest automatic daily
/// snapshot's date; <see cref="DailyCount"/> counts every restorable snapshot (daily and manual);
/// <see cref="LastPreMigrationSnapshot"/> is the newest pre-migration copy. <see cref="NewestSnapshotAtUtc"/>
/// is when the newest restorable snapshot (of either kind) was written, and <see cref="LastFailureAtUtc"/> is
/// when the most recent snapshot attempt failed — cleared again by the next successful snapshot — so a
/// silently failing backup is visible in Settings, not only in the log.
/// </summary>
public sealed record BackupStatus(
    DateOnly? LastDailySnapshot,
    int DailyCount,
    DateTime? LastPreMigrationSnapshot,
    DateTime? NewestSnapshotAtUtc = null,
    DateTime? LastFailureAtUtc = null);

/// <summary>The outcome of a snapshot request: whether a file was written, and its path (if any).</summary>
public sealed record BackupResult(bool Created, string? Path);
