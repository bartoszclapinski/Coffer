using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Coffer.Core.Ai;

namespace Coffer.Infrastructure.AI;

/// <summary>
/// The redaction rules behind <see cref="IPromptRedactionSession"/> (hard rule #7). Pure and
/// synchronous; <see cref="PromptAnonymizer"/> builds one per AI request with the owner's identity
/// names and the known counterparty names loaded. Order of operations in <see cref="Redact"/>:
/// <list type="number">
/// <item>Set GUIDs aside, so the digit patterns below cannot corrupt an id the model has to echo
/// back (a goal id ending in <c>-a12345678901</c> looks like a PESEL).</item>
/// <item>Learn person counterparty names from the labelled name sub-fields of the text, so the same
/// name is also caught where it appears without its label (e.g. repeated in a title).</item>
/// <item>Replace the value of every personal labelled sub-field: a person's name becomes a pseudonym
/// (a legal entity's name stays), addresses / phones / identifiers / accounts / card numbers become
/// fixed placeholders. The label stays, so the model still sees the structure. Outside the card
/// location, street addresses and postal codes in free text become <c>[ADDRESS]</c>.</item>
/// <item>Pattern redactions: IBAN before bare account runs (so <c>PL61…</c> is <c>[IBAN]</c>, not
/// <c>PL[ACCOUNT]</c>), NIP, phone numbers before PESEL (an 11-digit <c>48…</c> run is a phone).</item>
/// <item>Owner identity names → <c>[OWNER]</c>, then known counterparty names → <c>[PARTY_n]</c>.
/// Both diacritic- and case-insensitive, and for two- or three-word names in any word order.</item>
/// </list>
/// </summary>
public sealed partial class PromptRedactor : IPromptRedactionSession
{
    public const string OwnerPlaceholder = "[OWNER]";
    public const string AddressPlaceholder = "[ADDRESS]";
    public const string PhonePlaceholder = "[PHONE]";
    public const string IdentifierPlaceholder = "[ID]";
    public const string AccountPlaceholder = "[ACCOUNT]";
    public const string CardPlaceholder = "[CARD]";
    public const string IbanPlaceholder = "[IBAN]";
    public const string NipPlaceholder = "[NIP]";
    public const string PeselPlaceholder = "[PESEL]";

    private const string PartyTokenPrefix = "PARTY_";

    // Shorter values ("PL", "SA") would redact unrelated words everywhere.
    private const int MinNameLetters = 3;

    // A name match must not touch a letter/digit on either side, and must not sit inside one of
    // our own placeholders ("[PARTY_1]" must not be re-matched by a counterparty called "PARTY").
    // (A brand such as "ALLEGRO" in "allegro.pl" is never matched: one-word names are legal
    // entities, see CounterpartyNames, and are not learnt.)
    private const string NameStartGuard = @"(?<![\p{L}\p{N}\[])";
    private const string NameEndGuard = @"(?![\p{L}\p{N}_\]])";

    // GUIDs are swapped for private-use sentinels while the patterns run: open, index digits, close.
    private const char GuidOpen = '';
    private const char GuidClose = '';
    private const char GuidDigitZero = '';

    private readonly Regex? _ownerRegex;
    private readonly Dictionary<string, string> _counterpartyNameByKey = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _tokenByKey = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _nameByToken = new(StringComparer.Ordinal);
    private Regex? _counterpartyRegex;
    private bool _counterpartyRegexStale;

    public PromptRedactor(IEnumerable<string> ownerNames, IEnumerable<string> counterpartyNames)
    {
        ArgumentNullException.ThrowIfNull(ownerNames);
        ArgumentNullException.ThrowIfNull(counterpartyNames);

        _ownerRegex = BuildNameRegex(ownerNames.SelectMany(NamePatterns));
        foreach (var name in counterpartyNames)
        {
            AddCounterparty(name);
        }
    }

    public bool HasOwnerNames => _ownerRegex is not null;

