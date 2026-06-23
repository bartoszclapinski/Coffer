namespace Coffer.Core.Anomalies;

/// <summary>Read-side projection of an <see cref="Domain.Alert"/> for the Alerty list.</summary>
public sealed record AlertListItem(
    Guid Id,
    AnomalyType Type,
    string Title,
    string Description,
    decimal? RelatedAmount,
    DateOnly PeriodFrom,
    DateOnly PeriodTo,
    DateTime DetectedAt);
