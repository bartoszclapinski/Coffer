using System.Globalization;
using Coffer.Core.Anomalies;
using Coffer.Core.Budgeting;

namespace Coffer.Infrastructure.Anomalies.Detectors;

/// <summary>
/// Flags every category whose current-month spend has crossed its <see cref="Core.Domain.CategoryBudget"/>
/// limit (<see cref="BudgetZone.Over"/>). Unlike the statistical detectors this one reads the assembled
/// <see cref="AnomalyDetectionContext.Budgets"/> overview (the detection service runs
/// <see cref="IBudgetTrackingQuery"/> once and hands it in), so the detector itself stays pure. It fires
/// only on the crossing, not on the "approaching" warning zone — that stays a coloured bar on the Budgets
/// page rather than a mid-month alert. One candidate per (category, month); the signature keeps it
/// idempotent across re-scans and a dismissed month is never re-raised.
/// </summary>
public sealed class OverBudgetDetector : IAnomalyDetector
{
    public IEnumerable<AnomalyCandidate> Detect(AnomalyDetectionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.Budgets is not { } overview)
        {
            yield break;
        }

        var month = overview.Month;
        var periodFrom = month;
        var periodTo = month.AddMonths(1).AddDays(-1);

        foreach (var line in overview.Budgets)
        {
            if (line.Status.Zone != BudgetZone.Over)
            {
                continue;
            }

            var limit = line.Status.Limit;
            var spent = line.Status.Spent;
            var overspend = spent - limit;
            var category = line.CategoryName;

            yield return new AnomalyCandidate(
                AnomalyType.OverBudget,
                (double)overspend,
                $"over-budget:{line.CategoryId}:{month:yyyyMM}",
                $"Przekroczony budżet: „{category}”",
                $"Wydatki w kategorii „{category}” w tym miesiącu ({AnomalyFormatting.Pln(spent)}) "
                    + $"przekroczyły limit {AnomalyFormatting.Pln(limit)} o {AnomalyFormatting.Pln(overspend)}.",
                null,
                spent,
                periodFrom,
                periodTo,
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["category"] = category,
                    ["limit"] = limit.ToString(CultureInfo.InvariantCulture),
                    ["spent"] = spent.ToString(CultureInfo.InvariantCulture),
                    ["overspend"] = overspend.ToString(CultureInfo.InvariantCulture),
                });
        }
    }
}
