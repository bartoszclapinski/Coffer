# 04 — AI Strategy

## Three distinct uses of AI in this app

| Use case | Model | Why |
|---|---|---|
| Categorization (in batch) | Claude Haiku 4.5 / GPT-4o mini | Cheap, fast, "good enough" for label-from-description |
| Chat with data | Claude Sonnet 4.6 / GPT-4o | Reasoning over user history, tool calling |
| Receipt OCR (vision) | Claude Sonnet 4.6 vision | Quality matters, Polish receipts are messy |
| Anomaly commentary | Claude Sonnet 4.6 | Short, contextual explanations |
| Statement parsing fallback | Claude Sonnet 4.6 | Structured JSON output, multilingual |
| Risks/sugestions for goals | Claude Sonnet 4.6 | Context-rich reasoning |

## Provider abstraction

Use `Microsoft.Extensions.AI` to swap providers without touching call sites.

```csharp
public interface IAiProvider
{
    Task<string> CompleteAsync(AiRequest request, CancellationToken ct);
    Task<TResult> CompleteJsonAsync<TResult>(AiRequest request, CancellationToken ct);
    IAsyncEnumerable<string> StreamAsync(AiRequest request, CancellationToken ct);
}

public record AiRequest
{
    public required string Prompt { get; init; }
    public required string Model { get; init; }
    public string? SystemPrompt { get; init; }
    public IReadOnlyList<AiTool>? Tools { get; init; }
    public IReadOnlyList<AiAttachment>? Attachments { get; init; }    // for vision
    public int MaxTokens { get; init; } = 1000;
    public double Temperature { get; init; } = 0.3;
}
```

Two concrete implementations: `ClaudeProvider`, `OpenAiProvider`. Settings choose which is active per use case.

## Categorization — hybrid pipeline

Goal: assign a category to every transaction. Targets:

- 95%+ accuracy
- Cost: ~1–2 PLN for first import (2 years history), pennies thereafter
- Latency: invisible to user (runs in import background task)

### Three-stage pipeline

```
Transaction
  │
  ▼
[1] Cache lookup ── exact NormalizedDescription seen before? ──▶ HIT (~95%) → done
  │
  ▼
[2] Rule engine ── regex match against user rules? ──▶ MATCH → done, also cached
  │
  ▼
[3] AI batch ── unknown descriptions buffered to batch of 20–50 ──▶ category, cached
```

### Cache implementation

Backed by a simple table:

```csharp
public class CategoryCache
{
    public Guid Id { get; set; }
    public string NormalizedDescription { get; set; } = "";
    public Guid CategoryId { get; set; }
    public DateTime LastUsedAt { get; set; }
    public int HitCount { get; set; }
    public CacheSource Source { get; set; }  // Rule | AI | Manual
}

modelBuilder.Entity<CategoryCache>().HasIndex(c => c.NormalizedDescription).IsUnique();
```

User manual re-categorization writes back to cache with `Source = Manual` and overrides AI cache entries on next encounter. This is the learning loop.

### Rule engine

```csharp
public class RuleEngine
{
    public bool TryMatch(Transaction tx, IReadOnlyList<Rule> rules, out Guid categoryId)
    {
        foreach (var rule in rules.Where(r => r.IsEnabled).OrderBy(r => r.Priority))
        {
            if (Regex.IsMatch(tx.NormalizedDescription, rule.Pattern, RegexOptions.IgnoreCase))
            {
                categoryId = rule.CategoryId;
                return true;
            }
        }
        categoryId = default;
        return false;
    }
}
```

Examples of typical rules:
- `LIDL|BIEDRONKA|ŻABKA` → Spożywcze
- `ORLEN|SHELL|BP|CIRCLE\sK` → Paliwo
- `NETFLIX|SPOTIFY|ANTHROPIC|OPENAI` → Subskrypcje
- `MBANK.*KREDYT|PKO.*RATA` → Kredyt hipoteczny

### AI batch categorization

When unknown transactions accumulate, send a batch:

```
You are a Polish personal finance categorizer. Assign each transaction to exactly one category from the list below.

Categories: [Spożywcze, Paliwo, Restauracje, Subskrypcje, Edukacja, Rozrywka, Zdrowie, Transport, Mieszkanie, Ubrania, Kredyt hipoteczny, Inwestycje, Wpływy, Inne]

Return a JSON array of integers (0-based indexes into the categories array), in the same order as transactions.

Transactions:
1. "LIDL TORUN UL SZOSA LUBICKA" -127.40
2. "DEVMENTOR.PL KURS" -299.00
3. "ANTHROPIC PBC" -89.00
4. "KAUFLAND POL." -245.50
... up to 50

Return ONLY the JSON array, e.g.: [0, 4, 3, 0]
```

