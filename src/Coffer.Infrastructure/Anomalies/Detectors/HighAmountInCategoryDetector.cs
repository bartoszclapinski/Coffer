using System.Globalization;
using Coffer.Core.Anomalies;

namespace Coffer.Infrastructure.Anomalies.Detectors;

/// <summary>
/// Flags a recent debit whose magnitude is a z-score outlier against its own category's
/// baseline (mean + std of prior-6-month debit magnitudes). Stays quiet on sparse categories
/// via <see cref="AnomalyThresholds.MinBaselineSamples"/>.
/// </summary>
public sealed class HighAmountInCategoryDetector : IAnomalyDetector
{
    public IEnumerable<AnomalyCandidate> Detect(AnomalyDetectionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var baselineByCategory = context.Baseline
            .Where(t => t.Amount < 0 && t.CategoryId is not null)
            .GroupBy(t => t.CategoryId!.Value)
            .ToDictionary(
                g => g.Key,
                g => g.Select(t => (double)Math.Abs(t.Amount)).ToList());

        foreach (var tx in context.Recent.Where(t => t.Amount < 0 && t.CategoryId is not null))
        {
            if (!baselineByCategory.TryGetValue(tx.CategoryId!.Value, out var magnitudes)
                || magnitudes.Count < AnomalyThresholds.MinBaselineSamples)
            {
                continue;
            }

            var (mean, std) = AnomalyStatistics.MeanAndStdDev(magnitudes);
            if (std <= 0d)
            {
                continue;
            }

            var magnitude = (double)Math.Abs(tx.Amount);
            var z = (magnitude - mean) / std;
            if (z <= AnomalyThresholds.HighAmountZScore)
            {
                continue;
            }

            var categoryName = AnomalyFormatting.Category(context, tx.CategoryId);
            var amount = Math.Abs(tx.Amount);

            yield return new AnomalyCandidate(
                AnomalyType.HighAmountInCategory,
                z,
                $"high-amount:{tx.Id}",
                $"Wysoka kwota w kategorii „{categoryName}”",
                $"Transakcja {AnomalyFormatting.Pln(amount)} w kategorii „{categoryName}” "
                    + $"znacznie przekracza typowy wydatek (średnio {AnomalyFormatting.Pln((decimal)mean)}).",
                tx.Id,
                amount,
                tx.Date,
                tx.Date,
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["category"] = categoryName,
                    ["amount"] = amount.ToString(CultureInfo.InvariantCulture),
                    ["zScore"] = z.ToString("F2", CultureInfo.InvariantCulture),
                    ["categoryMean"] = mean.ToString("F2", CultureInfo.InvariantCulture),
                });
        }
    }
}
