namespace Coffer.Core.Anomalies;

/// <summary>
/// The inputs every <see cref="IAnomalyDetector"/> receives: the <see cref="Recent"/> window
/// (the last 30 days up to the latest transaction) compared against the <see cref="Baseline"/>
/// (the prior 6 months), plus a category-id → display-name map for templated descriptions.
/// Detectors are read-only and stateless — they project candidates, they do not persist.
/// </summary>
public sealed record AnomalyDetectionContext(
    IReadOnlyList<TransactionSnapshot> Recent,
    IReadOnlyList<TransactionSnapshot> Baseline,
    IReadOnlyDictionary<Guid, string> CategoryNames,
    DateOnly RecentFrom,
    DateOnly RecentTo);
