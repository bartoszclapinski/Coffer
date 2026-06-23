namespace Coffer.Core.Anomalies;

/// <summary>
/// A detector's finding before it becomes a persisted <see cref="Domain.Alert"/>.
/// <see cref="Signature"/> is the stable dedup key (a re-run produces the same signature
/// for the same logical anomaly, so it is inserted once). <see cref="Title"/> and
/// <see cref="Description"/> are the deterministic Polish templated text used in 13-A; the
/// 13-B commentator may replace them for the top candidates, reading <see cref="Context"/>
/// for the raw numbers. <see cref="Score"/> ranks candidates across a run.
/// </summary>
public sealed record AnomalyCandidate(
    AnomalyType Type,
    double Score,
    string Signature,
    string Title,
    string Description,
    Guid? RelatedTransactionId,
    decimal? RelatedAmount,
    DateOnly PeriodFrom,
    DateOnly PeriodTo,
    IReadOnlyDictionary<string, string> Context);
