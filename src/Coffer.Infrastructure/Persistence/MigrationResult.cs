namespace Coffer.Infrastructure.Persistence;

public enum MigrationStatus
{
    UpToDate,
    Migrated,
    Failed,
}

public sealed record MigrationResult(MigrationStatus Status, IReadOnlyList<string> AppliedMigrations)
{
    public static MigrationResult UpToDate() => new(MigrationStatus.UpToDate, Array.Empty<string>());

    public static MigrationResult Migrated(IEnumerable<string> applied)
    {
        ArgumentNullException.ThrowIfNull(applied);
        return new MigrationResult(MigrationStatus.Migrated, applied.ToList());
    }

    /// <summary>
    /// Reports a partially-completed migration run. <paramref name="appliedBeforeFailure"/>
    /// lists the migrations that did finish before the exception, so the caller (Sprint 6
    /// UI or a recovery routine) can present an honest "what just happened" picture and
    /// decide whether to restore from a backup.
    /// </summary>
    public static MigrationResult Failed(IEnumerable<string> appliedBeforeFailure)
    {
        ArgumentNullException.ThrowIfNull(appliedBeforeFailure);
        return new MigrationResult(MigrationStatus.Failed, appliedBeforeFailure.ToList());
    }
}