    public string Redact(string text) => RedactCore(text, StatementTextMode.Description);

    public string RedactDocument(string text) => RedactCore(text, StatementTextMode.RawDocument);

    public string Restore(string text)
    {
        if (string.IsNullOrEmpty(text) || _nameByToken.Count == 0)
        {
            return text;
        }

        return PartyTokenRegex().Replace(
            text,
            m => _nameByToken.TryGetValue(m.Value, out var name) ? name : m.Value);
    }

    private string RedactCore(string text, StatementTextMode mode)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text;
        }

        var masked = MaskGuids(text, out var guids);

        var fields = StatementDescriptionFields.Parse(masked, mode);
        foreach (var field in fields)
        {
            AddCounterparty(StatementDescriptionFields.CounterpartyNameOf(masked, field));
        }

        var result = ReplaceLabelledValues(masked, fields);

        result = IbanRegex().Replace(result, IbanPlaceholder);
        result = NipRegex().Replace(result, NipPlaceholder);
        result = AccountRegex().Replace(result, AccountPlaceholder);
        result = PrefixedPhoneRegex().Replace(result, PhonePlaceholder);
        result = CountryCodePhoneRegex().Replace(result, PhonePlaceholder);
        result = GroupedPhoneRegex().Replace(result, PhonePlaceholder);
        result = PeselRegex().Replace(result, PeselPlaceholder);

        if (_ownerRegex is not null)
        {
            result = _ownerRegex.Replace(result, OwnerPlaceholder);
        }

        var counterparties = CounterpartyRegex();
        if (counterparties is not null)
        {
            result = counterparties.Replace(result, m => TokenFor(m.Value));
        }

        return UnmaskGuids(result, guids);
    }

    /// <summary>
    /// Rebuilds the text with the personal labelled values replaced. Everything except a card
    /// location's value is free text, where street addresses and postal codes are redacted too.
    /// </summary>
    private string ReplaceLabelledValues(string text, IReadOnlyList<DescriptionField> fields)
    {
        var sb = new StringBuilder(text.Length);
        var cursor = 0;
        foreach (var field in fields)
        {
            AppendFreeText(sb, text.AsSpan(cursor, field.ValueStart - cursor));
            var value = text.Substring(field.ValueStart, field.ValueLength);
            if (field.Kind == DescriptionFieldKind.Location)
            {
                sb.Append(value); // the merchant's location (BIEDRONKA 1234, UL. ...) is signal, not personal
            }
            else if (ReplacementFor(field, value) is { } replacement)
            {
                sb.Append(replacement);
            }
            else
            {
                AppendFreeText(sb, value);
            }

            cursor = field.ValueEnd;
        }

        AppendFreeText(sb, text.AsSpan(cursor));
        return sb.ToString();
    }

    private static void AppendFreeText(StringBuilder sb, ReadOnlySpan<char> segment)
    {
        if (segment.IsEmpty)
        {
            return;
        }

        sb.Append(PolishAddress.Pattern.Replace(segment.ToString(), AddressPlaceholder));
    }

    /// <summary>The replacement for a labelled value, or <c>null</c> to keep it as free text.</summary>
    private string? ReplacementFor(DescriptionField field, string value)
    {
        if (value.Length == 0)
        {
            return null;
        }

        switch (field.Kind)
        {
            case DescriptionFieldKind.CounterpartyName:
                return CounterpartyNames.IsLegalEntity(Unescape(value)) ? null : TokenFor(Unescape(value));

            case DescriptionFieldKind.CounterpartyNameAndAddress:
            {
                var (name, address) = StatementDescriptionFields.SplitNameAndAddress(value);
                var nameOut = name.Length == 0 || CounterpartyNames.IsLegalEntity(Unescape(name))
                    ? name
                    : TokenFor(Unescape(name));
                return address.Length == 0 ? nameOut : string.Join(' ', nameOut, AddressPlaceholder).Trim();
            }

            case DescriptionFieldKind.Address:
                return AddressPlaceholder;
            case DescriptionFieldKind.Phone:
                return PhonePlaceholder;
            case DescriptionFieldKind.Identifier:
                return IdentifierPlaceholder;
            case DescriptionFieldKind.Account:
                return AccountPlaceholder;
            case DescriptionFieldKind.Card:
                return CardPlaceholder;
            default:
                return null;
        }
    }

    /// <summary>
    /// The placeholder for a counterparty name: <c>[OWNER]</c> when it is the owner (a transfer
    /// between the owner's own accounts), an already-issued placeholder value unchanged, otherwise a
    /// stable per-session <c>[PARTY_n]</c> pseudonym shared by every word order of the name.
    /// </summary>
    private string TokenFor(string name)
    {
        var trimmed = name.Trim();
        if (IsPlaceholder(trimmed))
        {
            return trimmed;
        }

        if (_ownerRegex is not null)
        {
            var ownerMatch = _ownerRegex.Match(trimmed);
            if (ownerMatch.Success && ownerMatch.Length == trimmed.Length)
            {
                return OwnerPlaceholder;
            }
        }

        var key = NameKey(trimmed);
        if (_tokenByKey.TryGetValue(key, out var token))
        {
            return token;
        }

        token = $"[{PartyTokenPrefix}{(_tokenByKey.Count + 1).ToString(CultureInfo.InvariantCulture)}]";
        _tokenByKey[key] = token;
        _nameByToken[token] = _counterpartyNameByKey.GetValueOrDefault(key, trimmed);
        return token;
    }

    /// <summary>
    /// Remembers a person counterparty name for unlabelled matching. Legal entities are skipped: their
    /// names are merchant signal (<c>ZUS</c>, <c>ALLEGRO</c>) and are not pseudonymised.
    /// </summary>
    private void AddCounterparty(string? name)
    {
        var trimmed = name?.Trim().TrimEnd(',', '.', ';').Trim();
        if (string.IsNullOrEmpty(trimmed)
            || IsPlaceholder(trimmed)
            || trimmed.Count(char.IsLetter) < MinNameLetters
            || CounterpartyNames.IsLegalEntity(trimmed))
        {
            return;
        }

        if (_counterpartyNameByKey.TryAdd(NameKey(trimmed), trimmed))
        {
            _counterpartyRegexStale = true;
        }
    }

    private Regex? CounterpartyRegex()
    {
        if (_counterpartyRegexStale)
        {
            _counterpartyRegex = BuildNameRegex(_counterpartyNameByKey.Values.SelectMany(NamePatterns));
            _counterpartyRegexStale = false;
        }

        return _counterpartyRegex;
    }

    /// <summary>
    /// Regex fragments for a name. Statements print names in either order ("JAN KOWALSKI" /
    /// "KOWALSKI JAN"), so a two- or three-word name also yields its word permutations.
    /// </summary>
    private static IEnumerable<string> NamePatterns(string? name)
    {
        var words = PolishText.Fold(name ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0 || string.Concat(words).Count(char.IsLetter) < MinNameLetters)
        {
            return [];
        }

        var orders = words.Length is 2 or 3 ? Permutations(words) : [words];
        return orders.Select(order => PolishText.DiacriticInsensitivePattern(string.Join(' ', order)));
    }

    /// <summary>Case-, diacritic- and word-order-insensitive key: the folded upper-case words, sorted.</summary>
    private static string NameKey(string name) =>
        string.Join(' ', PolishText.Key(name).Split(' ', StringSplitOptions.RemoveEmptyEntries).Order(StringComparer.Ordinal));

    private static Regex? BuildNameRegex(IEnumerable<string> patterns)
    {
        // Longest pattern first so "JAN KOWALSKI" wins over a separately listed "KOWALSKI".
        var distinct = patterns.Distinct(StringComparer.Ordinal).OrderByDescending(p => p.Length).ToList();
        if (distinct.Count == 0)
        {
            return null;
        }

        return new Regex(
            $"{NameStartGuard}(?:{string.Join('|', distinct)}){NameEndGuard}",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static List<string[]> Permutations(string[] words)
    {
        if (words.Length == 1)
        {
            return [words];
        }

        var result = new List<string[]>();
        for (var i = 0; i < words.Length; i++)
        {
            var rest = words.Where((_, index) => index != i).ToArray();
            foreach (var tail in Permutations(rest))
            {
                result.Add([words[i], .. tail]);
            }
        }

        return result;
    }

    private static bool IsPlaceholder(string value) =>
        value.Length > 2 && value[0] == '[' && value[^1] == ']';

    private static string Unescape(string value) => value.Replace("\"\"", "\"", StringComparison.Ordinal);

    private static string MaskGuids(string text, out List<string>? guids)
    {
        guids = null;
        if (text.Contains(GuidOpen, StringComparison.Ordinal))
        {
            return text; // cannot mask unambiguously; the id may be redacted, which is the safe side
        }

        List<string>? found = null;
        var masked = GuidRegex().Replace(text, m =>
        {
            found ??= [];
            found.Add(m.Value);
            var index = (found.Count - 1).ToString(CultureInfo.InvariantCulture);
            return GuidOpen + string.Concat(index.Select(d => (char)(GuidDigitZero + (d - '0')))) + GuidClose;
        });
        guids = found;
        return masked;
    }

    private static string UnmaskGuids(string text, List<string>? guids)
    {
        if (guids is null)
        {
            return text;
        }

        return MaskedGuidRegex().Replace(text, m =>
        {
            var index = 0;
            foreach (var c in m.Groups[1].Value)
            {
                index = (index * 10) + (c - GuidDigitZero);
            }

            return index < guids.Count ? guids[index] : m.Value;
        });
    }

    [GeneratedRegex(@"(?<![0-9A-Fa-f-])[0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12}(?![0-9A-Fa-f-])")]
    private static partial Regex GuidRegex();

    [GeneratedRegex("([-]+)")]
    private static partial Regex MaskedGuidRegex();

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

    // "+48 500 600 700", "+48500600700", "0048-500-600-700".
    [GeneratedRegex(@"(?<![\d+])(?:\+|00)48[ -]?\d{3}[ -]?\d{3}[ -]?\d{3}(?!\d)")]
    private static partial Regex PrefixedPhoneRegex();

    // "48500600700" (BLIK "OD: 48… DO: 48…") or "48 500 600 700": the country code without "+".
    [GeneratedRegex(@"(?<![\d+])48(?:\d{9}|[ -]\d{3}[ -]\d{3}[ -]\d{3})(?!\d)")]
    private static partial Regex CountryCodePhoneRegex();

    // A bare mobile number written in 3-3-3 groups ("500 600 700", "500-600-700"). Plain-space or
    // hyphen groups only and never next to a decimal separator, so an amount ("1 234,56", or pl-PL
    // output, which groups with a non-breaking space) is not taken for a phone. An ungrouped 9-digit
    // run is left alone: reference numbers look the same and only the labelled field is reliable.
    [GeneratedRegex(@"(?<!\d)(?<!\d[.,])\d{3}[ -]\d{3}[ -]\d{3}(?!\d)(?![.,]\d)")]
    private static partial Regex GroupedPhoneRegex();

    // PESEL: exactly 11 digits not part of a longer number or a decimal ("/TI/P44051401458/" in a tax
    // transfer title still matches; "12345678901.50" does not). No checksum check on purpose: an
    // 11-digit reference number that happens to be redacted carries no categorisation signal. GUIDs
    // are masked before this runs, so a hex segment such as "a12345678901" is not touched.
    [GeneratedRegex(@"(?<!\d)(?<!\d[.,])\d{11}(?!\d)(?![.,]\d)")]
    private static partial Regex PeselRegex();

    [GeneratedRegex(@"\[PARTY_\d+\]")]
    private static partial Regex PartyTokenRegex();
}
