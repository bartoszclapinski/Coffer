namespace Coffer.Core.Anomalies;

/// <summary>
/// The statistical detectors that can raise an <see cref="Domain.Alert"/> (doc 04).
/// Persisted as the enum name so the column stays readable and forward-compatible.
/// </summary>
public enum AnomalyType
{
    /// <summary>A debit whose amount is a z-score &gt; 3 outlier within its category.</summary>
    HighAmountInCategory,

    /// <summary>A merchant that never appeared in the baseline window.</summary>
    NewMerchant,

    /// <summary>A category whose recent spend is &gt; 2σ above its 6-month monthly average.</summary>
    CategorySpike,

    /// <summary>The same merchant and amount charged on the same or an adjacent day.</summary>
    DuplicatePayment,

    /// <summary>A regularly-recurring merchant that did not appear in the recent window.</summary>
    MissingRecurrence,

    /// <summary>A category whose month-to-date spend has crossed its <see cref="Domain.CategoryBudget"/> limit.</summary>
    OverBudget,
}
