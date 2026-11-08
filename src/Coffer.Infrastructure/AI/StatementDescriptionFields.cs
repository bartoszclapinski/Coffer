using System.Text.RegularExpressions;

namespace Coffer.Infrastructure.AI;

/// <summary>What a labelled statement sub-field holds, as far as AI privacy is concerned.</summary>
public enum DescriptionFieldKind
{
    /// <summary>Bookkeeping metadata with no categorisation value (operation date, reference number, ...).</summary>
    Metadata,

    /// <summary>The transfer title (<c>Tytuł:</c>). Kept for the AI; still passes the pattern redactions.</summary>
    Title,

    /// <summary>The card/BLIK location (<c>Lokalizacja:</c> and its <c>Adres/Miasto/Kraj</c> parts). Kept for the AI.</summary>
    Location,

    /// <summary>
    /// Counterparty name (<c>Nazwa nadawcy/odbiorcy:</c>). A person's name is pseudonymised; a legal
    /// entity's name is kept (<see cref="CounterpartyNames.IsLegalEntity"/>).
    /// </summary>
    CounterpartyName,

    /// <summary>
    /// Counterparty name possibly followed by its address, as other banks label it (<c>Odbiorca:</c>,
    /// <c>Dane nadawcy:</c>, <c>Nazwa i adres odbiorcy:</c>, ...). Split by
    /// <see cref="StatementDescriptionFields.SplitNameAndAddress"/>: the name is treated as a
    /// <see cref="CounterpartyName"/>, the address is redacted.
    /// </summary>
    CounterpartyNameAndAddress,

    /// <summary>Counterparty street address. Personal: redacted.</summary>
    Address,

    /// <summary>Phone number (BLIK to phone). Personal: redacted.</summary>
    Phone,

    /// <summary>Payer identifier (<c>Nazwa i nr identyfikatora:</c>, PESEL/NIP in tax transfers). Personal: redacted.</summary>
    Identifier,

    /// <summary>Sender/recipient account number. Personal: redacted.</summary>
    Account,

    /// <summary>Masked card number. Redacted.</summary>
    Card,
}

/// <summary>How the text handed to <see cref="StatementDescriptionFields.Parse"/> was produced.</summary>
public enum StatementTextMode
{
    /// <summary>
    /// A parsed transaction description, or a prompt built from them. The CSV parser has already
    /// unescaped the fields and joined the sub-field columns with spaces, so a value may contain a
    /// bare quote or a semicolon (<c>P.H.U. "ANEX" ANNA NOWAK</c>). A value ends only at the next
    /// known label or at a tab or line break.
    /// </summary>
    Description,

    /// <summary>
    /// Raw statement text sent whole to the AI fallback parser: CSV (quoted fields, <c>""</c> escapes,
    /// comma or semicolon delimiters) or PDF text. A value inside a quoted CSV field ends at the
    /// field's closing quote; outside quotes it ends at a semicolon or a tab. A line break always
    /// ends it.
    /// </summary>
    RawDocument,
}

/// <summary>One labelled sub-field found in a statement description: the label and its value span.</summary>
/// <param name="Label">The label text as it appears in the source (without the colon).</param>
/// <param name="Kind">The privacy classification of the label.</param>
/// <param name="LabelStart">Index of the label's first character.</param>
/// <param name="ValueStart">Index of the value's first character (after the colon and spaces).</param>
/// <param name="ValueEnd">Exclusive end of the value (before the next label or field terminator).</param>
public sealed record DescriptionField(string Label, DescriptionFieldKind Kind, int LabelStart, int ValueStart, int ValueEnd)
{
    public int ValueLength => ValueEnd - ValueStart;
}

