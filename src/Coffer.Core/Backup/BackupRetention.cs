using System.Globalization;

namespace Coffer.Core.Backup;

/// <summary>
/// Pure retention policy for backup files. Given the backup filenames, today's date, a keep-days window,
/// and a date parser, it returns the filenames that have aged out. Dates are parsed from the filename so
/// no database is needed and it works before login. Unparsable names are ignored (never deleted).
/// </summary>
public static class BackupRetention
{
    private const string Prefix = "coffer-";
    private const string Suffix = ".db";

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
            && DateOnly.TryParseExact(core, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
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
