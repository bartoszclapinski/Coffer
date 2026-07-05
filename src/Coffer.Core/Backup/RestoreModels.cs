namespace Coffer.Core.Backup;

/// <summary>
/// One available daily snapshot the owner can restore from — its date, the backup file name, and its size
/// on disk (for a human-readable listing). Derived from the files in the backups folder.
/// </summary>
public sealed record SnapshotInfo(DateOnly Date, string FileName, long SizeBytes);

/// <summary>
/// A staged restore request. Persisted as a marker (<c>restore-pending.json</c>) from a running app and
/// consumed at the next startup, before the database is opened, so the swap happens against a closed file.
/// </summary>
public sealed record PendingRestore(DateOnly SnapshotDate, string SnapshotFileName, DateTime RequestedAtUtc);

/// <summary>
/// The outcome of applying a pending restore: whether a swap happened, which snapshot date it restored
/// from, and where the pre-restore safety copy of the previous database was written (if one was taken).
/// </summary>
public sealed record RestoreResult(bool Applied, DateOnly? RestoredFrom, string? SafetyCopyPath);
