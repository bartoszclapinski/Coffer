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

    /// <summary>Counterparty name (<c>Nazwa nadawcy/odbiorcy:</c>). Personal: pseudonymised.</summary>
    CounterpartyName,

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
/// Finds the labelled sub-fields PKO BP (and other Polish banks) pack into a transaction
/// description: <c>Nazwa odbiorcy: … Adres odbiorcy: … Tytuł: …</c>. The CSV parser joins the
/// sub-field columns with spaces, so a value runs from its label to the next known label, or to a
/// hard field terminator (quote, semicolon, tab, line break) when the text is raw CSV, as in the AI
/// fallback parser's prompt. Labels match case-insensitively (normalised descriptions are upper
/// case) and with or without Polish diacritics (<c>Tytuł</c> / <c>Tytul</c>).
/// </summary>
public static class StatementDescriptionFields
{
    // Longest first, so "Nazwa nadawcy/odbiorcy" wins over "Nazwa nadawcy" and
    // "Nazwa i nr identyfikatora" is never read as a "Nazwa ..." name field.
    private static readonly (string Label, DescriptionFieldKind Kind)[] _labels =
    [
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
    ];

    private static readonly Regex _labelRegex = BuildLabelRegex();

    private static readonly char[] _terminators = ['"', ';', '\t', '\r', '\n'];

    /// <summary>Returns the labelled sub-fields of <paramref name="text"/>, in order of appearance.</summary>
    public static IReadOnlyList<DescriptionField> Parse(string text)
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

            var terminator = text.AsSpan(valueStart, limit - valueStart).IndexOfAny(_terminators);
            var valueEnd = terminator >= 0 ? valueStart + terminator : limit;
            while (valueEnd > valueStart && char.IsWhiteSpace(text[valueEnd - 1]))
            {
                valueEnd--;
            }

            fields.Add(new DescriptionField(label.Value, KindOf(label.Value), label.Index, valueStart, valueEnd));
        }

        return fields;
    }

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
        var alternatives = string.Join('|', _labels.Select(l => PolishText.DiacriticInsensitivePattern(l.Label)));
        return new Regex(
            $@"(?<![^\s""',;(])(?<label>{alternatives})[ \t]*:[ \t]*",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }
}
