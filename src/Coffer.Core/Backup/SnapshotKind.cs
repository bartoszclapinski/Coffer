namespace Coffer.Core.Backup;

/// <summary>
/// How a local snapshot in the backups folder came to be: the once-a-day automatic
/// <see cref="Daily"/> snapshot (<c>coffer-YYYY-MM-DD.db</c>) or a <see cref="Manual"/> "Back up now"
/// snapshot (<c>coffer-YYYY-MM-DD-manual-HHMMSS.db</c>). Both are restorable and share one retention policy.
/// </summary>
public enum SnapshotKind
{
    Daily,
    Manual,
}
