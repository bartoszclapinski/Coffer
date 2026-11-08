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
/// <item>Learn counterparty names from the labelled name sub-fields of the text, so the same name is
/// also caught where it appears without its label (e.g. repeated in a title).</item>
/// <item>Replace the value of every personal labelled sub-field: names become pseudonyms, addresses /
/// phones / identifiers / accounts / card numbers become fixed placeholders. The label stays, so the
/// model still sees the structure; the title and location stay readable.</item>
/// <item>Pattern redactions: IBAN before bare account runs (so <c>PL61…</c> is <c>[IBAN]</c>, not
/// <c>PL[ACCOUNT]</c>), NIP, phone numbers before PESEL (an 11-digit <c>48…</c> run is a phone).</item>
/// <item>Owner identity names → <c>[OWNER]</c>, then known counterparty names → <c>[PARTY_n]</c>.
/// Both diacritic- and case-insensitive.</item>
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
    private const string NameStartGuard = @"(?<![\p{L}\p{N}\[])";
    private const string NameEndGuard = @"(?![\p{L}\p{N}_\]])";

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

        _ownerRegex = BuildOwnerRegex(ownerNames);
        foreach (var name in counterpartyNames)
        {
            AddCounterparty(name);
        }
    }

    public bool HasOwnerNames => _ownerRegex is not null;

    public string Redact(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text;
        }

        var fields = StatementDescriptionFields.Parse(text);
        foreach (var field in fields)
        {
            if (field.Kind == DescriptionFieldKind.CounterpartyName)
            {
                AddCounterparty(text.Substring(field.ValueStart, field.ValueLength));
            }
        }

        var result = fields.Count > 0 ? ReplaceLabelledValues(text, fields) : text;

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

        return result;
    }

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

    private string ReplaceLabelledValues(string text, IReadOnlyList<DescriptionField> fields)
    {
        var sb = new StringBuilder(text.Length);
        var cursor = 0;
        foreach (var field in fields)
        {
            var placeholder = PlaceholderFor(field, text);
            if (placeholder is null || field.ValueLength == 0)
            {
                continue;
            }

            sb.Append(text, cursor, field.ValueStart - cursor);
            sb.Append(placeholder);
            cursor = field.ValueEnd;
        }

        sb.Append(text, cursor, text.Length - cursor);
        return sb.ToString();
    }

    private string? PlaceholderFor(DescriptionField field, string text) => field.Kind switch
    {
        DescriptionFieldKind.CounterpartyName => TokenFor(text.Substring(field.ValueStart, field.ValueLength)),
        DescriptionFieldKind.Address => AddressPlaceholder,
        DescriptionFieldKind.Phone => PhonePlaceholder,
        DescriptionFieldKind.Identifier => IdentifierPlaceholder,
        DescriptionFieldKind.Account => AccountPlaceholder,
        DescriptionFieldKind.Card => CardPlaceholder,
        _ => null,
    };

    /// <summary>
    /// The placeholder for a counterparty name: <c>[OWNER]</c> when it is the owner (a transfer
    /// between the owner's own accounts), an already-issued placeholder value unchanged, otherwise a
    /// stable per-session <c>[PARTY_n]</c> pseudonym.
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

        var key = PolishText.Key(trimmed);
        if (_tokenByKey.TryGetValue(key, out var token))
        {
            return token;
        }

        token = $"[{PartyTokenPrefix}{(_tokenByKey.Count + 1).ToString(CultureInfo.InvariantCulture)}]";
        _tokenByKey[key] = token;
        _nameByToken[token] = _counterpartyNameByKey.GetValueOrDefault(key, trimmed);
        return token;
    }

    private void AddCounterparty(string? name)
    {
        var trimmed = name?.Trim().TrimEnd(',', '.', ';');
        if (string.IsNullOrEmpty(trimmed) || IsPlaceholder(trimmed) || trimmed.Count(char.IsLetter) < MinNameLetters)
        {
            return;
        }

        if (_counterpartyNameByKey.TryAdd(PolishText.Key(trimmed), trimmed))
        {
            _counterpartyRegexStale = true;
        }
    }

    private Regex? CounterpartyRegex()
    {
        if (_counterpartyRegexStale)
        {
            _counterpartyRegex = BuildNameRegex(_counterpartyNameByKey.Values.Select(PolishText.DiacriticInsensitivePattern));
            _counterpartyRegexStale = false;
        }

        return _counterpartyRegex;
    }

    private static Regex? BuildOwnerRegex(IEnumerable<string> ownerNames)
    {
        // Statements print names in either order ("JAN KOWALSKI" / "KOWALSKI JAN"), so a two- or
        // three-word owner name also matches its word permutations.
        var patterns = new List<string>();
        foreach (var raw in ownerNames)
        {
            var words = PolishText.Fold(raw ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (words.Length == 0 || string.Concat(words).Count(char.IsLetter) < MinNameLetters)
            {
                continue;
            }

            var orders = words.Length is 2 or 3 ? Permutations(words) : [words];
            patterns.AddRange(orders.Select(order => PolishText.DiacriticInsensitivePattern(string.Join(' ', order))));
        }

        return BuildNameRegex(patterns);
    }

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
    // 11-digit reference number that happens to be redacted carries no categorisation signal.
    [GeneratedRegex(@"(?<!\d)(?<!\d[.,])\d{11}(?!\d)(?![.,]\d)")]
    private static partial Regex PeselRegex();

    [GeneratedRegex(@"\[PARTY_\d+\]")]
    private static partial Regex PartyTokenRegex();
}