/// <summary>
/// Finds the labelled sub-fields Polish banks pack into a transaction description:
/// <c>Nazwa odbiorcy: … Adres odbiorcy: … Tytuł: …</c> (PKO BP), and <c>Odbiorca: …</c>,
/// <c>Dane nadawcy: …</c> or <c>Kontrahent: …</c> (other banks, seen mostly by the AI fallback
/// parser). A value runs from its label to the next known label, or to a field terminator that
/// depends on the <see cref="StatementTextMode"/>. Labels match case-insensitively (normalised
/// descriptions are upper case) and with or without Polish diacritics (<c>Tytuł</c> / <c>Tytul</c>).
/// </summary>
public static class StatementDescriptionFields
{
    private static readonly (string Label, DescriptionFieldKind Kind)[] _labels =
    [
        // PKO BP "Historia rachunku".
        ("Numer faktury VAT lub okres płatności zbiorczej", DescriptionFieldKind.Metadata),
        ("Referencje własne zleceniodawcy", DescriptionFieldKind.Metadata),
        ("Oryginalna kwota operacji", DescriptionFieldKind.Metadata),
        ("Nazwa i nr identyfikatora", DescriptionFieldKind.Identifier),
        ("Data wykonania operacji", DescriptionFieldKind.Metadata),
        ("Rachunek nadawcy/odbiorcy", DescriptionFieldKind.Account),
        ("Nazwa nadawcy/odbiorcy", DescriptionFieldKind.CounterpartyName),
        ("Adres nadawcy/odbiorcy", DescriptionFieldKind.Address),
        ("Identyfikator płatności", DescriptionFieldKind.Metadata),
        ("Data i czas operacji", DescriptionFieldKind.Metadata),
        ("Nazwa zleceniodawcy", DescriptionFieldKind.CounterpartyName),
        ("Adres zleceniodawcy", DescriptionFieldKind.Address),
        ("Numer referencyjny", DescriptionFieldKind.Metadata),
        ("Rachunek odbiorcy", DescriptionFieldKind.Account),
        ("Rachunek nadawcy", DescriptionFieldKind.Account),
        ("Symbol formularza", DescriptionFieldKind.Metadata),
        ("Okres płatności", DescriptionFieldKind.Metadata),
        ("Numer telefonu", DescriptionFieldKind.Phone),
        ("Nazwa odbiorcy", DescriptionFieldKind.CounterpartyName),
        ("Nazwa nadawcy", DescriptionFieldKind.CounterpartyName),
        ("Adres odbiorcy", DescriptionFieldKind.Address),
        ("Adres nadawcy", DescriptionFieldKind.Address),
        ("Numer karty", DescriptionFieldKind.Card),
        ("Lokalizacja", DescriptionFieldKind.Location),
        ("Operacja", DescriptionFieldKind.Metadata),
        ("Miasto", DescriptionFieldKind.Location),
        ("Tytuł", DescriptionFieldKind.Title),
        ("Adres", DescriptionFieldKind.Location),
        ("Kraj", DescriptionFieldKind.Location),

        // Common labels of other Polish banks.
        ("Nazwa i adres kontrahenta", DescriptionFieldKind.CounterpartyNameAndAddress),
        ("Nazwa i adres odbiorcy", DescriptionFieldKind.CounterpartyNameAndAddress),
        ("Nazwa i adres nadawcy", DescriptionFieldKind.CounterpartyNameAndAddress),
        ("Dane zleceniodawcy", DescriptionFieldKind.CounterpartyNameAndAddress),
        ("Dane kontrahenta", DescriptionFieldKind.CounterpartyNameAndAddress),
        ("Dane odbiorcy", DescriptionFieldKind.CounterpartyNameAndAddress),
        ("Dane nadawcy", DescriptionFieldKind.CounterpartyNameAndAddress),
        ("Zleceniodawca", DescriptionFieldKind.CounterpartyNameAndAddress),
        ("Beneficjent", DescriptionFieldKind.CounterpartyNameAndAddress),
        ("Kontrahent", DescriptionFieldKind.CounterpartyNameAndAddress),
        ("Odbiorca", DescriptionFieldKind.CounterpartyNameAndAddress),
        ("Nadawca", DescriptionFieldKind.CounterpartyNameAndAddress),
        ("Nazwa kontrahenta", DescriptionFieldKind.CounterpartyName),
        ("Adres kontrahenta", DescriptionFieldKind.Address),
        ("Rachunek kontrahenta", DescriptionFieldKind.Account),
        ("Rachunek beneficjenta", DescriptionFieldKind.Account),
        ("Konto odbiorcy", DescriptionFieldKind.Account),
        ("Konto nadawcy", DescriptionFieldKind.Account),
        ("Numer rachunku", DescriptionFieldKind.Account),
    ];

    private static readonly Regex _labelRegex = BuildLabelRegex();

    /// <summary>Returns the labelled sub-fields of <paramref name="text"/>, in order of appearance.</summary>
    public static IReadOnlyList<DescriptionField> Parse(string text, StatementTextMode mode = StatementTextMode.Description)
    {
        if (string.IsNullOrEmpty(text))
        {
            return [];
        }

        var matches = _labelRegex.Matches(text);
        if (matches.Count == 0)
        {
            return [];
        }

        var fields = new List<DescriptionField>(matches.Count);
        for (var i = 0; i < matches.Count; i++)
        {
            var match = matches[i];
            var label = match.Groups["label"];
            var valueStart = match.Index + match.Length;
            var limit = i + 1 < matches.Count ? matches[i + 1].Index : text.Length;

            var valueEnd = mode == StatementTextMode.RawDocument
                ? RawDocumentValueEnd(text, label.Index, valueStart, limit)
                : DescriptionValueEnd(text, valueStart, limit);
            while (valueEnd > valueStart && char.IsWhiteSpace(text[valueEnd - 1]))
            {
                valueEnd--;
            }

            var kind = KindOf(label.Value);
            if (kind == DescriptionFieldKind.Location && IsLocationPart(label.Value)
                && (fields.Count == 0 || fields[^1].Kind != DescriptionFieldKind.Location))
            {
                // "Adres:" / "Miasto:" / "Kraj:" describe the merchant only as parts of a PKO
                // "Lokalizacja:" block. On their own (another bank's transfer) "Adres:" is the
                // counterparty's address and the others are ordinary text.
                kind = PolishText.Key(label.Value) == "ADRES" ? DescriptionFieldKind.Address : DescriptionFieldKind.Metadata;
            }

            fields.Add(new DescriptionField(label.Value, kind, label.Index, valueStart, valueEnd));
        }

        return fields;
    }

