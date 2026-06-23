namespace Coffer.Core.Anomalies;

/// <summary>
/// Command side of the alerts feature. Both transitions stamp <c>ResolvedAt</c> and move the
/// alert out of the active list; a dismissed alert's signature stays on record so detection
/// never re-raises it.
/// </summary>
public interface IAlertService
{
    Task AcknowledgeAsync(Guid alertId, CancellationToken ct);

    Task DismissAsync(Guid alertId, CancellationToken ct);
}
