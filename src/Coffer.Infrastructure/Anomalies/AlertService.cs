using Coffer.Core.Anomalies;
using Coffer.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Coffer.Infrastructure.Anomalies;

/// <summary>
/// Mutates alert lifecycle state. Acknowledging keeps the alert on the active list; dismissing
/// removes it and — because the unique signature is never resurrected by a re-scan — suppresses
/// the anomaly permanently. Both stamp <see cref="Core.Domain.Alert.ResolvedAt"/> in UTC.
/// </summary>
public sealed class AlertService : IAlertService
{
    private readonly IDbContextFactory<CofferDbContext> _contextFactory;

    public AlertService(IDbContextFactory<CofferDbContext> contextFactory)
    {
        ArgumentNullException.ThrowIfNull(contextFactory);
        _contextFactory = contextFactory;
    }

    public Task AcknowledgeAsync(Guid alertId, CancellationToken ct) =>
        SetStatusAsync(alertId, AlertStatus.Acknowledged, ct);

    public Task DismissAsync(Guid alertId, CancellationToken ct) =>
        SetStatusAsync(alertId, AlertStatus.Dismissed, ct);

    private async Task SetStatusAsync(Guid alertId, AlertStatus status, CancellationToken ct)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);

        var alert = await db.Alerts.FirstOrDefaultAsync(a => a.Id == alertId, ct).ConfigureAwait(false);
        if (alert is null)
        {
            return;
        }

        alert.Status = status;
        alert.ResolvedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }
}
