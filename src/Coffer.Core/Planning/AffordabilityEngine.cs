using Coffer.Core.Domain;

namespace Coffer.Core.Planning;

/// <summary>
/// Answers "can I spend this amount today?" deterministically. It seeds from the real opening balance
/// (18-A anchored per-account balance), applies the proposed spend as a same-day outflow, projects the
/// known recurring flows forward with the same <see cref="CashFlowProjectionEngine"/> the planner uses,
/// overlays a flat daily variable burn for ordinary discretionary spending, and finds the lowest the
/// balance reaches before the next inflow (the relief point — typically the next salary). If that low
/// stays at or above the owner's safety floor the spend is affordable. Pure and deterministic: it makes
/// no AI call, and the assistant only narrates the returned <see cref="AffordabilityVerdict"/> (the
/// Sprint-14 "engine calculates, AI explains" rule).
///
/// <para>Same-day ordering is conservative — the proposed spend and any recurring outflows apply before
/// inflows, and the daily burn is charged up to each event before the event lands, so a within-window
/// dip is never hidden by a later credit.</para>
/// </summary>
public sealed class AffordabilityEngine
{
    /// <summary>
    /// How far to look for the relief inflow. A monthly salary always lands inside a month, so ~3 months
    /// is ample; if no inflow appears in the window the burn is projected to the horizon end instead, so
    /// the answer stays conservative rather than optimistic.
    /// </summary>
    public const int DefaultMaxHorizonDays = 92;

    private readonly CashFlowProjectionEngine _projectionEngine;

    public AffordabilityEngine(CashFlowProjectionEngine projectionEngine)
    {
        ArgumentNullException.ThrowIfNull(projectionEngine);
        _projectionEngine = projectionEngine;
    }

    public AffordabilityVerdict Assess(
        decimal spendAmount,
        DateOnly spendDate,
        decimal openingBalance,
        IReadOnlyCollection<RecurringFlow> flows,
        decimal dailyBurn,
        decimal safetyFloor,
        BalanceTrust trust,
        bool balanceIsRelative,
        int maxHorizonDays = DefaultMaxHorizonDays)
    {
        ArgumentNullException.ThrowIfNull(flows);
        ArgumentNullException.ThrowIfNull(trust);
        ArgumentOutOfRangeException.ThrowIfNegative(spendAmount);
        ArgumentOutOfRangeException.ThrowIfNegative(dailyBurn);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxHorizonDays, 1);

        var horizonEnd = spendDate.AddDays(maxHorizonDays - 1);

        // Reuse the projection engine only to expand the flows into dated, signed events over the window;
        // its running balance is discarded because we re-walk with the proposed spend and daily burn.
        var timeline = _projectionEngine.Project(flows, openingBalance, spendDate, maxHorizonDays).Events;

        // The proposed spend is a same-day outflow on the spend date — apply it before anything else.
        var balance = openingBalance - spendAmount;
        var lowest = balance;
        var lowestDate = spendDate;
        AffordabilityDriver? driver = null;
        var cursor = spendDate;
        DateOnly? nextInflowDate = null;

        foreach (var e in timeline)
        {
            if (e.Date < spendDate)
            {
                continue;
            }

            // Charge ordinary burn for the calendar days between the previous point and this event, and
            // capture any pre-event dip (e.g. the balance sagging just before the next salary lands).
            var days = e.Date.DayNumber - cursor.DayNumber;
            if (days > 0 && dailyBurn > 0m)
            {
                balance -= dailyBurn * days;
                if (balance < lowest)
                {
                    lowest = balance;
                    lowestDate = e.Date;
                    driver = null;
                }
            }

            cursor = e.Date;

            if (e.Direction == FlowDirection.Inflow)
            {
                // First inflow after the spend is the relief point — the window ends here.
                nextInflowDate = e.Date;
                break;
            }

            balance += e.Amount; // signed: outflow is negative
            if (balance < lowest)
            {
                lowest = balance;
                lowestDate = e.Date;
                driver = new AffordabilityDriver(e.FlowId, e.Name, e.Date, e.Amount);
            }
        }

        // No relief inflow in the window: keep burning to the horizon end so we do not report a falsely
        // rosy low point that ignores spending after the last modelled event.
        if (nextInflowDate is null && dailyBurn > 0m)
        {
            var days = horizonEnd.DayNumber - cursor.DayNumber;
            if (days > 0)
            {
                balance -= dailyBurn * days;
                if (balance < lowest)
                {
                    lowest = balance;
                    lowestDate = horizonEnd;
                    driver = null;
                }
            }
        }

        var headroom = lowest - safetyFloor;

        return new AffordabilityVerdict(
            CanAfford: lowest >= safetyFloor,
            SpendAmount: spendAmount,
            SpendDate: spendDate,
            OpeningBalance: openingBalance,
            LowestBalance: lowest,
            LowestBalanceDate: lowestDate,
            SafetyFloor: safetyFloor,
            Headroom: headroom,
            DailyBurn: dailyBurn,
            NextInflowDate: nextInflowDate,
            Driver: driver,
            IsUncertain: !trust.IsTrustworthy,
            UncertaintyGap: trust.Gaps.Count > 0 ? trust.Gaps[0] : null,
            IsRelative: balanceIsRelative);
    }
}