Cost per batch of 50: roughly 800 input + 100 output tokens on Haiku ≈ ~$0.0005. After cache warmup, batch calls happen rarely.

### Anonymization before AI

Even for categorization, sensitive fields are redacted:

```csharp
public class PromptAnonymizer
{
    public string Anonymize(string description)
    {
        var s = description;
        s = Regex.Replace(s, @"\b\d{20,26}\b", "[ACCOUNT]");        // account numbers
        s = Regex.Replace(s, @"\b\d{3}-\d{2}-\d{2}-\d{3}\b", "[NIP]");
        s = Regex.Replace(s, @"\bPL\d{26}\b", "[IBAN]");
        // Keep merchant names as-is — they're useful and not sensitive
        return s;
    }
}
```

Account numbers are useless to the model anyway (no signal in 26 random digits) and removing them protects user privacy.

## Chat with data — tool calling

Goal: user asks natural-language questions ("ile wydałem na paliwo?"), model figures out which queries to run.

### Why tool calling, not SQL generation

Generating SQL from LLM is unsafe (injection, full table access, slow on errors). Tool calling gives the model a fixed menu of safe operations.

### Available tools

```csharp
[Tool("Returns total spent in a date range, optionally filtered by category")]
public async Task<decimal> GetTotalSpent(DateOnly from, DateOnly to, string? category = null);

[Tool("Returns transactions matching criteria")]
public async Task<List<TransactionDto>> GetTransactions(
    DateOnly from, DateOnly to,
    string? merchantPattern = null,
    string? category = null,
    int limit = 50);

[Tool("Returns spending broken down by category for a period")]
public async Task<Dictionary<string, decimal>> GetSpendingByCategory(DateOnly from, DateOnly to);

[Tool("Returns monthly spending trend for a category")]
public async Task<List<MonthlyTrend>> GetMonthlyTrend(string category, int months);

[Tool("Returns detected anomalies in a period")]
public async Task<List<AnomalyDto>> FindAnomalies(DateOnly from, DateOnly to);

[Tool("Returns goals matching status filter")]
public async Task<List<GoalDto>> GetGoals(GoalStatus? status = null);

[Tool("Returns receipt items matched to a transaction")]
public async Task<List<ReceiptItemDto>> GetReceiptItems(Guid transactionId);
```

Tools are read-only. The chat model cannot mutate state — only the user can, through dedicated UI.

### Tool transparency in UI

Show the user which tools were called (collapsible panel under each AI response):

> 🔧 `GetSpendingByCategory(from=2025-11-01, to=2025-11-30, category="Paliwo")`

Builds trust and aids debugging. Users see that answers are derived from real queries, not hallucinated.

### System prompt

```
You are Coffer's financial assistant. You help the user understand their finances using the tools provided.

Rules:
- Use tools to answer questions; never invent numbers
- All amounts are PLN unless stated otherwise
- Today's date is {today_iso}
- Be concise; prefer 2-4 sentences over paragraphs
- For comparisons, mention the comparison period explicitly
- If the user asks for advice, briefly summarize facts then offer 1-2 specific suggestions tied to numbers
- Do not give legal, tax, or licensed investment advice
- Respond in {language} (default: Polish)
```

## Anomaly detection — statistics first, AI second

LLM is bad at math; statistics are good at math. Use stats for detection, LLM for explanation.

### Statistical detectors

```csharp
public interface IAnomalyDetector
{
    IAsyncEnumerable<AnomalyCandidate> DetectAsync(
        IReadOnlyList<Transaction> recent,
        IReadOnlyList<Transaction> baseline,
        CancellationToken ct);
}
```

Detectors implemented:
- **HighAmountInCategoryDetector** — z-score > 3 on amount within category
- **NewMerchantDetector** — merchant never seen before
- **CategorySpikeDetector** — current month total > 2σ above 6-month average
- **DuplicatePaymentDetector** — same merchant, same amount, within 24 hours
- **MissingRecurrenceDetector** — usual subscription didn't appear this month

Each returns `AnomalyCandidate { TransactionId, Type, Score, RawNumbers }` with no human-readable text.

