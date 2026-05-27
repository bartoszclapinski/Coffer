using System.Globalization;
using System.Text.RegularExpressions;

namespace Coffer.Infrastructure.Parsing.Polish;

/// <summary>
/// Parses Polish-format decimal amounts as they appear on bank statements.
/// Examples: <c>"1 234,56 zł"</c>, <c>"1 234,56"</c>, <c>"-89,90"</c>,
/// <c>"89,90-"</c> (trailing minus, seen on some statements), <c>"12,00 PLN"</c>.
/// Returns <see cref="decimal"/> per hard rule #1 — money is never floating-point.
/// </summary>
public static partial class PolishAmountParser
{
    public static decimal Parse(string raw)
    {
        if (!TryParse(raw, out var value))
        {
            throw new FormatException($"Cannot parse Polish amount: '{raw}'.");
        }
        return value;
    }

    public static bool TryParse(string raw, out decimal value)
    {
        value = 0m;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        var trimmed = raw.Trim();

        // Trailing minus convention seen on some Polish statements: "89,90-" means -89.90.
        var trailingMinus = false;
        if (trimmed.EndsWith('-'))
        {
            trailingMinus = true;
            trimmed = trimmed[..^1].TrimEnd();
        }

        var stripped = _currencyOrSpace().Replace(trimmed, string.Empty);
        var normalized = stripped.Replace(',', '.');
        if (trailingMinus && !normalized.StartsWith('-'))
        {
            normalized = "-" + normalized;
        }

        return decimal.TryParse(normalized, NumberStyles.Number, CultureInfo.InvariantCulture, out value);
    }

    /// <summary>
    /// Strips NBSP, regular spaces, narrow no-break space, and the currency hints
    /// (<c>zł</c>, <c>PLN</c>, with or without surrounding whitespace). Anything
    /// else is left for <see cref="decimal.TryParse"/> to reject.
    /// </summary>
    [GeneratedRegex(@"[  \s]|z[łl]|PLN", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex _currencyOrSpace();
}
