using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Coffer.Infrastructure.AI;

/// <summary>
/// Diacritic-insensitive matching helpers for Polish names and labels. Bank exports are
/// inconsistent: the same person can appear as <c>Łukasz Źdźbło</c> in one place and as
/// <c>LUKASZ ZDZBLO</c> in an ASCII-only export, so redaction has to treat the two spellings
/// as the same name.
/// </summary>
public static class PolishText
{
    private static readonly Dictionary<char, string> _diacriticVariants = new()
    {
        ['a'] = "ą",
        ['c'] = "ć",
        ['e'] = "ę",
        ['l'] = "ł",
        ['n'] = "ń",
        ['o'] = "ó",
        ['s'] = "ś",
        ['z'] = "źż",
    };

    /// <summary>
    /// Strips diacritics (including <c>ł</c>, which Unicode does not decompose), collapses
    /// whitespace runs to one space and trims. Case is preserved.
    /// </summary>
    public static string Fold(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var decomposed = text.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        var pendingSpace = false;
        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsWhiteSpace(c))
            {
                pendingSpace = sb.Length > 0;
                continue;
            }

            if (pendingSpace)
            {
                sb.Append(' ');
                pendingSpace = false;
            }

            sb.Append(c switch
            {
                'ł' => 'l',
                'Ł' => 'L',
                _ => c,
            });
        }

        return sb.ToString();
    }

    /// <summary>
    /// A canonical comparison key: folded and upper-cased, so <c>Łukasz  Źdźbło</c> and
    /// <c>LUKASZ ZDZBLO</c> produce the same key.
    /// </summary>
    public static string Key(string text) => Fold(text).ToUpperInvariant();

    /// <summary>
    /// Builds a regex fragment that matches <paramref name="text"/> with or without Polish diacritics
    /// on each letter and with any whitespace run between words. Combine with
    /// <see cref="RegexOptions.IgnoreCase"/> for case-insensitive matching.
    /// </summary>
    public static string DiacriticInsensitivePattern(string text)
    {
        var folded = Fold(text);
        var sb = new StringBuilder(folded.Length * 4);
        foreach (var c in folded)
        {
            if (c == ' ')
            {
                sb.Append(@"\s+");
                continue;
            }

            var lower = char.ToLowerInvariant(c);
            if (_diacriticVariants.TryGetValue(lower, out var variants))
            {
                sb.Append('[').Append(lower).Append(variants).Append(']');
            }
            else
            {
                sb.Append(Regex.Escape(c.ToString()));
            }
        }

        return sb.ToString();
    }
}
