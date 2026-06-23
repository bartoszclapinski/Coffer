using Coffer.Core.Anomalies;
using Coffer.Core.Domain;
using Coffer.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Coffer.Infrastructure.Anomalies;

/// <summary>
/// Read-side query for the active alert list: everything not <see cref="AlertStatus.Dismissed"/>,
/// newest first. Runs untracked and projects straight into <see cref="AlertListItem"/>.
/// </summary>
public sealed class AlertsQuery : IAlertsQuery
{
    private readonly IDbContextFactory<CofferDbContext> _contextFactory;

    public AlertsQuery(IDbContextFactory<CofferDbContext> contextFactory)
    {
        ArgumentNullException.ThrowIfNull(contextFactory);
        _contextFactory = contextFactory;
    }

    public async Task<IReadOnlyList<AlertListItem>> GetActiveAsync(CancellationToken ct)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);

        return await db.Alerts.AsNoTracking()
            .Where(a => a.Status != AlertStatus.Dismissed)
            .OrderByDescending(a => a.DetectedAt)
            .Select(a => new AlertListItem(
                a.Id,
                a.Type,
                a.Title,
                a.Description,
                a.RelatedAmount,
                a.PeriodFrom,
                a.PeriodTo,
                a.DetectedAt))
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }
}
