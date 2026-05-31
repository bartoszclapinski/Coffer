# Sprint 7 — Parsing foundations + PKO BP standard checking

**Phase:** 1 (Statement parser for PKO BP — opener)
**Status:** Closed (2026-05-31)
**Depends on:** sprint-0 (project layout), sprint-4 (`Transaction` schema in `_SchemaInfo`-versioned DB; Sprint 8's import flow will persist parsed transactions through it)

> **Closure note (2026-05-31):** foundations + the deterministic PKO PDF parser shipped via PR #53.
> Manual verification (step K) revealed the freely-available PKO export is **"Historia rachunku"**
> (CSV/PDF/XML), **not** the paid **"Wyciąg z rachunku"** this parser targets — so the PKO **PDF**
> parser is speculative (synthetic-verified only). **Sprint 8 pivots PKO parsing to the
> "Historia rachunku" CSV export** and generalises `IStatementParser` off `PdfDocument`. See
> [log.md](log.md) for the CSV schema and the full finding. Foundations (DTOs, Polish helpers,
> registry, dedup hash) carry over to the CSV path unchanged.

## Goal

A deterministic parser for PKO BP standard checking statements that runs in CI against a synthetic PKO-shaped PDF, plus the surrounding infrastructure — Polish-format helpers, fingerprint detector, registry, dedup hash, property-based tests — so Sprint 8 can drop in the AI fallback and the Anonymizer without rebuilding any plumbing. Manual verification confirms the parser handles a real PKO PDF dropped into a gitignored fixture folder; no real statements are committed (hard rule #5).

## Strategy

Sprint 7 is foundation-heavy by intent. The roadmap's Phase 1 lists nine bullets across parser interfaces, helpers, PKO parser, Anonymizer, AI fallback, golden file tests, dedup, and property tests — too much for one sprint. The cut:

- **Sprint 7 (this one) — deterministic + foundations.** Interfaces, Polish helpers, PdfPig letter grouping, fingerprint detector for 7-8 banks (only PKO has a parser today), `PkoBpStatementParser` for the standard checking layout, `TransactionHash`, property-based and unit tests. CI exercises the parser against a QuestPDF-generated synthetic PKO statement; the real-statement smoke test is a manual step against a gitignored fixture.
- **Sprint 8 (next) — AI fallback + Anonymizer + remaining PKO layouts + golden files.** Anthropic SDK integration, `PromptAnonymizer` (hard rule #7), cost tracking primitives (`AiUsageEntry`, `AiBudgetGate`), Anonymizer CLI that produces committed anonymized samples, three more PKO layouts (credit card / savings / foreign currency), and the 5+ golden-file tests the roadmap calls for.

This split is deliberate — the AI fallback alone is ~25-30 sprint steps once you factor in prompt building, JSON-mode parsing, cost tracking, and the `tools/Anonymizer` CLI that has to exist before any committed golden samples are possible. Bundling it with parser foundations would make both halves brittle.

Concrete strategic calls inside the sprint:

- **DTOs in `Coffer.Shared`, interfaces in `Coffer.Core`** per the CLAUDE.md project layout. `ParseResult`/`ParsedTransaction` cross every layer (parsers produce them, future `ImportStatementUseCase` consumes them, persistence maps them onto `Transaction` entities) — that is exactly the Shared role. `IStatementParser`/`IBankDetector` are domain contracts and stay in Core.
- **Polish-format helpers as static methods, not types.** `PolishAmountParser.ParseDecimal(string)` etc. — no state, no DI, easy to unit-test and property-test.
- **`PdfLetterGrouping` exposes `GroupIntoRows` and a small `RowExtensions` set** (e.g. `TextAt(double xMin, double xMax)`) so individual parsers stay slim.
- **`FingerprintBankDetector` is data + code, not DI-heavy.** Fingerprints live in a static readonly array, scoring is `OrderByDescending(Priority).FirstOrDefault(matching)`. Adding a new bank in Sprint 8+ is one array entry; no DI churn.
- **`StatementParserRegistry` resolves by `BankCode`.** If no parser is registered for a detected bank, throws `UnsupportedBankException(string bankCode)`. Sprint 8 swaps the throw for an `AiAssistedParser` lookup at the registry level — no callsite changes.
- **`PkoBpStatementParser` only handles "Wyciąg z rachunku".** Other layouts (credit card / savings / foreign currency) throw `UnsupportedPkoLayoutException` for now. Sprint 8 adds them.
- **`TransactionHash` is pure `Coffer.Core/Domain/` code** — SHA-256 over `accountNumber|date|amount|normalizedDescription`. Same constant in every layer; no infra deps.
- **Synthetic test PDFs via QuestPDF.** PdfPig is read-only, so for CI we generate a PKO-shaped PDF programmatically (one or two header layouts, ~15 fake transactions). Real-statement coverage is manual (gitignored `tests/.local-fixtures/`). Sprint 8's Anonymizer replaces the synthetic with anonymized real-shape fixtures.
- **Property-based tests via FsCheck.** Amount parser round-trips through `decimal` for arbitrary `decimal` values; date parser round-trips through `DateOnly`. Catches the Polish-format edge cases that hand-written tests miss.

Three PRs in the established workflow:
1. **Plan** (`chore/plan-sprint-7`, this document) — issue first
2. **Implementation** (`feature/sprint-7-parsing-foundations`, new issue) — code + tests
3. **Closure** (`chore/close-sprint-7`, new issue) — post-merge bookkeeping

## Steps

### A. NuGet packages

- [x] 7.1 `Coffer.Infrastructure` — add `PdfPig` (`0.1.*` or latest stable). License: Apache 2.0 per CLAUDE.md tech-stack note.
- [x] 7.2 `Coffer.Infrastructure.Tests` — add `FsCheck.Xunit` (`3.*`) for property-based tests.
- [x] 7.3 `Coffer.Infrastructure.Tests` — add `QuestPDF` (`2024.*`) **with community license attribute** in the test project (QuestPDF is free for personal/small-team use; the attribute satisfies the license check). Used to generate synthetic PKO PDFs for CI tests.

### B. Core contracts (`Coffer.Core/Parsing/`)

- [x] 7.4 `BankFingerprint` record — `(string BankCode, string BankName, int Priority)`. Polish bank names go through XML doc, codes stay ASCII.
- [x] 7.5 `ParserConfidence` enum — `High | Medium | Low`. Deterministic parsers return `High`; the future AI fallback returns `Medium`.
- [x] 7.6 `IBankDetector` — `BankFingerprint? Detect(UglyToad.PdfPig.PdfDocument doc)`. PdfPig type leaks into the interface; acceptable because every detector implementation operates on the same PDF model, and `Coffer.Core` already has zero ban on third-party value types (the ban is on UI/framework deps per hard rule #3).
- [x] 7.7 `IStatementParser` — `BankCode`, `CanHandle(BankFingerprint)`, `Task<ParseResult> ParseAsync(PdfDocument doc, CancellationToken ct)`.
- [x] 7.8 `UnsupportedBankException(string bankCode)` and `UnsupportedPkoLayoutException(string layoutHint)`. Both sealed; carry just the identifier so logs cannot leak statement content.

### C. Shared DTOs (`Coffer.Shared/Parsing/`)

- [x] 7.9 `ParsedTransaction` — `Date` (DateOnly), `BookingDate` (DateOnly?), `Amount` (decimal), `Currency` (string), `Description` (string raw), `Merchant` (string?). Per hard rule #1 + #9 — decimal money, currency always present, dates are `DateOnly`.
- [x] 7.10 `ParseResult` — `BankCode`, `AccountNumber` (normalized), `Currency`, `PeriodFrom` / `PeriodTo` (DateOnly), `Transactions` (IReadOnlyList), `Confidence`, `Warnings` (IReadOnlyList<string>).

### D. Polish-format helpers (`Coffer.Infrastructure/Parsing/Polish/`)

- [x] 7.11 `PolishAmountParser.TryParse(string raw, out decimal value)` and `Parse(string raw)`. Strips ` ` (NBSP), regular spaces, `zł`, `PLN`, swaps comma for dot, parses with `CultureInfo.InvariantCulture`. Handles negatives (leading `-` or trailing `-`, both seen on Polish statements).
- [x] 7.12 `PolishDateParser.Parse(string raw)` returning `DateOnly`. Accepts `dd.MM.yyyy`, `dd-MM-yyyy`, `yyyy-MM-dd`.
- [x] 7.13 `DescriptionNormalizer.Normalize(string raw)` — collapses whitespace, strips card-number suffixes (`/****1234/`, `**1234`), removes `/PL/` / `/EU/` country codes, strips `BLIK`/`KRD` prefixes, uppercases. Used both for `NormalizedDescription` column and for `TransactionHash` input.
- [x] 7.14 `AccountNumberNormalizer.Normalize(string raw)` — strips spaces and dashes, uppercases, preserves the country prefix (`PL61109010140000071219812874`).
- [x] 7.15 `PdfLetterGrouping.GroupIntoRows(IReadOnlyList<Letter> letters, double yTolerance = 2.0)` per the docs/03 example. Returns `IEnumerable<IReadOnlyList<Letter>>` sorted top-to-bottom; each row is left-to-right.
- [x] 7.16 `PdfRowExtensions.TextAt(this IReadOnlyList<Letter> row, double xMin, double xMax)` — concatenates `letter.Value` for letters in the X band. Used by `PkoBpStatementParser` to pull date / description / amount columns.

### E. Bank detection (`Coffer.Infrastructure/Parsing/`)

- [x] 7.17 `FingerprintBankDetector : IBankDetector`. Static-readonly array of 7-8 `BankFingerprint` entries (PKO BP, ING, mBank, Pekao, Santander, Millennium, Citi, Alior). Reads first page text via `doc.GetPage(1).Text`, matches case-insensitively, returns highest-priority match or `null`.
- [x] 7.18 Register `IBankDetector` as Singleton via a new `AddCofferParsing` DI extension (registers detector + registry + the PKO parser).

### F. PKO BP parser (`Coffer.Infrastructure/Parsing/Pko/`)

- [x] 7.19 `PkoStandardCheckingHeader` — small static helper that extracts (account number, currency, period from/to) from the header rows of a "Wyciąg z rachunku" statement. Column-position constants captured at the top of the file with comments mapping each X coordinate to the column it represents.
- [x] 7.20 `PkoTransactionRowParser` — turns one grouped row (or a row-plus-continuation-rows block) into a `ParsedTransaction`. Handles the multi-line description case from docs/03 — when the next row has no date/amount columns but has description-region text, concatenate with a single space.
- [x] 7.21 `PkoBpStatementParser : IStatementParser` — composes the above. `CanHandle` returns true only for `BankCode == "PKO_BP"`. Inside `ParseAsync`:
  1. Detect layout from page-1 header keywords. If not "Wyciąg z rachunku", throw `UnsupportedPkoLayoutException`.
  2. Extract header (account, currency, period).
  3. Walk pages, group letters into rows, identify transaction rows by "row has a date in the date column and an amount in the amount column".
  4. For each transaction row, look ahead for continuation rows (next row has no date / no amount, but has description-region text).
  5. Build `ParsedTransaction`. Sign convention: debits negative, credits positive (PKO uses separate columns; signs are determined by which column has the amount).
  6. Return `ParseResult` with `Confidence = High`.

### G. Registry (`Coffer.Infrastructure/Parsing/`)

- [x] 7.22 `StatementParserRegistry` — constructor takes `IEnumerable<IStatementParser>` resolved by DI; builds `Dictionary<string, IStatementParser>` keyed on `BankCode`. `Resolve(BankFingerprint? fp)` throws `UnsupportedBankException` when the fingerprint is null or unrecognised — Sprint 8 swaps the throw for the AI fallback resolution.

### H. Transaction dedup hash (`Coffer.Core/Domain/`)

- [x] 7.23 `TransactionHash.Compute(string accountNumber, DateOnly date, decimal amount, string normalizedDescription)` returns uppercase hex SHA-256 over `accountNumber|date(yyyy-MM-dd)|amount(F2 invariant)|normalizedDescription`. Pure static. The Sprint-8 import flow uses this to dedup re-imports of the same statement.

### I. DI wiring

- [x] 7.24 `AddCofferParsing(this IServiceCollection)` extension on `Coffer.Infrastructure/DependencyInjection/`:
  - `IBankDetector` → `FingerprintBankDetector` (Singleton)
  - All `IStatementParser` implementations as Singletons (Sprint 7 has one: PKO BP)
  - `StatementParserRegistry` as Singleton — constructor pulls `IEnumerable<IStatementParser>` automatically
- [x] 7.25 Plug `AddCofferParsing` into `AddCofferInfrastructure`.

### J. Tests (Coffer.Infrastructure.Tests)

- [x] 7.26 `PolishAmountParserTests`:
  - `Parse_KnownPositive_ReturnsExpected` (a few hand-picked PKO sample strings)
  - `Parse_KnownNegative_ReturnsExpected` (both leading and trailing minus variants)
  - `Parse_HandlesNonBreakingSpace_AsThousandsSeparator`
  - `Parse_RejectsGarbage_ThrowsFormatException`
  - **Property:** `decimal → format → ParseDecimal → original` round-trip for arbitrary `decimal` values (FsCheck)
- [x] 7.27 `PolishDateParserTests`:
  - One-each for the three accepted formats
  - `Parse_RejectsAmbiguous_ThrowsFormatException` (e.g. `02-13-2025` — 13 is not a month)
  - **Property:** `DateOnly → "dd.MM.yyyy" → Parse → original` round-trip
- [x] 7.28 `DescriptionNormalizerTests` — table-driven (one Polish-statement-derived raw → expected normalized) for ~6 cases: trailing card number, country code, BLIK prefix, multi-space, mixed-case, multi-line concatenated input.
- [x] 7.29 `AccountNumberNormalizerTests` — IBAN with spaces, IBAN with dashes, IBAN with mixed case.
- [x] 7.30 `PdfLetterGroupingTests` — fabricate `Letter`-shaped value objects (small private record + adapter), assert grouping handles single-row, multi-row, tight-row (Y within tolerance), and split-row (Y just outside tolerance).
- [x] 7.31 `FingerprintBankDetectorTests`:
  - `Detect_PkoBpStatement_ReturnsPkoFingerprint` (synthetic page text containing "PKO Bank Polski")
  - `Detect_UnknownText_ReturnsNull`
  - `Detect_EmptyDocument_ReturnsNull`
  - `Detect_CaseInsensitive_StillMatches`
- [x] 7.32 `StatementParserRegistryTests`:
  - `Resolve_PkoFingerprint_ReturnsPkoParser`
  - `Resolve_UnknownFingerprint_Throws`
  - `Resolve_NullFingerprint_Throws`
- [x] 7.33 `PkoBpStatementParserTests` — generate a synthetic PKO-shaped PDF with QuestPDF that contains:
  - Header with bank name, account number (a fake `PL61 1090 1014 0000 0712 1981 2874`), currency, period
  - 10-15 transaction rows with mixed debits/credits, multi-line descriptions on some, NBSP-separated thousands on amounts
  - Test: `Parse_SyntheticChecking_ReturnsAllTransactions` — asserts count, period, account; spot-checks 2-3 specific transactions (date, amount, normalized description).
  - Test: `Parse_NonCheckingLayout_ThrowsUnsupportedPkoLayoutException`
- [x] 7.34 `TransactionHashTests`:
  - `Compute_ForSameInputs_IsStable`
  - `Compute_ChangingAnyComponent_ChangesHash`
  - `Compute_NormalizationApplied_DifferentRawSameNormalizedYieldsSameHash`

### K. Manual verification

- [x] 7.35 Drop a real PKO BP "Wyciąg z rachunku" PDF into `tests/.local-fixtures/pko-checking.real.pdf` (gitignored — hard rule #5). Verify `.gitignore` already covers `*.real.pdf` per CLAUDE.md hard rule #5; add if missing.
- [x] 7.36 Run a small console harness (a `dotnet run --project tests/Coffer.Infrastructure.Tests -- verify-pko <path>` or a `[Trait("Category","ManualOnly")]` test invoked via `dotnet test --filter`) that parses the real PDF and prints `ParseResult` to stdout. Eyeball the output: account number matches the real one (locally only — do NOT paste it into commits), period correct, transactions sane.
- [x] 7.37 Confirm `tests/.local-fixtures/` is gitignored and the file does NOT show in `git status`.

### L. Validation and merge

- [x] 7.38 `dotnet build` + `dotnet test` + `dotnet format --verify-no-changes` green locally
- [x] 7.39 `gh issue create` for implementation — title `feat(sprint-7): parsing foundations + PKO BP standard checking`, labels `feat` + `sprint-7`
- [x] 7.40 Commit on `feature/sprint-7-parsing-foundations`, push, `gh pr create` with `Closes #<impl-issue>`
- [x] 7.41 CI green, squash-merge, branch deleted
- [x] 7.42 `gh issue create` for closure → separate `chore/close-sprint-7` PR

## Definition of Done

1. `IStatementParser` / `IBankDetector` / `BankFingerprint` / `ParserConfidence` / `UnsupportedBankException` in `Coffer.Core/Parsing/` and `UnsupportedPkoLayoutException` in `Coffer.Infrastructure/Parsing/Pko/`.
2. `ParseResult` + `ParsedTransaction` in `Coffer.Shared/Parsing/`.
3. Five Polish-format helpers in `Coffer.Infrastructure/Parsing/Polish/` (`PolishAmountParser`, `PolishDateParser`, `DescriptionNormalizer`, `AccountNumberNormalizer`, `PdfLetterGrouping` / `PdfRowExtensions`).
4. `FingerprintBankDetector` with at least 7 fingerprints (PKO BP + 6 others).
5. `StatementParserRegistry` with PKO parser registered; unknown banks throw `UnsupportedBankException`.
6. `PkoBpStatementParser` handles the standard checking layout; other PKO layouts throw `UnsupportedPkoLayoutException`.
7. `TransactionHash.Compute` in `Coffer.Core/Domain/`.
8. **~30-35 new tests pass** locally and on CI Ubuntu, including property-based amount/date round-trips and a QuestPDF-generated synthetic PKO checking PDF.
9. Manual verification against a gitignored real PKO PDF: nothing leaked into git history (✓). **Outcome correction:** the only freely-available real export is "Historia rachunku", not the "Wyciąg z rachunku" this parser targets, so the parser correctly threw `UnsupportedPkoLayoutException` rather than producing output. This is the intended layout-gate behaviour, not a parser failure — but it means real-data verification of PKO parsing moves to Sprint 8 against the CSV export. See [log.md](log.md).
10. `Coffer.Core` and `Coffer.Shared` stay free of `Avalonia`, `CommunityToolkit.Mvvm`, `Anthropic.SDK`, Win32 references. `Coffer.Core` is allowed to reference `UglyToad.PdfPig` (read-only PDF value model) via `IBankDetector` — see Open Questions.

## Files affected

**New:**
- `src/Coffer.Core/Parsing/IStatementParser.cs`
- `src/Coffer.Core/Parsing/IBankDetector.cs`
- `src/Coffer.Core/Parsing/BankFingerprint.cs`
- `src/Coffer.Core/Parsing/ParserConfidence.cs`
- `src/Coffer.Core/Parsing/UnsupportedBankException.cs`
- `src/Coffer.Core/Domain/TransactionHash.cs`
- `src/Coffer.Shared/Parsing/ParseResult.cs`
- `src/Coffer.Shared/Parsing/ParsedTransaction.cs`
- `src/Coffer.Infrastructure/Parsing/Polish/PolishAmountParser.cs`
- `src/Coffer.Infrastructure/Parsing/Polish/PolishDateParser.cs`
- `src/Coffer.Infrastructure/Parsing/Polish/DescriptionNormalizer.cs`
- `src/Coffer.Infrastructure/Parsing/Polish/AccountNumberNormalizer.cs`
- `src/Coffer.Infrastructure/Parsing/Polish/PdfLetterGrouping.cs` (+ `PdfRowExtensions`)
- `src/Coffer.Infrastructure/Parsing/FingerprintBankDetector.cs`
- `src/Coffer.Infrastructure/Parsing/StatementParserRegistry.cs`
- `src/Coffer.Infrastructure/Parsing/Pko/PkoBpStatementParser.cs`
- `src/Coffer.Infrastructure/Parsing/Pko/PkoStandardCheckingHeader.cs`
- `src/Coffer.Infrastructure/Parsing/Pko/PkoTransactionRowParser.cs`
- `src/Coffer.Infrastructure/Parsing/Pko/UnsupportedPkoLayoutException.cs`
- `tests/Coffer.Infrastructure.Tests/Parsing/Polish/*` (5 test files)
- `tests/Coffer.Infrastructure.Tests/Parsing/FingerprintBankDetectorTests.cs`
- `tests/Coffer.Infrastructure.Tests/Parsing/StatementParserRegistryTests.cs`
- `tests/Coffer.Infrastructure.Tests/Parsing/Pko/PkoBpStatementParserTests.cs`
- `tests/Coffer.Infrastructure.Tests/Parsing/Pko/SyntheticPkoPdfBuilder.cs` (test fixture)
- `tests/Coffer.Core.Tests/Domain/TransactionHashTests.cs`

**Modified:**
- `src/Coffer.Infrastructure/Coffer.Infrastructure.csproj` — add `PdfPig`
- `tests/Coffer.Infrastructure.Tests/Coffer.Infrastructure.Tests.csproj` — add `FsCheck.Xunit`, `QuestPDF`
- `src/Coffer.Infrastructure/DependencyInjection/ServiceRegistration.cs` — `AddCofferParsing` + plug into `AddCofferInfrastructure`
- `.gitignore` — confirm `*.real.pdf` is covered; add `tests/.local-fixtures/` if not
- `.ai/sprints/sprint-7/sprint-7.md` — checkboxes, status
- `.ai/sprints/sprint-7/log.md` — progress
- `.ai/sprints/index.md` — status

## Open questions

1. **`UglyToad.PdfPig` in `Coffer.Core` via `IBankDetector` — does this violate hard rule #3?**
   - Hard rule #3: "`Coffer.Core` has zero references to UI frameworks. No `using Avalonia.*`, no `using Microsoft.Maui.*`, no `using System.Windows.*`."
   - PdfPig is a read-only PDF parsing library, not a UI framework. The rule is specifically about UI frameworks; PdfPig is closer to a value-object library (the contract operates on `PdfDocument` as a parameter type, not as a `Coffer.Core` runtime dependency in the heavy sense).
   - Alternative: define a `Coffer.Core` abstraction `IStatementDocument` that wraps `PdfDocument`; every parser receives the abstraction; `Coffer.Infrastructure` provides the concrete wrapper. Heavier; the abstraction would expose pages and letters anyway.
   - **Recommendation:** allow `UglyToad.PdfPig` in `Coffer.Core/Parsing/IBankDetector.cs` and `IStatementParser.cs`. Add a one-line note to CLAUDE.md hard rule #3 explicitly carving out "read-only data libraries" alongside "UI frameworks". The cleaner abstraction (`IStatementDocument`) can be a Sprint-9 chore once we see whether two PDF libraries ever coexist (extremely unlikely).

2. **`ParseResult` / `ParsedTransaction` in `Coffer.Shared` or `Coffer.Core`?**
   - `Coffer.Shared` per the project layout in CLAUDE.md: "DTOs and primitives used across all layers." `ParseResult` is exactly that — parsers produce it (Infrastructure), the future `ImportStatementUseCase` consumes it (Application), persistence maps from it (Infrastructure).
   - `Coffer.Core` is for domain entities and interfaces. `ParseResult` is not a domain entity — it's a transport DTO.
   - **Recommendation:** `Coffer.Shared/Parsing/`. The records have no methods, just data.

3. **`TransactionHash` — what exactly goes into the digest?**
   - Need a hash that survives benign description normalization (trimming, case, card-number suffix stripping) so re-imports of the same statement don't double-insert.
   - Need a hash that is sensitive enough to distinguish a same-day same-amount but different-merchant pair (which does happen — two coffee runs at the same price).
   - **Recommendation:** `SHA-256(accountNumber | date(yyyy-MM-dd) | amount(F2 invariant) | normalizedDescription)`. Account number anchors the hash to one account; date+amount catches most uniqueness; normalized description handles the genuine same-day same-amount pair. Edge case: two identical transactions in one statement (e.g. two BLIK payments to the same merchant for the same amount within the same day) hash identically and get deduped — acceptable for v1; future enhancement can append a per-statement sequence number if real loss is observed.

4. **Property-based test framework — `FsCheck.Xunit` v3 or stick with `2.x`?**
   - v3 is current, has better `[Property]` attribute integration with xUnit 2/3.
   - **Recommendation:** `FsCheck.Xunit` `3.*`. Six lines of test code per property; no churn risk.

5. **`QuestPDF` license in CI?**
   - QuestPDF requires the community license attribute for "free for personal/small-team use" — the attribute is a one-time setup in `AssemblyInfo` or via `QuestPDF.Settings.License = LicenseType.Community;` in test bootstrap.
   - **Recommendation:** set `LicenseType.Community` in a test-collection-fixture or `ModuleInitializer` once. Documented in the test file header.

6. **Synthetic PDF fidelity — how close to real PKO does it need to be?**
   - Sprint 7's CI test is "can the parser walk a PKO-shaped layout" — column positions roughly match real, header has "Wyciąg z rachunku" and a fake IBAN, ~15 transaction rows. Pixel-perfect emulation is wasted effort because Sprint 8's Anonymizer will replace this fixture with anonymized real samples anyway.
   - **Recommendation:** "shape over fidelity" — column X coords match real PKO PDFs within a few points, but row counts and exact text content are synthetic. Document the layout assumptions inline next to the column-position constants.

7. **Manual-verification harness — separate executable, or a Trait-tagged xUnit test?**
   - Separate executable means another project to maintain. Trait-tagged test means `dotnet test --filter "Category=ManualOnly"` runs it; same project, no extra plumbing.
   - **Recommendation:** Trait-tagged test (`[Trait("Category","ManualOnly")]`) in the existing `Coffer.Infrastructure.Tests`. Reads the gitignored fixture if present, prints `ParseResult` via xUnit `ITestOutputHelper`. CI skips via filter.

8. **`UnsupportedPkoLayoutException` placement — Core or Infrastructure?**
   - It's PKO-specific (other banks have their own layouts). Core's `UnsupportedBankException` is the generic case; PKO sub-layout failures are an Infrastructure concern.
   - **Recommendation:** `Coffer.Infrastructure/Parsing/Pko/UnsupportedPkoLayoutException.cs` — derives from `Exception` directly, sealed.

9. **Sign convention for amounts — signed at parser or at persistence?**
   - Parser returns signed (debit = negative, credit = positive) per `docs/03 §"prompt template"` which also encodes this. Sprint 8's import flow doesn't need to flip signs.
   - **Recommendation:** signed at parser. PKO PDFs use separate columns for debits/credits; parser writes the correct sign.

10. **Description for hash — raw or normalized?**
    - Raw breaks dedup on harmless differences (extra whitespace, mixed case). Normalized risks collapsing distinct transactions that happen to normalize identically (rare in practice).
    - **Recommendation:** normalized. See Open Question #3.

11. **Currency on `ParsedTransaction` — duplicate of `ParseResult.Currency` or per-transaction?**
    - PKO statements are single-currency per statement; foreign-currency accounts have one currency too. Multi-currency-per-statement is rare and Sprint-8+ work.
    - **Recommendation:** keep `Currency` on `ParsedTransaction` because (a) the model needs to support multi-currency one day, (b) per-transaction explicit is honest, (c) trivial cost. Parser fills it from the statement header for the standard checking case.

12. **PdfPig type leakage into `Coffer.Core`** — see open question #1 above.

## Notes

- Hard rule #1 (money is `decimal`): `ParsedTransaction.Amount` is `decimal`, `PolishAmountParser` returns `decimal`. Hard rule #9 (currency on every monetary entity): `ParsedTransaction.Currency` non-null. Hard rule #5 (no real bank statements in repo): all CI fixtures are synthetic or absent; manual verification uses a gitignored real PDF.
- Hard rule #3 (Core stays free of UI/framework refs) gets the open-question carve-out for PdfPig as a read-only data library, not a framework. See Q1.
- Hard rule #11 (PUBLIC repo audit): the synthetic PDF builder must never embed real account numbers, real merchant strings, or anything that could be tied to a person. Use placeholder IBAN (`PL61 1090 1014 0000 0712 1981 2874` is a well-known test value), generic merchants ("BIEDRONKA", "ORLEN", "MPK KRAKOW").
- Sprint 7 is the **first phase-1 sprint** — the project gains a feature dimension here beyond auth foundations.
- After Sprint 7 the parser produces `ParseResult` but nobody persists it yet. Sprint 8 ships the import flow that maps `ParseResult` → `Transaction` entities and runs `TransactionHash` for dedup.
