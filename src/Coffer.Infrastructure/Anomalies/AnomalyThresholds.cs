namespace Coffer.Infrastructure.Anomalies;

/// <summary>
/// Detector tuning constants (resolved during Sprint-13 planning). The min-sample guardrail
/// keeps z-score detectors quiet on sparse categories; the recurrence floor defines "regular".
/// </summary>
internal static class AnomalyThresholds
{
    /// <summary>Minimum baseline transactions in a category before a z-score detector fires.</summary>
    public const int MinBaselineSamples = 8;

    /// <summary>z-score above which a single amount is a high-amount outlier.</summary>
    public const double HighAmountZScore = 3.0;

    /// <summary>Sigma multiple above the monthly mean that marks a category spike.</summary>
    public const double CategorySpikeSigma = 2.0;

    /// <summary>Distinct baseline months a merchant must appear in to count as recurring.</summary>
    public const int MinRecurrenceMonths = 3;
}