### LLM commentary layer

For top candidates (top 5–10 per period), call LLM to generate user-facing description:

```
You are explaining financial anomalies to the user in Polish.

For each anomaly, write 1-2 sentences explaining what's unusual and why. Be specific: cite numbers and comparisons.

Anomaly: {type}
Transaction: {date}, {merchant}, {amount} PLN
Context:
- Category: {category}
- Z-score: {score}
- 6-month avg in category: {avgAmount}
- This month total in category: {monthTotal}

Output: a JSON object {"title": "...", "description": "..."}.
```

Cost: minimal (10 anomalies/month × ~$0.001 each).

## Vision — receipt OCR

See `05-receipt-pipeline.md` for the full pipeline. AI strategy details:

- **Model:** Claude Sonnet 4.6 with vision
- **Input:** preprocessed receipt photo (deskewed, cropped to receipt area)
- **Prompt:** structured extraction of merchant, date, time, line items, total, VAT
- **Cost:** ~5 grosze per receipt
- **Fallback:** if Claude rate-limited, switch to OpenAI gpt-4o vision

## Cost discipline

### Per-month budget

User sets a budget cap in settings (default 20 PLN). Enforcement:

```csharp
public class AiBudgetGate
{
    public async Task<bool> CanProceedAsync(decimal estimatedCostPln, AiPriority priority, CancellationToken ct)
    {
        var spent = await _ledger.GetCurrentMonthSpendAsync(ct);
        var cap = await _settings.GetMonthlyCapPlnAsync(ct);
        if (spent + estimatedCostPln > cap)
        {
            if (priority == AiPriority.Critical) return true;       // categorization during import
            await _notifier.NotifyBudgetExceededAsync(spent, cap, ct);
            return false;
        }
        return true;
    }
}
```

### Cost ledger

Every AI call writes to a ledger:

```csharp
public class AiUsageEntry
{
    public Guid Id { get; set; }
    public DateTime At { get; set; }
    public string Provider { get; set; } = "";          // Claude | OpenAI
    public string Model { get; set; } = "";
    public string Purpose { get; set; } = "";           // categorization | chat | vision | parser-fallback | anomaly-comment
    public int InputTokens { get; set; }
    public int OutputTokens { get; set; }
    public decimal EstimatedCostUsd { get; set; }
    public decimal EstimatedCostPln { get; set; }
}
```

UI in settings shows:
- Current month spend with breakdown by purpose
- 6-month trend
- Estimated cost per import

### Caching = cost control

Categorization cache is the single biggest cost saver. Same merchant string → same category → free. Push hard for cache hits:

- Normalize aggressively (uppercase, strip card suffixes, collapse whitespace)
- Cache keyed by normalized form
- Background job pre-warms cache from existing transactions on first launch after migration

### Prompt token efficiency

- Avoid long system prompts repeated per request; use prompt caching where supported (Anthropic prompt caching for Sonnet)
- Use compact JSON schemas, not verbose markdown in prompts
- For lists, use indexes not full objects when only a label is needed

## Failure handling

### Network errors

- Retry with exponential backoff: 1s, 2s, 4s, max 3 retries
- After 3 failures: queue the operation locally, show user a non-blocking "AI unavailable, queued" toast
- Categorization queue resumes when network is back

### Rate limit (429)

- Honor `Retry-After` header
- Switch provider if available (e.g., Claude rate-limited → try OpenAI for the same request)
- Never silently fail; log to Serilog with full request context (anonymized)

### Bad JSON output

- Use strict JSON mode where supported (`response_format: json_object` on OpenAI)
- For Claude: validate JSON, on parse failure do one retry with a "your previous response was not valid JSON, retry" addendum
- After 2 failures: log raw response for inspection, return error to caller, do NOT swallow silently

### Hallucinated tool args

- Validate all tool inputs against expected types
- For dates, parse and reject impossible ones (year 1900, year 9999)
- For amounts, reject negatives in contexts that expect positive
- Tool errors propagate back to model so it can correct on retry

## Logging AI traffic — for debugging

Every AI call is logged with:
- Timestamp, provider, model, purpose
- Token counts and cost
- Prompt hash (not raw prompt — too verbose)
- Response status (success / json-error / network-error / rate-limit)
- Latency

When debugging "why did the model do X", separate diagnostic mode (opt-in) saves full prompt and response to a local file for that session only.
