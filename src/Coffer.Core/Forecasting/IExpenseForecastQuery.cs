namespace Coffer.Core.Forecasting;

/// <summary>
/// Read-side next-month expense forecast. The implementation (in <c>Coffer.Infrastructure</c>) anchors on
/// the current month (like the dashboard — the latest transaction's month), targets the following
/// calendar month, assembles each category's fixed part (active recurring outflows that land in the
/// target month) and variable part (trailing-history discretionary spend, recurring charges excluded)
/// plus its current budget limit, and runs them through <see cref="ExpenseForecastEngine"/>. Optionally
/// scoped to one account.
/// </summary>
public interface IExpenseForecastQuery
{
    Task<ExpenseForecast> GetForecastAsync(Guid? accountId, CancellationToken ct);
}
