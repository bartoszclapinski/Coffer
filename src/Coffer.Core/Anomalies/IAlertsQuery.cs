namespace Coffer.Core.Anomalies;

/// <summary>Read side of the alerts feature: the active (undismissed, unacknowledged) list.</summary>
public interface IAlertsQuery
{
    Task<IReadOnlyList<AlertListItem>> GetActiveAsync(CancellationToken ct);
}
