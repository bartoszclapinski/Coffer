using System.Text.RegularExpressions;

namespace Coffer.Infrastructure.AI;

/// <summary>
/// Street-address and postal-code patterns for free text (Sprint 30). Transfer titles often carry
/// an address that is not under an <c>Adres …</c> label: <c>CZYNSZ UL. KWIATOWA 5/3 87-100 TORUN</c>,
/// <c>oplata za lokal al. Jana Pawla II 12 m. 4</c>. A street needs a prefix (<c>ul.</c>, <c>al.</c>,
/// <c>os.</c>, <c>pl.</c> or the full word), a name and a house number, so a bare street name or a
/// merchant such as <c>BIEDRONKA 1234</c> is not an address. The city after a postal code is kept:
/// on its own it is not personal and it is categorisation signal.
/// </summary>
public static partial class PolishAddress
{
    /// <summary>Finds street addresses (with an optional trailing postal code) and lone postal codes.</summary>
    public static Regex Pattern => AddressRegex();

    /// <summary>Index of the first street address or postal code in <paramref name="text"/>, or -1.</summary>
    public static int IndexOfFirst(string text)
    {
        var match = AddressRegex().Match(text);
        return match.Success ? match.Index : -1;
    }

    // Street: prefix, optional leading number word ("3 Maja", "1000-lecia"), one to five name words,
    // house number with optional "/flat" and optional "m. n" / "lok. n", then an optional postal code.
    // Postal code: "87-100", never a part of a longer digit/hyphen run (a NIP "123-45-67-890" or an
    // ISO date "2026-01-15" does not match).
    [GeneratedRegex(
        @"(?<![\p{L}\p{N}])(?:ulica|ul|aleja|aleje|al|osiedle|os|plac|pl)\.?\s+"
        + @"(?:\d{1,4}(?:-\p{L}+)?\s+)?"
        + @"(?:\p{L}[\p{L}'.\-]*\s+){1,5}"
        + @"\d{1,4}\p{L}?(?:\s*/\s*\d{1,4}\p{L}?)?"
        + @"(?:\s+(?:m|lok)\.?\s*\d{1,4}\p{L}?)?"
        + @"(?![\p{L}\p{N}])"
        + @"(?:[\s,]+(?<![\d-])\d{2}-\d{3}(?![\d-]))?"
        + @"|(?<![\d-])\d{2}-\d{3}(?![\d-])",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex AddressRegex();
}
