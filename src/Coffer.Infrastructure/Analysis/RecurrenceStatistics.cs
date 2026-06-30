namespace Coffer.Infrastructure.Analysis;

/// <summary>
/// Shared recurrence maths over a set of transaction dates, used by both the anomaly
/// <c>MissingRecurrenceDetector</c> and the planning <c>RecurringFlowDetector</c> so "how regular is
/// this merchant" lives in one place.
/// </summary>
internal static class RecurrenceStatistics
{
    /// <summary>Number of distinct calendar months the dates span.</summary>
    public static int DistinctMonths(IEnumerable<DateOnly> dates)
    {
        ArgumentNullException.ThrowIfNull(dates);
        return dates.Select(d => (d.Year, d.Month)).Distinct().Count();
    }

    /// <summary>The median day-of-month across the dates — the flow's expected anchor day.</summary>
    public static int MedianDayOfMonth(IEnumerable<DateOnly> dates)
    {
        ArgumentNullException.ThrowIfNull(dates);

        var days = dates.Select(d => d.Day).OrderBy(d => d).ToList();
        if (days.Count == 0)
        {
            return 1;
        }

        var mid = days.Count / 2;
        return days.Count % 2 == 1
            ? days[mid]
            : (int)Math.Round((days[mid - 1] + days[mid]) / 2.0, MidpointRounding.AwayFromZero);
    }
}
