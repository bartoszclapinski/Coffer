using Coffer.Core.Domain;

namespace Coffer.Core.Planning;

/// <summary>
/// Turns a set of active <see cref="RecurringFlow"/>s, an opening balance and a horizon into a dated
/// cash-flow timeline with a running balance. Pure and deterministic — the AI assistant only narrates
/// this output, it never produces a number (the Sprint-14 "engine calculates, AI explains" rule).
/// Same-day events apply outflows before inflows so a within-day dip is never hidden.
/// </summary>
public sealed class CashFlowProjectionEngine
{
    public CashFlowProjection Project(
        IReadOnlyCollection<RecurringFlow> flows,
        decimal openingBalance,
        DateOnly from,
        int horizonDays,
        decimal tightFloor = 0m)
    {
        ArgumentNullException.ThrowIfNull(flows);
        ArgumentOutOfRangeException.ThrowIfLessThan(horizonDays, 1);

        var to = from.AddDays(horizonDays - 1);

        var occurrences = flows
            .Where(flow => flow.IsActive)
            .SelectMany(
                flow => Occurrences(flow, from, to),
                (flow, date) => new
                {
                    flow.Id,
                    flow.Name,
                    Date = date,
                    flow.Direction,
                    Signed = flow.Direction == FlowDirection.Outflow ? -flow.TypicalAmount : flow.TypicalAmount,
                    Accrual = AccrualPeriod(date, flow.AccrualOffsetMonths),
                });

        // Outflows first on a shared day (Outflow > Inflow) so the conservative low point is captured.
        var ordered = occurrences
            .OrderBy(o => o.Date)
            .ThenByDescending(o => o.Direction)
            .ToList();

        var balance = openingBalance;
        var lowest = openingBalance;
        DateOnly? lowestDate = null;
        var events = new List<CashFlowEvent>(ordered.Count);

        foreach (var o in ordered)
        {
            balance += o.Signed;
            if (balance < lowest)
            {
                lowest = balance;
                lowestDate = o.Date;
            }

            events.Add(new CashFlowEvent(
                o.Id, o.Name, o.Date, o.Direction, o.Signed, o.Accrual, balance, balance <= tightFloor));
        }

        return new CashFlowProjection(from, to, openingBalance, balance, lowest, lowestDate, events);
    }

    /// <summary>Every date in [from, to] on which the flow's cadence lands.</summary>
    private static IEnumerable<DateOnly> Occurrences(RecurringFlow flow, DateOnly from, DateOnly to)
    {
        var interval = Math.Max(1, flow.IntervalMonths);
        // Phase of the cadence within the interval, taken from the anchor month (monthly = every month).
        var phase = interval == 1 ? 0 : ((flow.AnchorMonth ?? from.Month) - 1) % interval;

        var cursor = new DateOnly(from.Year, from.Month, 1);
        var end = new DateOnly(to.Year, to.Month, 1);

        while (cursor <= end)
        {
            var absoluteMonth = (cursor.Year * 12) + (cursor.Month - 1);
            if (interval == 1 || ((absoluteMonth - phase) % interval) == 0)
            {
                var day = Math.Clamp(flow.AnchorDayOfMonth, 1, DateTime.DaysInMonth(cursor.Year, cursor.Month));
                var date = new DateOnly(cursor.Year, cursor.Month, day);
                if (date >= from && date <= to)
                {
                    yield return date;
                }
            }

            cursor = cursor.AddMonths(1);
        }
    }

    /// <summary>The first day of the month a payment's cost belongs to, given the accrual offset.</summary>
    private static DateOnly AccrualPeriod(DateOnly paymentDate, int accrualOffsetMonths) =>
        new DateOnly(paymentDate.Year, paymentDate.Month, 1).AddMonths(-accrualOffsetMonths);
}
