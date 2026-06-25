namespace Coffer.Core.Goals;

/// <summary>
/// Derives the deterministic <see cref="FinancialContext"/> the engine evaluates goals against from
/// the PLN transaction history — monthly income, fixed and variable spend (6-month moving average),
/// per-category averages, and seasonality. The returned context's
/// <see cref="FinancialContext.OtherActiveGoals"/> is empty; the engine fills it per goal.
/// </summary>
public interface IFinancialContextBuilder
{
    Task<FinancialContext> BuildAsync(DateOnly today, CancellationToken ct);
}