    /// <summary>
    /// Splits a <see cref="DescriptionFieldKind.CounterpartyNameAndAddress"/> value at the first street
    /// address or postal code: <c>JAN WISNIEWSKI ul. Polna 1</c> becomes (<c>JAN WISNIEWSKI</c>,
    /// <c>ul. Polna 1</c>). Without an address the whole value is the name and the address is empty.
    /// </summary>
    public static (string Name, string Address) SplitNameAndAddress(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var start = PolishAddress.IndexOfFirst(value);
        return start < 0
            ? (value.Trim(), string.Empty)
            : (value[..start].Trim().TrimEnd(',').TrimEnd(), value[start..].Trim());
    }

    /// <summary>
    /// The counterparty name a name field holds (the name part of a name-and-address value), or
    /// <c>null</c> for any other field or an empty value. <c>""</c> (a raw CSV escape) is unescaped.
    /// </summary>
    public static string? CounterpartyNameOf(string text, DescriptionField field)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(field);
        if (field.Kind is not (DescriptionFieldKind.CounterpartyName or DescriptionFieldKind.CounterpartyNameAndAddress)
            || field.ValueLength == 0)
        {
            return null;
        }

        var value = text.Substring(field.ValueStart, field.ValueLength).Replace("\"\"", "\"", StringComparison.Ordinal);
        var name = field.Kind == DescriptionFieldKind.CounterpartyNameAndAddress ? SplitNameAndAddress(value).Name : value;
        return name.Length == 0 ? null : name;
    }

    private static int DescriptionValueEnd(string text, int valueStart, int limit)
    {
        var end = text.AsSpan(valueStart, limit - valueStart).IndexOfAny('\t', '\r', '\n');
        return end >= 0 ? valueStart + end : limit;
    }

    private static int RawDocumentValueEnd(string text, int labelStart, int valueStart, int limit)
    {
        // The label sits inside a quoted CSV field when an odd number of quotes precede it on its
        // line. A "" escape adds two quotes, so it does not change the parity.
        var lineStart = labelStart == 0 ? 0 : text.AsSpan(0, labelStart).LastIndexOfAny('\r', '\n') + 1;
        var quoted = text.AsSpan(lineStart, labelStart - lineStart).Count('"') % 2 == 1;

        for (var i = valueStart; i < limit; i++)
        {
            var c = text[i];
            if (c is '\r' or '\n')
            {
                return i;
            }

            if (quoted)
            {
                if (c != '"')
                {
                    continue;
                }

                if (i + 1 < text.Length && text[i + 1] == '"')
                {
                    i++; // an escaped quote inside the field
                    continue;
                }

                return i;
            }

            if (c is ';' or '\t')
            {
                return i;
            }
        }

        return limit;
    }

    private static bool IsLocationPart(string label) => PolishText.Key(label) is "ADRES" or "MIASTO" or "KRAJ";

    private static DescriptionFieldKind KindOf(string label)
    {
        var folded = PolishText.Fold(label);
        foreach (var (candidate, kind) in _labels)
        {
            if (string.Equals(PolishText.Fold(candidate), folded, StringComparison.OrdinalIgnoreCase))
            {
                return kind;
            }
        }

        return DescriptionFieldKind.Metadata;
    }

    private static Regex BuildLabelRegex()
    {
        // A label starts the text or follows whitespace / a CSV delimiter, and is followed by a colon.
        // Each letter matches with or without its Polish diacritic ("TYTUL:" in an ASCII export).
        // Longest first, so "Nazwa nadawcy/odbiorcy" wins over "Nazwa nadawcy".
        var alternatives = string.Join(
            '|',
            _labels.OrderByDescending(l => l.Label.Length).Select(l => PolishText.DiacriticInsensitivePattern(l.Label)));
        return new Regex(
            $@"(?<![^\s""',;(])(?<label>{alternatives})[ \t]*:[ \t]*",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }
}
