using System.Text.RegularExpressions;
using Coffer.Infrastructure.Parsing.Polish;
using UglyToad.PdfPig.Content;

namespace Coffer.Infrastructure.Parsing.Pko;

/// <summary>
/// Reads the statement-level header: account number, currency, period dates.
/// PKO's checking statements print these in the first ~150 Y units of page 1;
/// finding them is done by regex on the page text rather than by Letter
/// positions because the header layout varies a lot more than the transaction
/// table layout does.
/// </summary>
internal static partial class PkoStandardCheckingHeader
{
    internal sealed record HeaderInfo(
        string AccountNumber,
        string Currency,
        DateOnly PeriodFrom,
        DateOnly PeriodTo);

    public static HeaderInfo Extract(Page firstPage)
    {
        var text = firstPage.Text;

        var account = ExtractAccountNumber(text);
        var currency = ExtractCurrency(text);
        var (from, to) = ExtractPeriod(text);

        return new HeaderInfo(account, currency, from, to);
    }

    private static string ExtractAccountNumber(string text)
    {
        // PKO prints the IBAN as "Numer rachunku: PL61 1090 1014 ..." or just the
        // 26-digit body in newer statements. Try both shapes.
        var match = _ibanWithCountry().Match(text);
        if (match.Success)
        {
            return AccountNumberNormalizer.Normalize(match.Value);
        }
        match = _ibanDigitsOnly().Match(text);
        if (match.Success)
        {
            return AccountNumberNormalizer.Normalize(match.Value);
        }
        return string.Empty;
    }

    private static string ExtractCurrency(string text)
    {
        var match = _currencyLine().Match(text);
        if (match.Success)
        {
            return match.Groups[1].Value.Trim();
        }
        // PKO checking is overwhelmingly PLN; fall through to that default when
        // the header doesn't print an explicit currency line.
        return "PLN";
    }

    private static (DateOnly From, DateOnly To) ExtractPeriod(string text)
    {
        var match = _periodLine().Match(text);
        if (!match.Success)
        {
            // Sprint-7 fallback — caller can flag a warning when From == To == default.
            return (default, default);
        }
        return (
            PolishDateParser.Parse(match.Groups[1].Value),
            PolishDateParser.Parse(match.Groups[2].Value));
    }

    [GeneratedRegex(@"PL\s?\d{2}(\s?\d{4}){6}", RegexOptions.CultureInvariant)]
    private static partial Regex _ibanWithCountry();

    [GeneratedRegex(@"\d{2}(\s\d{4}){6}", RegexOptions.CultureInvariant)]
    private static partial Regex _ibanDigitsOnly();

    [GeneratedRegex(@"Waluta\s*[:\s]\s*([A-Z]{3})", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex _currencyLine();

    [GeneratedRegex(@"od\s+(\d{2}[.\-]\d{2}[.\-]\d{4})\s+do\s+(\d{2}[.\-]\d{2}[.\-]\d{4})", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex _periodLine();
}
