using System.Text.RegularExpressions;
using Coffer.Core.Ai;

namespace Coffer.Infrastructure.AI;

/// <summary>
/// Redacts account numbers, IBANs and NIPs from text before it is sent to an AI
/// provider (hard rule #7). Merchant names are deliberately preserved — they are the
/// categorisation signal and are not sensitive. Order matters: IBAN (with its country
/// prefix) is matched before bare account-number runs so <c>PL61…</c> becomes
/// <c>[IBAN]</c>, not <c>PL[ACCOUNT]</c>.
/// </summary>
public sealed partial class PromptAnonymizer : IPromptAnonymizer
{
    public string Anonymize(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text;
        }

        var result = IbanRegex().Replace(text, "[IBAN]");
        result = NipRegex().Replace(result, "[NIP]");
        result = AccountRegex().Replace(result, "[ACCOUNT]");
        return result;
    }

    public string Anonymize(string text, IReadOnlyList<string> ownerNames)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text;
        }

        var result = text;
        if (ownerNames is { Count: > 0 })
        {
            foreach (var name in ownerNames)
            {
                var trimmed = name?.Trim();
                if (string.IsNullOrEmpty(trimmed))
                {
                    continue;
                }

                // Whole-word, case-insensitive so "Jan Kowalski" on the header is redacted
                // without clipping unrelated substrings. Diacritics count as word characters.
                var pattern = $@"\b{Regex.Escape(trimmed)}\b";
                result = Regex.Replace(result, pattern, "[NAME]", RegexOptions.IgnoreCase);
            }
        }

        return Anonymize(result);
    }

    // Polish IBAN: "PL" + 26 digits, optionally grouped in spaces (as bank exports show it).
    [GeneratedRegex(@"\bPL[\s]?(?:\d[\s]?){26}", RegexOptions.IgnoreCase)]
    private static partial Regex IbanRegex();

    // NIP: 10 digits, commonly written 123-45-67-890 or 123-456-78-90.
    [GeneratedRegex(@"\b\d{3}-\d{2,3}-\d{2,3}-\d{2,3}\b")]
    private static partial Regex NipRegex();

    // Bare domestic account / long digit runs (20–26 digits), allowing the grouping
    // spaces PKO uses ("82 1020 5604 0000 0102 8996 3017").
    [GeneratedRegex(@"\b\d{2}(?:[\s]\d{4}){5,6}\b|\b\d{20,26}\b")]
    private static partial Regex AccountRegex();
}
