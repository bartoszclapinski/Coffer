using Coffer.Core.Domain;

namespace Coffer.Core.Forecasting;

/// <summary>
/// Cadence helper for the forecast: whether a <see cref="RecurringFlow"/> has an occurrence in a given
/// calendar month. Mirrors the phase logic in <see cref="Planning.CashFlowProjectionEngine"/> (monthly
/// flows land every month; interval &gt; 1 lands only when the <see cref="RecurringFlow.AnchorMonth"/>
/// phase aligns), so the forecast attributes a quarterly/annual charge only to the month it falls in.
/// </summary>
public static class FlowSchedule
{
    /// <summary>True when <paramref name="flow"/>'s cadence produces an occurrence in <paramref name="month"/>.</summary>
    public static bool OccursInMonth(RecurringFlow flow, DateOnly month)
    {
        ArgumentNullException.ThrowIfNull(flow);

        var interval = Math.Max(1, flow.IntervalMonths);
        if (interval == 1)
        {
            return true;
        }

        var phase = ((flow.AnchorMonth ?? month.Month) - 1) % interval;
        var absoluteMonth = (month.Year * 12) + (month.Month - 1);
        return ((absoluteMonth - phase) % interval) == 0;
    }
}
