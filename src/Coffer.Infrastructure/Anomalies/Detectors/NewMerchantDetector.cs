using System.Globalization;
using Coffer.Core.Anomalies;

namespace Coffer.Infrastructure.Anomalies.Detectors;

/// <summary>
/// Flags a debit to a merchant never seen across the entire baseline window. Raised at most
/// once per merchant per run. Skipped when there is no baseline at all (a fresh import would
/// otherwise mark every merchant as "new").
/// </summary>
public sealed class NewMerchantDetector : IAnomalyDetector
{
    public IEnumerable<AnomalyCandidate> Detect(AnomalyDetectionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.Baseline.Count == 0)
        {
            yield break;
        }

        var seen = context.Baseline
            .Where(t => !string.IsNullOrWhiteSpace(t.Merchant))
            .Select(t => AnomalyFormatting.MerchantKey(t.Merchant!))
            .ToHashSet(StringComparer.Ordinal);

        var raised = new HashSet<string>(StringComparer.Ordinal);

        foreach (var tx in context.Recent.Where(t => t.Amount < 0 && !string.IsNullOrWhiteSpace(t.Merchant)))
        {
            var key = AnomalyFormatting.MerchantKey(tx.Merchant!);
            if (seen.Contains(key) || !raised.Add(key))
            {
                continue;
            }

            var merchant = tx.Merchant!.Trim();
            var amount = Math.Abs(tx.Amount);

            yield return new AnomalyCandidate(
                AnomalyType.NewMerchant,
                (double)amount,
                $"new-merchant:{key}",
                $"Nowy sprzedawca: {merchant}",
                $"Pierwsza płatność u sprzedawcy „{merchant}” ({AnomalyFormatting.Pln(amount)}) "
                    + "w analizowanym okresie.",
                tx.Id,
                amount,
                tx.Date,
                tx.Date,
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["merchant"] = merchant,
                    ["amount"] = amount.ToString(CultureInfo.InvariantCulture),
                });
        }
    }
}
