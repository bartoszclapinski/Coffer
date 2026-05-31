using System.Globalization;

namespace Coffer.Infrastructure.Parsing.Polish;

/// <summary>
/// Parses Polish-format dates as they appear on bank statements. Supports
/// <c>dd.MM.yyyy</c>, <c>dd-MM-yyyy</c>, and the ISO <c>yyyy-MM-dd</c> form
/// (some PKO statements use ISO in machine-generated sections).
/// </summary>
public static class PolishDateParser
{
    private static readonly string[] _formats =
    {
        "dd.MM.yyyy",
        "dd-MM-yyyy",
        "yyyy-MM-dd",
    };

    public static DateOnly Parse(string raw)
    {
        if (!TryParse(raw, out var value))
        {
            throw new FormatException($"Cannot parse Polish date: '{raw}'.");
        }
        return value;
    }

    public static bool TryParse(string raw, out DateOnly value)
    {
        value = default;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        return DateOnly.TryParseExact(
            raw.Trim(),
            _formats,
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out value);
    }
}
