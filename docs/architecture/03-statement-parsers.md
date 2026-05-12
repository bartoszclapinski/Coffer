# 03 — Statement Parsers

## The problem

Every Polish bank exports PDF statements in a different layout. Even a single bank has multiple layout variants (checking account vs credit card vs savings vs foreign currency). The app must:

1. Detect which bank produced the PDF
2. Route to a bank-specific parser
3. Fall back gracefully when no parser exists yet
4. Be testable and stable across bank-side layout changes

## Architecture: registry + strategy + AI fallback

```
PDF input
   │
   ▼
BankDetector ── reads first page, matches fingerprints ──▶ BankFingerprint
   │
   ▼
StatementParserRegistry.Resolve(fingerprint) ──▶ IStatementParser
   │
   ├── PkoBpStatementParser
   ├── MBankStatementParser
   ├── IngStatementParser
   ├── ... (one per supported bank)
   └── AiAssistedParser (fallback when no specific parser)
   │
   ▼
ParseResult { Account, Period, Transactions[], Source }
   │
   ▼
Normalization → Deduplication → Persistence
```

## Interfaces

```csharp
public interface IBankDetector
{
    BankFingerprint? Detect(PdfDocument doc);
}

public record BankFingerprint(string BankCode, string BankName, int Priority);

public interface IStatementParser
{
    string BankCode { get; }
    bool CanHandle(BankFingerprint fingerprint);
    Task<ParseResult> ParseAsync(PdfDocument doc, CancellationToken ct);
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

## PKO BP parser specifics (primary bank)

PKO has at least three layouts seen in practice:
1. Standard checking account ("Wyciąg z rachunku")
2. Credit card statement ("Wyciąg z karty kredytowej")
3. Savings account ("Wyciąg z konta oszczędnościowego")
4. Foreign currency account ("Wyciąg z rachunku walutowego")

Each has slightly different column positions and headers. Strategy: detect sub-layout from header keywords on page 1, then dispatch to a specific column-position table.

```csharp
public class PkoBpStatementParser : IStatementParser
{
    public string BankCode => "PKO_BP";

    public bool CanHandle(BankFingerprint fp) => fp.BankCode == BankCode;

    public async Task<ParseResult> ParseAsync(PdfDocument doc, CancellationToken ct)
    {
        var layout = DetectLayout(doc);                 // checking | creditCard | savings | foreignCurrency
        var (account, currency, period) = ExtractHeader(doc, layout);
        var transactions = ExtractTransactions(doc, layout, ct);
        return new ParseResult
        {
            BankCode = BankCode,
            AccountNumber = account,
            Currency = currency,
            PeriodFrom = period.From,
            PeriodTo = period.To,
            Transactions = transactions,
            Confidence = ParserConfidence.High
        };
    }
}
```

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
