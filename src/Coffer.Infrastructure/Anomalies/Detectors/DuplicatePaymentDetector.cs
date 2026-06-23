using System.Globalization;
using Coffer.Core.Anomalies;

namespace Coffer.Infrastructure.Anomalies.Detectors;

/// <summary>
/// Flags two recent debits to the same merchant for the same amount on the same or an adjacent
/// day — a likely double-charge. Each pair is reported once (signature from the sorted id pair).
/// </summary>
public sealed class DuplicatePaymentDetector : IAnomalyDetector
{
    public IEnumerable<AnomalyCandidate> Detect(AnomalyDetectionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var debits = context.Recent
            .Where(t => t.Amount < 0 && !string.IsNullOrWhiteSpace(t.Merchant))
            .ToList();

        for (var i = 0; i < debits.Count; i++)
        {
            for (var j = i + 1; j < debits.Count; j++)
            {
                var a = debits[i];
                var b = debits[j];

                if (a.Amount != b.Amount)
                {
                    continue;
                }

                if (!string.Equals(
                        AnomalyFormatting.MerchantKey(a.Merchant!),
                        AnomalyFormatting.MerchantKey(b.Merchant!),
                        StringComparison.Ordinal))
                {
                    continue;
                }

                if (Math.Abs(a.Date.DayNumber - b.Date.DayNumber) > 1)
                {
                    continue;
                }

                var later = b.Date >= a.Date ? b : a;
                var (lo, hi) = a.Id.CompareTo(b.Id) <= 0 ? (a.Id, b.Id) : (b.Id, a.Id);
                var merchant = later.Merchant!.Trim();
                var amount = Math.Abs(a.Amount);

                yield return new AnomalyCandidate(
                    AnomalyType.DuplicatePayment,
                    (double)amount,
                    $"duplicate:{lo}:{hi}",
                    $"Możliwa podwójna płatność: {merchant}",
                    $"Dwie płatności po {AnomalyFormatting.Pln(amount)} u sprzedawcy „{merchant}” "
                        + "w tym samym lub sąsiednim dniu.",
                    later.Id,
                    amount,
                    a.Date <= b.Date ? a.Date : b.Date,
                    a.Date >= b.Date ? a.Date : b.Date,
                    new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["merchant"] = merchant,
                        ["amount"] = amount.ToString(CultureInfo.InvariantCulture),
                    });
            }
        }
    }
}
