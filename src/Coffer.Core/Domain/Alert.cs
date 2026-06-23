using Coffer.Core.Anomalies;

namespace Coffer.Core.Domain;

/// <summary>
/// A persisted anomaly finding (doc 04). <see cref="DetectedAt"/> / <see cref="ResolvedAt"/>
/// are UTC system timestamps; <see cref="PeriodFrom"/> / <see cref="PeriodTo"/> are the
/// transaction-date window the anomaly covers (<see cref="DateOnly"/>). <see cref="Signature"/>
/// is the stable dedup key — unique across the table, so a rescan inserts each logical anomaly
/// at most once and a <see cref="AlertStatus.Dismissed"/> row is never resurrected.
/// <see cref="RelatedAmount"/> is <c>decimal</c> (hard rule #1).
/// </summary>
public class Alert
{
    public Guid Id { get; set; }

    public DateTime DetectedAt { get; set; }

    public AnomalyType Type { get; set; }

    public string Signature { get; set; } = "";

    public string Title { get; set; } = "";

    public string Description { get; set; } = "";

    public AlertStatus Status { get; set; }

    public decimal? RelatedAmount { get; set; }

    public Guid? RelatedTransactionId { get; set; }

    public DateOnly PeriodFrom { get; set; }

    public DateOnly PeriodTo { get; set; }

    public DateTime? ResolvedAt { get; set; }
}
