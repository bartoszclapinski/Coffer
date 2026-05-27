using System.Text.RegularExpressions;

namespace Coffer.Infrastructure.Parsing.Polish;

/// <summary>
/// Cleans up the noisy descriptions Polish bank statements emit. Drives both the
/// <c>Transaction.NormalizedDescription</c> column (for filter/search/match) and
/// the <c>TransactionHash</c> input (so re-imports of the same statement dedupe
/// past benign printer differences).
/// </summary>
public static partial class DescriptionNormalizer
{
    public static string Normalize(string raw)
    {
        if (string.IsNullOrEmpty(raw))
        {
            return string.Empty;
        }

        var s = raw;

        // 1. Strip leading transaction-code prefixes that bury the merchant.
        s = _knownPrefixes().Replace(s, string.Empty);

        // 2. Strip card-number references (`**1234`, `***1234`, `/****1234/`).
        s = _cardNumberFragments().Replace(s, string.Empty);

        // 3. Strip ISO 4217-ish country codes embedded in descriptions.
        s = _countryCodeMarkers().Replace(s, string.Empty);

        // 4. Collapse whitespace.
        s = _whitespaceRun().Replace(s, " ");

        return s.Trim().ToUpperInvariant();
    }

    [GeneratedRegex(@"^(BLIK|KRD|PŁATNOŚĆ KARTĄ|PLATNOSC KARTA|KARTA DEBETOWA|KARTA KREDYTOWA)\s+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex _knownPrefixes();

    [GeneratedRegex(@"/?\*+\d{4}/?", RegexOptions.CultureInvariant)]
    private static partial Regex _cardNumberFragments();

    [GeneratedRegex(@"/(PL|EU|US|GB|DE|FR|CZ|SK|UA)/", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex _countryCodeMarkers();

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex _whitespaceRun();
}
