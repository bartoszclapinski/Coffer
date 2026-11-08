using System.Globalization;

namespace Coffer.Core.Backup;

/// <summary>
/// Pure retention policy and naming for backup files. Given the backup filenames, today's date, a keep-days
/// window, and a date parser, it returns the filenames that have aged out. Dates are parsed from the filename
/// so no database is needed and it works before login. Unparsable names are ignored (never deleted).
/// </summary>
public static class BackupRetention
{
    private const string Prefix = "coffer-";
    private const string Suffix = ".db";
    private const string DailyFormat = "yyyy-MM-dd";
    private const string ManualMarker = "-manual-";
    private const string ManualTimeFormat = "HHmmss";

    /// <summary>The daily snapshot file name for <paramref name="date"/>: <c>coffer-YYYY-MM-DD.db</c>.</summary>
    public static string DailyFileName(DateOnly date) =>
        $"{Prefix}{date.ToString(DailyFormat, CultureInfo.InvariantCulture)}{Suffix}";

    /// <summary>
    /// The manual ("Back up now") snapshot file name: <c>coffer-YYYY-MM-DD-manual-HHMMSS.db</c> (local date and
    /// time). Distinct from the daily name, so a manual snapshot never overwrites today's daily one.
    /// </summary>
    public static string ManualFileName(DateOnly date, TimeOnly time) =>
        $"{Prefix}{date.ToString(DailyFormat, CultureInfo.InvariantCulture)}{ManualMarker}"
        + $"{time.ToString(ManualTimeFormat, CultureInfo.InvariantCulture)}{Suffix}";

    /// <summary>
    /// Parses a restorable snapshot name — daily (<c>coffer-YYYY-MM-DD.db</c>) or manual
    /// (<c>coffer-YYYY-MM-DD-manual-HHMMSS.db</c>) — or returns <c>null</c> for anything else (side-files,
    /// pre-migration names, temp files, junk).
    /// </summary>
    public static SnapshotName? ParseSnapshot(string fileName)
    {
        if (ParseDailyDate(fileName) is { } daily)
        {
            return new SnapshotName(daily, null, SnapshotKind.Daily);
        }

        var core = Core(fileName);
        if (core is null || core.Length != DailyFormat.Length + ManualMarker.Length + ManualTimeFormat.Length)
        {
            return null;
        }

        var datePart = core[..DailyFormat.Length];
        var marker = core.Substring(DailyFormat.Length, ManualMarker.Length);
        var timePart = core[(DailyFormat.Length + ManualMarker.Length)..];
        return marker == ManualMarker
            && DateOnly.TryParseExact(datePart, DailyFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            && TimeOnly.TryParseExact(timePart, ManualTimeFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var time)
            ? new SnapshotName(date, time, SnapshotKind.Manual)
            : null;
    }

    /// <summary>
    /// Retention for the snapshot folder (daily and manual names together): the newest
    /// <paramref name="keepNewest"/> snapshots are always kept regardless of age — so a machine that was off
    /// for a month, or a clock that jumped, can never prune every backup — and of the rest, those dated
    /// strictly older than <paramref name="today"/> − <paramref name="keepDays"/> are returned as expired.
    /// Unparsable names are ignored (never deleted).
    /// </summary>
    public static IReadOnlyList<string> SelectExpiredSnapshots(
        IEnumerable<string> fileNames,
        DateOnly today,
        int keepDays,
        int keepNewest)
    {
        ArgumentNullException.ThrowIfNull(fileNames);
        ArgumentOutOfRangeException.ThrowIfNegative(keepNewest);

        var cutoff = today.AddDays(-keepDays);
        var snapshots = new List<(string Name, SnapshotName Parsed)>();
        foreach (var name in fileNames)
        {
            if (ParseSnapshot(name) is { } parsed)
            {
                snapshots.Add((name, parsed));
            }
        }

        return snapshots
            .OrderByDescending(item => item.Parsed.SortKey)
            .ThenByDescending(item => item.Name, StringComparer.Ordinal)
            .Skip(keepNewest)
            .Where(item => item.Parsed.Date < cutoff)
            .Select(item => item.Name)
            .ToList();
    }

    /// <summary>The filenames whose parsed date is strictly older than <paramref name="today"/> − <paramref name="keepDays"/>.</summary>
    public static IReadOnlyList<string> SelectExpired(
        IEnumerable<string> fileNames,
        DateOnly today,
        int keepDays,
        Func<string, DateOnly?> parseDate)
    {
        ArgumentNullException.ThrowIfNull(fileNames);
        ArgumentNullException.ThrowIfNull(parseDate);

        var cutoff = today.AddDays(-keepDays);
        var expired = new List<string>();
        foreach (var name in fileNames)
        {
            if (parseDate(name) is { } date && date < cutoff)
            {
                expired.Add(name);
            }
        }

        return expired;
    }

    /// <summary>Parses a daily snapshot name <c>coffer-YYYY-MM-DD.db</c> into its date, or <c>null</c>.</summary>
    public static DateOnly? ParseDailyDate(string fileName)
    {
        var core = Core(fileName);
        return core is not null
            && DateOnly.TryParseExact(core, DailyFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : null;
    }

    /// <summary>Parses a pre-migration name <c>coffer-YYYYMMDDTHHMMSSZ.db</c> into its UTC date, or <c>null</c>.</summary>
    public static DateOnly? ParsePreMigrationDate(string fileName)
    {
        var core = Core(fileName);
        return core is not null
            && DateTime.TryParseExact(
                core,
                "yyyyMMdd'T'HHmmss'Z'",
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var stamp)
            ? DateOnly.FromDateTime(stamp)
            : null;
    }

    private static string? Core(string fileName)
    {
        ArgumentNullException.ThrowIfNull(fileName);
        return fileName.StartsWith(Prefix, StringComparison.Ordinal)
            && fileName.EndsWith(Suffix, StringComparison.Ordinal)
            ? fileName[Prefix.Length..^Suffix.Length]
            : null;
    }
}
