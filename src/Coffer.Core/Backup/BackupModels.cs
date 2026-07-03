namespace Coffer.Core.Backup;

/// <summary>
/// A read-only snapshot of the local backup state, derived from the files on disk (so it is available
/// before the database can be opened). <see cref="LastDailySnapshot"/>/<see cref="DailyCount"/> describe
/// the rolling daily snapshots; <see cref="LastPreMigrationSnapshot"/> is the newest pre-migration copy.
/// </summary>
public sealed record BackupStatus(
    DateOnly? LastDailySnapshot,
    int DailyCount,
    DateTime? LastPreMigrationSnapshot);

/// <summary>The outcome of a snapshot request: whether a file was written, and its path (if any).</summary>
public sealed record BackupResult(bool Created, string? Path);
