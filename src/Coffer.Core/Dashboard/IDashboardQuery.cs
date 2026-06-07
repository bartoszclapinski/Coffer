namespace Coffer.Core.Dashboard;

/// <summary>
/// Read-side query for the dashboard overview. The implementation lives in
/// <c>Coffer.Infrastructure</c> (it aggregates over the EF context, server-side);
/// <c>Coffer.Application</c> view models depend on this abstraction. A single call
/// returns the whole <see cref="DashboardSnapshot"/> so the page renders from one
/// short-lived context.
/// </summary>
public interface IDashboardQuery
{
    Task<DashboardSnapshot> GetSnapshotAsync(DashboardFilter filter, CancellationToken ct);
}
