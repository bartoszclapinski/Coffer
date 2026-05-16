namespace Coffer.Infrastructure.Persistence;

public enum MigrationStatus
{
    UpToDate,
    Migrated,
}

public sealed record MigrationResult(MigrationStatus Status, IReadOnlyList<string> AppliedMigrations)
{
    public static MigrationResult UpToDate() => new(MigrationStatus.UpToDate, Array.Empty<string>());

    public static MigrationResult Migrated(IEnumerable<string> applied)
    {
        ArgumentNullException.ThrowIfNull(applied);
        return new MigrationResult(MigrationStatus.Migrated, applied.ToList());
    }
}
