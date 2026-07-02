namespace Coffer.Core.Budgeting;

/// <summary>
/// Read-side budget tracking. The implementation (in <c>Coffer.Infrastructure</c>) anchors on the current
/// month (like the dashboard — the latest transaction's month, so an idle current month is not empty),
/// sums each budgeted category's month-to-date debits server-side, runs them through
/// <see cref="BudgetTrackingEngine"/>, and returns the statuses plus the unbudgeted lines. Optionally
/// scoped to one account.
/// </summary>
public interface IBudgetTrackingQuery
{
    Task<BudgetOverview> GetOverviewAsync(Guid? accountId, CancellationToken ct);
}
