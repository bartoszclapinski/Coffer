namespace Coffer.Core.Budgeting;

/// <summary>
/// Deterministic mid-month budget tracking (the Sprint-14/16/18 "engine calculates" rule). Given a
/// category's monthly <c>limit</c>, its <c>spendToDate</c>, and where the month is (<c>daysElapsed</c> of
/// <c>daysInMonth</c>), it returns a <see cref="BudgetStatus"/>: remaining, the fraction spent, a single
/// linear end-of-month projection (<c>spendToDate / daysElapsed * daysInMonth</c>), and an ok/warning/over
/// zone. No ML, no seasonality — a conservative, explainable estimate. Pure and free.
/// </summary>
public sealed class BudgetTrackingEngine
{
    /// <summary>Spend fraction at which a budget is flagged as approaching its limit.</summary>
    public const decimal WarningFraction = 0.80m;

    public BudgetStatus Evaluate(decimal limit, decimal spendToDate, int daysElapsed, int daysInMonth)
    {
        // A budget always has a positive limit; a zero/negative limit means "no meaningful budget".
        var effectiveLimit = Math.Max(0m, limit);
        var spent = Math.Max(0m, spendToDate);

        // Clamp the calendar so the projection never divides by zero or shrinks below elapsed.
        var elapsed = Math.Max(1, daysElapsed);
        var totalDays = Math.Max(elapsed, Math.Max(1, daysInMonth));

        var remaining = effectiveLimit - spent;
        var fraction = effectiveLimit > 0m ? spent / effectiveLimit : 0m;
        var projected = Math.Round(spent / elapsed * totalDays, 2, MidpointRounding.AwayFromZero);

        var zone = effectiveLimit > 0m && spent >= effectiveLimit
            ? BudgetZone.Over
            : effectiveLimit > 0m && (fraction >= WarningFraction || projected >= effectiveLimit)
                ? BudgetZone.Warning
                : BudgetZone.Ok;

        return new BudgetStatus(effectiveLimit, spent, remaining, fraction, projected, zone);
    }
}
