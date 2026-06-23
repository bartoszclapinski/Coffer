using System.Globalization;
using Coffer.Core.Anomalies;

namespace Coffer.Infrastructure.Anomalies.Detectors;

/// <summary>
/// Flags a category whose recent-window total exceeds its baseline monthly mean by more than
/// <see cref="AnomalyThresholds.CategorySpikeSigma"/> standard deviations. Needs enough baseline
/// samples and at least <see cref="AnomalyThresholds.MinRecurrenceMonths"/> distinct months so a
/// single heavy month does not define "normal".
/// </summary>
public sealed class CategorySpikeDetector : IAnomalyDetector
{
    public IEnumerable<AnomalyCandidate> Detect(AnomalyDetectionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var recentTotals = context.Recent
            .Where(t => t.Amount < 0 && t.CategoryId is not null)
            .GroupBy(t => t.CategoryId!.Value)
            .ToDictionary(g => g.Key, g => g.Sum(t => Math.Abs(t.Amount)));

        var baselineByCategory = context.Baseline
            .Where(t => t.Amount < 0 && t.CategoryId is not null)
            .GroupBy(t => t.CategoryId!.Value);

        foreach (var group in baselineByCategory)
        {
            var categoryId = group.Key;
            if (!recentTotals.TryGetValue(categoryId, out var recentTotal))
            {
                continue;
            }

            var items = group.ToList();
            if (items.Count < AnomalyThresholds.MinBaselineSamples)
            {
                continue;
            }

            var monthlyTotals = items
                .GroupBy(t => (t.Date.Year, t.Date.Month))
                .Select(m => (double)m.Sum(t => Math.Abs(t.Amount)))
                .ToList();

            if (monthlyTotals.Count < AnomalyThresholds.MinRecurrenceMonths)
            {
                continue;
            }

            var (mean, std) = AnomalyStatistics.MeanAndStdDev(monthlyTotals);
            if (std <= 0d)
            {
                continue;
            }

            var threshold = mean + (AnomalyThresholds.CategorySpikeSigma * std);
            if ((double)recentTotal <= threshold)
            {
                continue;
            }

            var sigma = ((double)recentTotal - mean) / std;
            var categoryName = AnomalyFormatting.Category(context, categoryId);

            yield return new AnomalyCandidate(
                AnomalyType.CategorySpike,
                sigma,
                $"category-spike:{categoryId}:{context.RecentFrom:yyyyMMdd}",
                $"Skok wydatków w kategorii „{categoryName}”",
                $"Wydatki w kategorii „{categoryName}” w ostatnim okresie ({AnomalyFormatting.Pln(recentTotal)}) "
                    + $"są wyraźnie wyższe niż zwykle (średnio {AnomalyFormatting.Pln((decimal)mean)} miesięcznie).",
                null,
                recentTotal,
                context.RecentFrom,
                context.RecentTo,
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["category"] = categoryName,
                    ["recentTotal"] = recentTotal.ToString(CultureInfo.InvariantCulture),
                    ["monthlyMean"] = mean.ToString("F2", CultureInfo.InvariantCulture),
                    ["sigma"] = sigma.ToString("F2", CultureInfo.InvariantCulture),
                });
        }
    }
}
