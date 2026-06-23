namespace Coffer.Infrastructure.Anomalies;

/// <summary>
/// Small statistical helpers shared by the detectors. Uses the sample standard deviation
/// (n-1 divisor) so a handful of baseline points does not understate spread.
/// </summary>
internal static class AnomalyStatistics
{
    /// <summary>
    /// Mean and sample standard deviation. Returns (0, 0) for an empty set and (mean, 0)
    /// when fewer than two values exist (spread is undefined, so it is reported as zero).
    /// </summary>
    public static (double Mean, double StdDev) MeanAndStdDev(IReadOnlyCollection<double> values)
    {
        ArgumentNullException.ThrowIfNull(values);

        if (values.Count == 0)
        {
            return (0d, 0d);
        }

        var mean = values.Average();
        if (values.Count < 2)
        {
            return (mean, 0d);
        }

        var sumSquares = values.Sum(v => (v - mean) * (v - mean));
        var variance = sumSquares / (values.Count - 1);
        return (mean, Math.Sqrt(variance));
    }
}
