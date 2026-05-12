# 05 — Receipt Pipeline

## Goal

User photographs a receipt on mobile. AI extracts items. App later auto-matches the receipt to the corresponding bank transaction (when the statement is imported on desktop), splitting the transaction into per-item categories.

This is the only pipeline that originates on mobile and finishes on desktop.

## End-to-end flow

```
[MOBILE]
   ┌─────────────────────────────────────┐
   │ User taps camera button             │
   ▼                                     │
   MediaPicker.CapturePhotoAsync          │
   │                                     │
   ▼                                     │
   On-device preprocessing                 │
   (deskew, crop, contrast, downscale)    │
   │                                     │
   ▼                                     │
   Upload image bytes to Claude vision    │
   (single API call, multimodal prompt)   │
   │                                     │
   ▼                                     │
   Receipt JSON: merchant, date, items[]  │
   │                                     │
   ▼                                     │
   Per-item categorization                │
   (separate or chained AI call)          │
   │                                     │
   ▼                                     │
   Save Receipt + ReceiptItem rows        │
   Status = WaitingForTransaction         │
   │                                     │
   ▼                                     │
   Sync to Drive (event sourcing)         │
   └─────────────────────────────────────┘
                                          
[DESKTOP — later, when statement is imported]
   ┌─────────────────────────────────────┐
   │ Statement import pipeline runs       │
   ▼                                     │
   For each new Transaction:              │
       ReceiptMatcher.FindCandidatesAsync │
       │                                 │
       ▼                                 │
       Score = weighted(amount, date,    │
                        merchant fuzzy)   │
       │                                 │
       ▼                                 │
       If score >= 0.95 → auto-link      │
       Else if best score >= 0.6 →       │
            user resolves manually       │
       Else → no match, receipt stays    │
            in Unmatched bucket          │
   │                                     │
   ▼                                     │
   On link: Transaction.ReceiptId = ...   │
   Generate TransactionSplits per item    │
   └─────────────────────────────────────┘
```

## Domain model

```csharp
public enum ReceiptStatus
{
    Pending,            // captured, waiting for OCR
    Processed,          // OCR done, items extracted
    WaitingForTransaction,
    Matched,            // auto-matched to a transaction
    Unmatched,          // OCR done but no candidate found
    Failed              // OCR or AI failed
}

public class Receipt
{
    public Guid Id { get; set; }
    public string ImagePath { get; set; } = "";        // encrypted file in app storage
    public DateTime CapturedAt { get; set; }            // when photo taken (UTC)
    public DateOnly? ReceiptDate { get; set; }          // from receipt itself
    public TimeOnly? ReceiptTime { get; set; }
    public string? MerchantName { get; set; }           // raw, as printed
    public string? NormalizedMerchant { get; set; }     // for matching
    public decimal? TotalAmount { get; set; }
    public string Currency { get; set; } = "PLN";
    public string? RawJson { get; set; }                // full AI output for re-processing
    public ReceiptStatus Status { get; set; }
    public Guid? MatchedTransactionId { get; set; }
    public List<ReceiptItem> Items { get; set; } = [];
}

public class ReceiptItem
{
    public Guid Id { get; set; }
    public Guid ReceiptId { get; set; }
    public string Name { get; set; } = "";
    public decimal? Quantity { get; set; }
    public decimal? UnitPrice { get; set; }
    public decimal Amount { get; set; }                 // line total after discounts
    public Guid? CategoryId { get; set; }               // assigned by AI
    public bool CategoryConfirmed { get; set; }         // user confirmed or changed
    public int LineNumber { get; set; }                 // ordering on receipt
}
```

## Mobile capture (MAUI)

### Camera control

