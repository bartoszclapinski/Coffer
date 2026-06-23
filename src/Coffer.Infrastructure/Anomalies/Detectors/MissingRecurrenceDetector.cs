using System.Globalization;
using Coffer.Core.Anomalies;

namespace Coffer.Infrastructure.Anomalies.Detectors;

/// <summary>
/// Flags a merchant that recurred across the baseline (appeared in at least
/// <see cref="AnomalyThresholds.MinRecurrenceMonths"/> distinct months) but is absent from the
/// recent window — a possibly missed subscription or bill. Skipped without a baseline.
/// </summary>
public sealed class MissingRecurrenceDetector : IAnomalyDetector
{
    public IEnumerable<AnomalyCandidate> Detect(AnomalyDetectionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.Baseline.Count == 0)
        {
            yield break;
        }

        var recentMerchants = context.Recent
            .Where(t => !string.IsNullOrWhiteSpace(t.Merchant))
            .Select(t => AnomalyFormatting.MerchantKey(t.Merchant!))
            .ToHashSet(StringComparer.Ordinal);

        var baselineByMerchant = context.Baseline
            .Where(t => t.Amount < 0 && !string.IsNullOrWhiteSpace(t.Merchant))
            .GroupBy(t => AnomalyFormatting.MerchantKey(t.Merchant!));

        foreach (var group in baselineByMerchant)
        {
            var key = group.Key;
            if (recentMerchants.Contains(key))
            {
                continue;
            }

            var items = group.ToList();
            var distinctMonths = items
                .Select(t => (t.Date.Year, t.Date.Month))
                .Distinct()
                .Count();

            if (distinctMonths < AnomalyThresholds.MinRecurrenceMonths)
            {
                continue;
            }

            var merchant = items[^1].Merchant!.Trim();
            var avgMagnitude = items.Average(t => Math.Abs(t.Amount));

            yield return new AnomalyCandidate(
                AnomalyType.MissingRecurrence,
                distinctMonths,
                $"missing-recurrence:{key}:{context.RecentFrom:yyyyMMdd}",
                $"Brak cyklicznej płatności: {merchant}",
                $"Regularna płatność u sprzedawcy „{merchant}” (średnio {AnomalyFormatting.Pln(avgMagnitude)}) "
                    + "nie pojawiła się w ostatnim okresie.",
                null,
                avgMagnitude,
                context.RecentFrom,
                context.RecentTo,
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["merchant"] = merchant,
                    ["avgAmount"] = avgMagnitude.ToString(CultureInfo.InvariantCulture),
                    ["months"] = distinctMonths.ToString(CultureInfo.InvariantCulture),
                });
        }
    }
}
