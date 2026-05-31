using System.Text.RegularExpressions;

namespace Coffer.Infrastructure.Parsing.Polish;

/// <summary>
/// Normalises IBAN-style account numbers as printed on Polish bank statements.
/// Input examples: <c>"PL61 1090 1014 0000 0712 1981 2874"</c>, <c>"61 1090 1014 0000 0712 1981 2874"</c>
/// (PKO sometimes omits the country prefix on the statement header).
/// Output: <c>"PL61109010140000071219812874"</c> — country prefix preserved, all
/// separators removed, uppercase.
/// </summary>
public static partial class AccountNumberNormalizer
{
    public static string Normalize(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }

        var trimmed = _separators().Replace(raw, string.Empty).ToUpperInvariant();

        // If the input is 26 digits (Polish domestic IBAN body) without a country
        // prefix, prepend "PL" so downstream consumers see a consistent shape.
        if (trimmed.Length == 26 && trimmed.All(char.IsDigit))
        {
            trimmed = "PL" + trimmed;
        }

        return trimmed;
    }

    [GeneratedRegex(@"[\s\-]", RegexOptions.CultureInvariant)]
    private static partial Regex _separators();
}