Use `MediaPicker.CapturePhotoAsync` for the photo, then a custom preview/crop overlay (MAUI's built-in capture is sufficient for v1; advanced cropping can be added later).

```csharp
public class MauiReceiptCamera : ICamera
{
    public async Task<CapturedImage?> CaptureReceiptAsync(CancellationToken ct)
    {
        if (!MediaPicker.Default.IsCaptureSupported) return null;
        var photo = await MediaPicker.Default.CapturePhotoAsync(new MediaPickerOptions
        {
            Title = "Sfotografuj paragon"
        });
        if (photo is null) return null;

        await using var stream = await photo.OpenReadAsync();
        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms, ct);
        return new CapturedImage(ms.ToArray(), photo.ContentType);
    }
}
```

### Preprocessing

Receipts photographed in the wild have:
- Skew (held at angle)
- Excess background (table, hand)
- Glare
- Variable lighting

Light preprocessing before sending to AI:
- **Auto-rotate** (EXIF orientation)
- **Downscale** to max 2048px on long side (vision API quality plateau, smaller payload)
- **Contrast boost** (mild — too aggressive loses thermal-print details)
- **Optional: edge detection + perspective correction** (use SkiaSharp for cropping; advanced CV not worth complexity for v1)

```csharp
public class ReceiptImagePreprocessor
{
    public byte[] Preprocess(byte[] input)
    {
        using var ms = new MemoryStream(input);
        using var bitmap = SKBitmap.Decode(ms);
        var resized = ResizeIfNeeded(bitmap, maxLongSide: 2048);
        // mild contrast enhancement omitted for brevity
        using var image = SKImage.FromBitmap(resized);
        using var data = image.Encode(SKEncodedImageFormat.Jpeg, quality: 85);
        return data.ToArray();
    }
}
```

Heavy processing (full deskew, perspective correction) deferred to later iteration if needed.

## Vision OCR — Claude Sonnet 4.6

### Prompt

```
You are analyzing a Polish retail receipt (paragon fiskalny).

Extract the following as JSON:
{
  "merchant": string,                     // store name as printed
  "merchantAddress": string | null,
  "date": string,                         // YYYY-MM-DD
  "time": string | null,                  // HH:MM
  "totalAmount": number,                  // PLN
  "currency": "PLN",
  "items": [
    {
      "name": string,                     // product as printed (Polish)
      "quantity": number | null,
      "unitPrice": number | null,
      "amount": number                    // line total
    }
  ]
}

Rules:
- Polish receipts use comma decimal; convert to dot.
- Skip non-product lines: VAT summaries, fiscal numbers, 'PARAGON FISKALNY' headers, 'PTU' codes.
- Group "RABAT" or "ZNIŻKA" lines into the preceding item's amount (apply discount, don't list separately).
- Multi-line item names: concatenate with space.
- If a value is unreadable, set it to null rather than guessing.
- Return ONLY valid JSON.
```

### API call (Claude SDK)

```csharp
public class ClaudeVisionReceiptOcr : IReceiptOcr
{
    public async Task<ReceiptOcrResult> ExtractAsync(byte[] imageBytes, string mediaType, CancellationToken ct)
    {
        var message = new MessageRequest
        {
            Model = "claude-sonnet-4-6",
            MaxTokens = 2000,
            Messages = [
                new Message
                {
                    Role = "user",
                    Content = [
                        new ImageContent { Source = new ImageSource { MediaType = mediaType, Data = Convert.ToBase64String(imageBytes) } },
                        new TextContent { Text = ReceiptPrompt }
                    ]
                }
            ]
        };

        var response = await _client.Messages.CreateAsync(message, ct);
        var json = ExtractText(response);
        var parsed = JsonSerializer.Deserialize<ReceiptOcrResult>(json)
                     ?? throw new InvalidOperationException("Empty receipt parse");
        await _ledger.RecordAsync("receipt-vision", response.Usage, ct);
        return parsed;
    }
}
```

### Per-item categorization

After OCR, a second call assigns a category to each item, using the user's category list:

```
You are categorizing items from a Polish receipt.

Categories available: [Spożywcze, Warzywa i owoce, Słodycze, Napoje, Chemia, Higiena osobista, Karmy dla zwierząt, Alkohol, Pieczywo, Mięso, Nabiał, Mrożonki, Inne]

For each item, return the category index (0-based).

Items:
1. CHLEB ŻYTNI 600G
2. MARS 50G
3. MARCHEW LUZ 1.2KG
4. SOK POMARAŃCZOWY 1L
5. PROSZEK PERSIL 2KG

Return ONLY a JSON array of integers, e.g.: [8, 2, 1, 3, 4]
```

These two calls can be combined into one if cost matters more than clarity. For v1, separate calls are easier to test and debug.

## Receipt matching

### Inputs

- Newly imported transactions (from a statement)
- Existing receipts with `Status = WaitingForTransaction`

### Scoring

For each (transaction, receipt) pair, compute a confidence score 0–1:

```csharp
public class ReceiptMatcher : IReceiptMatcher
{
    public ReceiptMatchScore Score(Transaction tx, Receipt receipt)
    {
        // Amount: tolerance ±0.5% to handle rounding
        if (receipt.TotalAmount is null) return ReceiptMatchScore.Zero;
        var amountTx = Math.Abs(tx.Amount);
        var amountRc = receipt.TotalAmount.Value;
        var amountDiff = Math.Abs(amountTx - amountRc) / Math.Max(amountTx, amountRc);
        if (amountDiff > 0.005m) return ReceiptMatchScore.Zero;     // hard reject

        // Date: within ±2 days (receipt date can be a day before bank booking)
        if (receipt.ReceiptDate is null) return ReceiptMatchScore.Zero;
        var dateDiff = Math.Abs((tx.Date.ToDateTime(default) - receipt.ReceiptDate.Value.ToDateTime(default)).Days);
        if (dateDiff > 2) return ReceiptMatchScore.Zero;
        var dateScore = 1.0 - dateDiff / 2.0;                       // 1.0 same day, 0.5 one day, 0.0 two days

        // Merchant: fuzzy compare normalized strings
        var normMerchant = NormalizeMerchant(receipt.MerchantName ?? "");
        var normDesc = tx.NormalizedDescription;
        var merchantScore = FuzzyMatch(normMerchant, normDesc);     // Jaro-Winkler in [0,1]

        // Weighted aggregation
        var score = 0.6 * (1.0 - amountDiff * 200.0)                // amount diff scaled
                  + 0.2 * dateScore
                  + 0.2 * merchantScore;
        return new ReceiptMatchScore(Math.Min(1.0, score), Reasons: [...]);
    }
}
```

### Decision

- **score ≥ 0.95** → auto-link, log decision, notify user (optional toast)
- **0.60 ≤ score < 0.95** → add to "needs review" queue, user resolves in UI
- **score < 0.60** → no match this round, receipt stays in `WaitingForTransaction` (will re-evaluate on next import)

### Edge cases

- **Receipt without explicit total (rare):** matcher cannot score amount → defer to user
- **One transaction, multiple matching receipts:** rare but possible if user pays for two purchases on one card swipe (unusual). Pick highest score, flag rest for user review.
- **Receipt OCR misread amount:** receipt total is wrong, real transaction amount is right → user can manually link by clicking transaction → "attach receipt" → pick from unmatched receipts
- **Receipt with no date (faded thermal print):** matcher uses capture date as fallback with reduced confidence

## Generating splits from receipt

When a transaction is linked to a receipt, generate `TransactionSplit` per `ReceiptItem`:

```csharp
public class SplitGenerator
{
    public List<TransactionSplit> GenerateFromReceipt(Transaction tx, Receipt receipt)
    {
        var splits = receipt.Items.Select(item => new TransactionSplit
        {
            Id = Guid.NewGuid(),
            TransactionId = tx.Id,
            ReceiptItemId = item.Id,
            Description = item.Name,
            Amount = -item.Amount,                      // negative = expense
            CategoryId = item.CategoryId
        }).ToList();

        // Reconciliation: sum of splits should equal transaction amount
        var sumSplits = splits.Sum(s => s.Amount);
        var diff = tx.Amount - sumSplits;
        if (Math.Abs(diff) > 0.05m)
        {
            splits.Add(new TransactionSplit
            {
                Id = Guid.NewGuid(),
                TransactionId = tx.Id,
                Description = "Korekta (zaokrąglenia/rabat ogólny)",
                Amount = diff,
                CategoryId = null
            });
        }

        return splits;
    }
}
```

## UI — desktop drill-down

In the transactions list, a transaction with `ReceiptId` shows a small receipt icon. Clicking expands an inline panel:

```
−127,40 zł  LIDL Toruń  [📄 paragon]
Spożywcze  ·  28.11
                    │
                    ▼
                ┌──────────────────────────────────────────┐
                │ Pozycje z paragonu (28.11 14:32)          │
                ├──────────────────────────────────────────┤
                │ Chleb żytni 600g       6,50 zł  Pieczywo  │
                │ Mleko 3,2% 1L          4,20 zł  Nabiał    │
                │ Marchew luz 1.2kg      4,80 zł  Warzywa   │
                │ Mars 50g               2,49 zł  Słodycze  │
                │ ... (23 więcej)                           │
                │                                            │
                │ [Zmień kategorię pozycji] [Rozłącz paragon] │
                └──────────────────────────────────────────┘
```

Per-item recategorization writes back to `ReceiptItem.CategoryId` and to user's rule set (offer: "Always categorize 'MARS' as 'Słodycze'?").

## UI — mobile

Bottom-tab "Receipts" or FAB on home:

- **Capture** flow: camera → preview → "AI analyzes..." spinner → confirmation screen with extracted items
- **Edit before save:** user can fix mis-OCR'd item names or amounts
- **List view:** all receipts with status badges (Waiting / Matched / Unmatched)
- **Match flow when bank import done:** push notification "Twój paragon z Lidla został powiązany z transakcją"

## Receipt image storage

- Local: encrypted file in app storage. The DB encryption key is reused for receipt files (AES-GCM with random IV per file).
- Sync: image NOT synced through Drive event log (too large). Instead:
  - Compressed JPEG uploaded once to Drive folder `Coffer/Receipts/`
  - Encrypted with same DEK (so even Drive can't read)
  - File name: `{receipt-guid}.enc`
  - Other devices download on demand when viewing that receipt

This keeps sync events small (~1KB JSON) while still making images available everywhere.

## Failure modes

| Failure | Handling |
|---|---|
| Camera permission denied | Friendly explanation, link to settings |
| Vision API down | Save image with `Status = Pending`; retry on next app open |
| Parsed JSON invalid | One retry, then `Status = Failed`; user can re-trigger or delete |
| Total mismatch (sum of items ≠ total) | Soft warning, save anyway, user reviews |
| Match auto-linked to wrong transaction | User clicks "Rozłącz" → splits removed, both items return to "Waiting" / "Unmatched" |
| Duplicate receipt photographed | Detect via image hash before AI call; offer "this looks like an existing receipt, view existing?" |
