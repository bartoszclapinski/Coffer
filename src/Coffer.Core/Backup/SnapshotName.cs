namespace Coffer.Core.Backup;

/// <summary>
/// A parsed snapshot file name (see <see cref="BackupRetention.ParseSnapshot"/>): the local date it was taken,
/// the local time of day for a manual snapshot (a daily name carries no time), and its kind.
/// </summary>
public readonly record struct SnapshotName(DateOnly Date, TimeOnly? Time, SnapshotKind Kind)
{
    /// <summary>A sortable local timestamp: the date plus the time of day (midnight for a daily snapshot).</summary>
    public DateTime SortKey => Date.ToDateTime(Time ?? TimeOnly.MinValue);
}
