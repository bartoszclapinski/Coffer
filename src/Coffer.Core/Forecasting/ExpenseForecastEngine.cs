namespace Coffer.Core.Forecasting;

/// <summary>
/// Turns assembled per-category inputs into a next-month <see cref="ExpenseForecast"/>. Pure and
/// deterministic — the query assembles the fixed/variable/limit numbers from the database, the engine
/// only combines them (<c>Total = Fixed + Variable</c>), rounds a suggested limit with a little
/// headroom, drops empty lines, and orders by size. A deliberately simple projection (flat trailing
/// averages plus known recurring charges — no seasonality), mirroring the Sprint-20 engine/query split.
/// </summary>
public sealed class ExpenseForecastEngine
{
    /// <summary>The step the suggested limit is rounded up to, in the display currency.</summary>
    public const decimal SuggestedLimitStep = 10m;

    public ExpenseForecast Forecast(DateOnly month, IReadOnlyList<CategoryForecastInput> inputs)
    {
        ArgumentNullException.ThrowIfNull(inputs);

        var monthStart = new DateOnly(month.Year, month.Month, 1);

        var categories = inputs
            .Select(i =>
            {
                var total = i.Fixed + i.Variable;
                return new CategoryForecast(
                    i.CategoryId,
                    i.CategoryName,
                    i.CategoryColor,
                    i.Fixed,
                    i.Variable,
                    total,
                    RoundUpToStep(total),
                    i.CurrentLimit);
            })
            .Where(c => c.Total > 0m)
            .OrderByDescending(c => c.Total)
            .ToList();

        var grandTotal = categories.Sum(c => c.Total);

        return new ExpenseForecast(monthStart, categories, grandTotal);
    }

    /// <summary>Rounds up to the next <see cref="SuggestedLimitStep"/> so the limit sits a little above the forecast.</summary>
    private static decimal RoundUpToStep(decimal amount) =>
        amount <= 0m ? 0m : Math.Ceiling(amount / SuggestedLimitStep) * SuggestedLimitStep;
}
