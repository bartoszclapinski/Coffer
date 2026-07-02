namespace Coffer.Core.Planning;

/// <summary>
/// Estimates the average daily discretionary spend for an account — the ordinary day-to-day burn the
/// <see cref="AffordabilityEngine"/> overlays between now and the next inflow so the answer is
/// conservative rather than optimistic. It is a rough deterministic average over recent history, not a
/// forecast: transactions already modelled as active <see cref="Domain.RecurringFlow"/>s (matched by
/// merchant or category) are excluded so recurring outflows are never counted twice. Returns <c>0</c>
/// when there is no qualifying history.
/// </summary>
public interface IVariableBurnQuery
{
    /// <summary>
    /// Average daily discretionary outflow (a positive magnitude in PLN) over the trailing window ending
    /// at <paramref name="asOf"/>, scoped to <paramref name="accountId"/> when given, else across all
    /// accounts.
    /// </summary>
    Task<decimal> GetDailyBurnAsync(Guid? accountId, DateOnly asOf, CancellationToken ct);
}
