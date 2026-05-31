# 03 — Statement Parsers

## The problem

Every Polish bank exports PDF statements in a different layout. Even a single bank has multiple layout variants (checking account vs credit card vs savings vs foreign currency). The app must:

1. Detect which bank produced the PDF
2. Route to a bank-specific parser
3. Fall back gracefully when no parser exists yet
4. Be testable and stable across bank-side layout changes

## Architecture: registry + strategy + AI fallback

```
Statement input (PDF or CSV)
   │
   ▼
BankDetector ── PDF: first-page text · CSV: header signature ──▶ BankFingerprint
   │
   ▼
StatementParserRegistry.Resolve(fingerprint, format) ──▶ IStatementParser
   │
   ├── PkoHistoriaCsvParser            (PKO_BP, Csv)
   ├── MBankStatementParser            (future, Pdf)
   ├── IngStatementParser              (future, Pdf)
   ├── ... (one per supported bank+format)
   └── AiAssistedParser (future fallback when no specific parser)
   │
   ▼
ParseResult { Account, Period, Transactions[], Source }
   │
   ▼
Normalization → Deduplication → Persistence
```

## Interfaces

Detector and parser operate on a format-neutral `StatementInput` (a `Stream` +
`StatementFormat` + optional file name), not on a `PdfDocument`. This keeps
`Coffer.Core` free of any third-party runtime dependency (hard rule #3) and lets
a single registry route both PDF and CSV statements. PDF parsers open PdfPig from
the stream themselves; the CSV parser reads the stream as Windows-1250.

```csharp
public enum StatementFormat { Pdf, Csv }

public sealed record StatementInput(Stream Content, StatementFormat Format, string? FileName = null);

public interface IBankDetector
{
    // Switches on input.Format: first-page text for PDF, header signature for CSV.
    BankFingerprint? Detect(StatementInput input);
}

public record BankFingerprint(string BankCode, string BankName, int Priority);

public interface IStatementParser
{
    string BankCode { get; }
    StatementFormat Format { get; }            // registry resolves on (BankCode, Format)
    bool CanHandle(BankFingerprint fingerprint);
    Task<ParseResult> ParseAsync(StatementInput input, CancellationToken ct);
}

public class StatementParserRegistry
{
    private readonly Dictionary<string, IStatementParser> _parsers;
    private readonly IStatementParser _fallback;

    public StatementParserRegistry(
        IEnumerable<IStatementParser> parsers,
        AiAssistedParser fallback)
    {
        _parsers = parsers.ToDictionary(p => p.BankCode);
        _fallback = fallback;
    }

    public IStatementParser Resolve(BankFingerprint? fp) =>
        fp is not null && _parsers.TryGetValue(fp.BankCode, out var parser)
            ? parser
            : _fallback;
}

public class ParseResult
{
    public required string BankCode { get; init; }
    public required string AccountNumber { get; init; }
    public required string Currency { get; init; }
    public required DateOnly PeriodFrom { get; init; }
    public required DateOnly PeriodTo { get; init; }
    public required List<ParsedTransaction> Transactions { get; init; }
    public ParserConfidence Confidence { get; init; } // High | Medium | Low
    public List<string> Warnings { get; init; } = [];
}

public class ParsedTransaction
{
    public required DateOnly Date { get; init; }
    public DateOnly? BookingDate { get; init; }
    public required decimal Amount { get; init; }
    public required string Currency { get; init; }
    public required string Description { get; init; }
    public string? Merchant { get; init; }
}
```

## Bank detection

First-page text search for each bank's identifying phrase. Multiple phrases per bank to handle layout variants.

```csharp
public class FingerprintBankDetector : IBankDetector
{
    private static readonly BankFingerprint[] Fingerprints =
    {
        new("PKO_BP",     "PKO Bank Polski",                  1),
        new("MBANK",      "mBank S.A.",                       1),
        new("ING",        "ING Bank Śląski",                  1),
        new("PEKAO",      "Bank Polska Kasa Opieki",          1),
        new("SANTANDER",  "Santander Bank Polska",            1),
        new("MILLENNIUM", "Bank Millennium",                  1),
        new("CITI",       "Citi Handlowy",                    1),
        new("ALIOR",      "Alior Bank",                       1),
        // ... more as needed
    };

    public BankFingerprint? Detect(PdfDocument doc)
    {
        if (doc.NumberOfPages == 0) return null;

        var firstPageText = doc.GetPage(1).Text;
        return Fingerprints
            .Where(f => firstPageText.Contains(f.BankName, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(f => f.Priority)
            .FirstOrDefault();
    }
}
```

If text-based detection fails (e.g., scanned PDF), the detector returns `null` and the registry falls back to `AiAssistedParser`.

## PdfPig fundamentals — read this before writing a parser

`PdfPig` gives you `Letter` objects with X/Y coordinates. A "row" in a transaction table is a set of letters with similar Y coordinates. A "column" is similar X.

### Detecting if PDF has real text or is a scan

```csharp
public static bool HasExtractableText(PdfDocument doc)
{
    if (doc.NumberOfPages == 0) return false;
    return doc.GetPage(1).Letters.Count > 50;
}
```

Below threshold = probably a scan; show user a message: "This PDF appears to be a scan. Please export the original PDF from your online banking."

### Grouping letters into rows

```csharp
public static IEnumerable<List<Letter>> GroupIntoRows(IReadOnlyList<Letter> letters, double yTolerance = 2.0)
{
    var sorted = letters.OrderByDescending(l => l.Location.Y).ToList();
    var current = new List<Letter>();
    double? currentY = null;

    foreach (var letter in sorted)
    {
        if (currentY is null || Math.Abs(letter.Location.Y - currentY.Value) <= yTolerance)
        {
            current.Add(letter);
            currentY ??= letter.Location.Y;
        }
        else
        {
            yield return current.OrderBy(l => l.Location.X).ToList();
            current = [letter];
            currentY = letter.Location.Y;
        }
    }
    if (current.Count > 0) yield return current.OrderBy(l => l.Location.X).ToList();
}
```

## Polish-specific gotchas

### Amounts

Polish format: `1 234,56 zł` where space is a non-breaking space `\u00A0`.

```csharp
public static decimal ParseAmount(string raw)
{
    var cleaned = raw
        .Replace("\u00A0", "")          // non-breaking space
        .Replace(" ", "")
        .Replace("zł", "")
        .Replace("PLN", "")
        .Trim()
        .Replace(",", ".");
    return decimal.Parse(cleaned, CultureInfo.InvariantCulture);
}
```

### Dates

Polish format: `28.11.2025` or `28-11-2025`. Always parse with explicit format:

```csharp
public static DateOnly ParsePolishDate(string raw)
{
    var formats = new[] { "dd.MM.yyyy", "dd-MM-yyyy", "yyyy-MM-dd" };
    return DateOnly.ParseExact(raw.Trim(), formats, CultureInfo.InvariantCulture);
}
```

### Multi-line descriptions

PKO and others wrap long merchant descriptions across 2–3 lines. Detect by checking if the next row has no date or amount but does have description-region X coordinates. Concatenate with single space.

### Description prefixes that should be stripped

Polish bank statements include codes that bury the actual merchant:
- `KRD` (transaction code)
- `BLIK` prefix
- `Płatność kartą` / `Karta debetowa` boilerplate
- `/PL/` country codes
- Card number suffixes like `/4567/` or `**4567`

```csharp
public static string NormalizeDescription(string raw)
{
    var s = raw;
    s = Regex.Replace(s, @"\s+", " ");                  // collapse whitespace
    s = Regex.Replace(s, @"\*+\d{4}", "");              // card numbers
    s = Regex.Replace(s, @"/PL/|/EU/", "");
    s = Regex.Replace(s, @"^(BLIK\s+|KRD\s+)", "", RegexOptions.IgnoreCase);
    return s.Trim().ToUpperInvariant();                 // upper for matching, original kept in `Description`
}
```

### Account numbers

IBAN with spaces: `PL61 1090 1014 0000 0712 1981 2874`. Normalize to: `PL61109010140000071219812874` (no spaces, country prefix preserved).

```csharp
public static string NormalizeAccountNumber(string raw)
{
    return Regex.Replace(raw, @"[\s\-]", "").ToUpperInvariant();
}
```

## PKO BP — parsed via CSV ("Historia rachunku"), not PDF

PKO BP is parsed from its **"Historia rachunku" CSV export**, implemented in
`PkoHistoriaCsvParser` (`Format => StatementFormat.Csv`).

**Why CSV, not the PDF "Wyciąg z rachunku":** Sprint 7 built a deterministic PDF
parser for the monthly "Wyciąg z rachunku" statement. Manual verification then
showed that the only freely-available PKO export is the on-demand "Historia
rachunku" (available as CSV/PDF/XML/XLS/HTML) — the formal "Wyciąg z rachunku" is
a paid document. The two have entirely different layouts. CSV is the far more
robust target: explicit columns (no positional X-coordinate guessing), ISO dates,
signed dot-decimal amounts, explicit per-row currency, and it commits cleanly as
golden-file tests. The speculative PDF "Wyciąg" parser and its PKO-specific
helpers were removed in Sprint 8 (recoverable from git history if PKO PDF ever
becomes free); the generic PDF letter-grouping helpers and the detector's
first-page text-matching path are retained as the multi-bank PDF backbone for
future banks that only offer PDF.

### "Historia rachunku" CSV schema

- **Windows-1250**, no BOM, comma-separated, every field quoted (`"..."`) —
  embedded commas occur inside description fields, so an RFC-4180-correct reader
  (CsvHelper) is required, not `Split(',')`.
- Fixed **12 columns**; the header names the first seven, then five unnamed
  overflow columns hold the description sub-fields:
  `Data operacji, Data waluty, Typ transakcji, Kwota, Waluta, Saldo po transakcji, Opis transakcji`
  + 5 empty headers (indices 7–11). Fields are read **positionally** because the
  overflow columns share an empty header name.
- `Kwota`: single **signed, dot-decimal** value (`-10.00`, `+2400.00`) — parsed
  with invariant culture after stripping a leading `+` (not the Polish comma
  helper).
- Dates: ISO `yyyy-MM-dd` for both `Data operacji` (→ `Date`) and `Data waluty`
  (→ `BookingDate`).
- `Waluta`: explicit per row (→ `Currency`).
- `Description`: join of non-empty columns from index 6 onward; labelled
  sub-fields like `Nazwa nadawcy/odbiorcy:`, `Tytuł:`, `Lokalizacja:`. `Merchant`
  is extracted from the `Nazwa …:` sub-field when present. One field may carry a
  leading `'` (Excel text-guard) → stripped.
- **No account number or period in the body.** `PeriodFrom`/`PeriodTo` are derived
  from the min/max `Data operacji`; `AccountNumber` is left empty with a warning
  (the Phase-2 import flow confirms the target account with the user).
- `Confidence = High` (deterministic).

A wrong header shape throws `UnsupportedCsvLayoutException` (sealed; carries a
structural hint only — never row content, per hard rules #6/#11).

## AI-assisted fallback parser

For unknown banks (e.g., owner refinances mortgage to a bank we don't have a parser for yet), the AI parser handles import without code changes.

```csharp
public class AiAssistedParser : IStatementParser
{
    public string BankCode => "AI_FALLBACK";
    public bool CanHandle(BankFingerprint? fp) => true;     // last resort

    public async Task<ParseResult> ParseAsync(PdfDocument doc, CancellationToken ct)
    {
        var rawText = ExtractStructuredText(doc);           // full text with positions
        var prompt = BuildPrompt(rawText);
        var json = await _claude.CompleteJsonAsync(prompt, model: "claude-sonnet-4-6", ct);
        return JsonSerializer.Deserialize<ParseResult>(json)!
            with { Confidence = ParserConfidence.Medium };
    }
}
```

### Prompt template (anonymized inputs only)

```
You are a Polish bank statement parser. Extract structured data from this statement.

Return JSON matching this schema:
{
  "bankName": string,
  "accountNumber": string (digits only with country prefix),
  "currency": string (ISO 4217),
  "periodFrom": string (YYYY-MM-DD),
  "periodTo": string (YYYY-MM-DD),
  "transactions": [
    {
      "date": string (YYYY-MM-DD),
      "bookingDate": string | null,
      "amount": number (negative for debits, positive for credits),
      "currency": string,
      "description": string (raw, as on statement),
      "merchant": string | null
    }
  ]
}

Statement text:
<<<
{rawText}
>>>

Rules:
- Amounts are decimals. Polish format uses comma; convert to dot in output.
- Dates use European format (DD.MM.YYYY); convert to ISO.
- If a transaction is ambiguous, include it with description and amount; merchant can be null.
- Return ONLY valid JSON, no markdown fences, no commentary.
```

### Cost notes

A 20-page statement at ~3000 tokens input + ~1000 output = ~$0.02 USD on Sonnet 4.6. Acceptable for occasional use. Frequent unknown-bank usage is a signal to write a deterministic parser for that bank.

### Migration path: AI fallback → deterministic parser

When the user imports the same unknown bank 3+ times, app suggests:

> "You've imported MillenniumBank statements 3 times via AI fallback. Generate a deterministic parser? This is faster, free per-import, and more reliable."

Generation flow (manual, dev-side):
1. Collect 3+ real statements (anonymize via `tools/Anonymizer`)
2. Open Cursor with golden samples + AI fallback's JSON output as expected results
3. Ask Claude/Cursor to write a parser following `PkoBpStatementParser` as template
4. Run against golden files; iterate until all pass
5. Register in DI

## Deduplication

Hash for each transaction:

```csharp
public static string ComputeHash(ParsedTransaction tx, string accountNumber)
{
    var input = $"{accountNumber}|{tx.Date:yyyy-MM-dd}|{tx.Amount:F2}|{NormalizeDescription(tx.Description)}";
    var bytes = Encoding.UTF8.GetBytes(input);
    var hash = SHA256.HashData(bytes);
    return Convert.ToHexString(hash);
}
```

Unique index on `Transaction.Hash`. Re-importing the same statement is safe — duplicates skipped, `ImportSession.TransactionsSkipped` records how many.

## Testing — see also `docs/architecture/03b-parser-testing.md` (TBD)

- Every parser has golden file tests
- Anonymizer tool produces sanitized samples committed to repo
- Property-based tests for amount/date parsers
- Unit tests for normalization helpers
- See `tools/Anonymizer/` for the anonymization tool

## When a bank changes its layout

Symptoms:
- New imports show garbled descriptions
- Amounts off by orders of magnitude
- Some transactions missing entirely
- Parser confidence drops

Response:
1. Get a fresh statement from the affected bank
2. Add it as a new golden sample with anonymizer
3. Update parser to handle new layout (often a column-position adjustment)
4. Verify all old golden files still pass

This is a maintenance reality, not a bug.
