# Sprint 8 — PKO BP "Historia rachunku" CSV parser + parser-input generalisation

**Phase:** 1 (Statement parser for PKO BP)
**Status:** Planned
**Depends on:** sprint-7 (parsing foundations: DTOs, Polish helpers, registry, `TransactionHash`)

## Goal

Importing a real PKO BP "Historia rachunku" CSV export produces a correct `ParseResult`
(account, period, currency, all transactions with signed amounts and joined descriptions),
verified against a committed synthetic golden CSV in CI and a gitignored real CSV manually.

## Background — why this sprint pivots

Sprint 7 built a deterministic PKO **PDF** parser for the "Wyciąg z rachunku" layout. Manual
verification then revealed the freely-available PKO export is **"Historia rachunku"** (an
on-demand operation list, available as CSV/PDF/XML/XLS/HTML), **not** the paid monthly
"Wyciąg z rachunku". The PDF parser correctly rejected the real file via the layout gate, but it
targets a document we cannot obtain. See [../sprint-7/log.md](../sprint-7/log.md) for the finding
and the full CSV schema.

**CSV is the right target:** explicit columns (no positional X-coordinate guessing), ISO dates,
signed dot-decimal amounts, explicit currency. It is also far more robust to bank-side layout
tweaks than the multi-line PDF, and lets us commit real golden-file tests.

## Strategy

Sprint 8 stays parser-only — it produces a `ParseResult`; nobody persists it yet (the import /
dedup-into-DB flow is Phase 2). Three concrete strategic calls:

- **Generalise the parser input off `PdfDocument`.** `IStatementParser.ParseAsync` and
  `IBankDetector.Detect` currently take PdfPig's `PdfDocument`, which blocks a CSV parser. Introduce
  a neutral `StatementInput` (raw `Stream` + `StatementFormat` enum + optional file name). PDF
  parsers open PdfPig from the stream themselves; the CSV parser reads the stream as Windows-1250.
  Nothing consumes these interfaces yet, so this is a cheap, backwards-incompatible change — no
  shims.
- **Remove the speculative PKO PDF "Wyciąg" parser.** It is verified only against synthetic PDFs of
  an assumed layout and can never be tested against real data (the document is paid). Keeping it is
  dead weight that violates the spirit of hard rule #10 (golden files should reflect reality).
  Remove the parser and its PKO-PDF-specific helpers, the synthetic PDF builder, the QuestPDF test
  dependency, and the PDF manual harness. **Keep** the format-agnostic foundations (DTOs, Polish
  helpers, registry, `TransactionHash`) and the generic PDF letter-grouping helpers + the
  fingerprint detector's text-matching path — those remain the multi-bank PDF backbone for future
  banks that only offer PDF.
- **Golden file = synthetic real-shape CSV, committed; real CSV stays a gitignored manual harness.**
  A hand-authored CSV matching the documented real schema (fake accounts/names/IBANs) is committable
  with zero anonymisation (hard rule #5 satisfied by construction) and serves as the CI golden file.
  Real-data verification reuses the Sprint-7 manual-harness pattern against a gitignored
  `tests/.local-fixtures/`. The full `tools/Anonymizer` CLI stays deferred — we only need it once we
  want real-quirk golden files for a second bank or AI-fallback baselines.

Deferred to later Phase-1 sprints: AI fallback (`AiAssistedParser` + `PromptAnonymizer`, hard rule
#7), AI cost tracking (`AiUsageEntry` / budget gate), the Anonymizer CLI, and remaining PKO layouts.
The import-into-DB + dedup flow is Phase 2.

Three PRs in the established workflow:
1. **Plan** (`chore/plan-sprint-8`, this document) — issue #56
2. **Implementation** (`feature/sprint-8-pko-csv`, new issue) — code + tests
3. **Closure** (`chore/close-sprint-8`, new issue) — post-merge bookkeeping

## The real CSV schema (from Sprint 7 finding)

- Comma-separated, every field quoted (`"..."`); embedded commas occur inside description fields →
  needs an RFC-4180-correct reader, not a naive `Split(',')`.
- **Windows-1250** encoding, no BOM.
- Fixed **12 columns**, header row:
  `Data operacji, Data waluty, Typ transakcji, Kwota, Waluta, Saldo po transakcji, Opis transakcji`
  followed by 5 unnamed overflow columns for description sub-fields (indices 7–11).
- `Kwota`: single **signed, dot-decimal** value (`-10.00`, `+2400.00`).
- Dates: ISO `yyyy-MM-dd` (both `Data operacji` and `Data waluty`).
- `Waluta`: explicit per row (e.g. `PLN`).
- Description: join of non-empty columns from index 6 onward; labelled sub-fields like
  `Rachunek nadawcy/odbiorcy:`, `Nazwa nadawcy/odbiorcy:`, `Adres ...:`, `Tytuł:`, `Referencje ...:`,
  `Numer telefonu:`, `Lokalizacja:`. One field may carry a leading `'` (Excel text-guard) → strip.

## Steps

### A. NuGet packages

- [x] 8.1 `Coffer.Infrastructure` — add `CsvHelper` (latest stable) for RFC-4180-correct quoted-field
  parsing. License: dual MS-PL / Apache 2.0 — fine for a public repo.
- [x] 8.2 `Coffer.Infrastructure` — add `System.Text.Encoding.CodePages` and register
  `CodePagesEncodingProvider.Instance` so `Encoding.GetEncoding(1250)` works on .NET 9 (Windows-1250
  is not a built-in code page on non-Windows / modern .NET).

### B. Generalise parser input (`Coffer.Core/Parsing/`, `Coffer.Shared/Parsing/`)

- [x] 8.3 `StatementFormat` enum in `Coffer.Shared/Parsing/` — `Pdf | Csv` (Xml/Xls/Html added when
  needed; do not pre-add).
- [x] 8.4 `StatementInput` in `Coffer.Shared/Parsing/` — carries the raw content and format:
  `Stream Content`, `StatementFormat Format`, `string? FileName`. The reader owns the stream;
  parsers must not dispose it.
- [x] 8.5 Change `IStatementParser` — `ParseAsync(StatementInput input, CancellationToken ct)`; add a
  `StatementFormat Format { get; }` (or fold format into `CanHandle`) so the registry can resolve by
  (bank, format). Update the XML docs (remove the PdfPig reference).
- [x] 8.6 Change `IBankDetector` — `BankFingerprint? Detect(StatementInput input)`. Drop the
  `PdfDocument` dependency from the interface; PdfPig stays an Infrastructure-only dependency now.
- [x] 8.7 Re-evaluate the `UglyToad.PdfPig` reference in `Coffer.Core.csproj`. With detection moved
  to `StatementInput`, Core no longer needs PdfPig — **remove it from Core** and revert the hard
  rule #3 carve-out note (Core goes back to zero third-party runtime deps). PdfPig stays only in
  Infrastructure.

### C. Remove the speculative PKO PDF "Wyciąg" parser (`Coffer.Infrastructure/Parsing/Pko/`)

- [x] 8.8 Delete `PkoBpStatementParser.cs`, `PkoColumnAnchors.cs`, `PkoColumnDetector.cs`,
  `PkoStandardCheckingHeader.cs`, `PkoTransactionRowParser.cs`, `UnsupportedPkoLayoutException.cs`.
- [x] 8.9 Delete the corresponding tests + fixtures: `Pko/PkoBpStatementParserTests.cs`,
  `Pko/SyntheticPkoPdfBuilder.cs`, `Pko/PkoBpRealStatementManualHarness.cs`, and
  `Parsing/SyntheticTextPdfBuilder.cs` if unused after. **Outcome:** the three PKO-PDF
  test files were deleted; `SyntheticTextPdfBuilder.cs` was **kept** — it is still used by
  the detector's ported PDF cases (8.22), so the "if unused" condition did not apply.
- [x] 8.10 Remove the `QuestPDF` package from `Coffer.Infrastructure.Tests.csproj` and any
  `LicenseType.Community` bootstrap, if no remaining test generates PDFs. **Outcome:**
  `QuestPDF` was **kept** — `FingerprintBankDetectorTests` still generates synthetic text
  PDFs to exercise PDF detection, so the condition did not apply.
- [x] 8.11 Keep `PdfLetterGrouping` / `PdfRowExtensions` (generic, reused by future PDF parsers) and
  their tests. Confirm they still build with no PKO-PDF callers.

### D. CSV parser (`Coffer.Infrastructure/Parsing/Pko/`)

- [x] 8.12 `PkoHistoriaCsvParser : IStatementParser` — `BankCode => "PKO_BP"`,
  `Format => StatementFormat.Csv`. Reads `input.Content` as Windows-1250 via CsvHelper.
- [x] 8.13 Column mapping per record → `ParsedTransaction`:
  - `Date` ← `Data operacji` (via `PolishDateParser`, ISO accepted).
  - `BookingDate` ← `Data waluty` (nullable).
  - `Amount` ← `Kwota` — signed dot-decimal; parse with invariant culture after stripping a leading
    `+`. (Note: `PolishAmountParser` assumes comma decimal; the CSV uses dot — use a dedicated
    invariant parse here, or extend the helper to accept dot. Decision in 8.18.)
  - `Currency` ← `Waluta`.
  - `Description` ← join of non-empty columns 6–11 with a single space; trim; strip a leading `'`.
  - `Merchant` ← extracted from the `Nazwa odbiorcy:` / `Nazwa nadawcy:` sub-field when present,
    else `null`.
- [x] 8.14 Header extraction for `ParseResult`: `AccountNumber` and period are **not** in the CSV
  body rows. Derive `PeriodFrom`/`PeriodTo` from the min/max `Data operacji` across rows; set
  `AccountNumber` to empty (or parse from `FileName` / a header line if present) and add a `Warning`
  when it cannot be determined. `Currency` from the first row (assert single-currency, warn if mixed).
- [x] 8.15 Validate column shape: confirm the header matches the expected 12-column PKO Historia
  signature; if not, throw a CSV-specific `UnsupportedCsvLayoutException` (sealed, carries a hint —
  no row content in the message, per the no-leak rule).
- [x] 8.16 `Confidence = High` (deterministic).

### E. Detection + registry

- [x] 8.17 `FingerprintBankDetector` — make it format-aware via `StatementInput`:
  - `Pdf` → existing first-page text fingerprint match (unchanged logic, now opens PdfPig from the
    stream).
  - `Csv` → match the PKO "Historia rachunku" header signature (and/or `FileName` like
    `Zestawienie operacji ...`) → `PKO_BP` fingerprint; else `null`.
- [x] 8.18 `StatementParserRegistry.Resolve` — resolve by (`BankCode`, `Format`). Update so a PKO
  fingerprint + `Csv` resolves to `PkoHistoriaCsvParser`. Unknown bank/format still throws
  `UnsupportedBankException` (AI fallback swaps this in a later sprint).
- [x] 8.19 DI: register `PkoHistoriaCsvParser` as the PKO `IStatementParser` in `AddCofferParsing`;
  remove the deleted PDF parser registration.

### F. Tests (`Coffer.Infrastructure.Tests`)

- [x] 8.20 Committed **synthetic golden CSV** fixture under
  `tests/Coffer.Infrastructure.Tests/Parsing/Pko/Fixtures/pko-historia.golden.csv` — Windows-1250,
  real schema, **fake** accounts/names/IBANs/merchants (hard rule #5/#11), ~10–15 rows covering:
  debit + credit, multi-column description, embedded comma in a quoted field, leading-`'` field,
  multi-line description if representable.
- [x] 8.21 `PkoHistoriaCsvParserTests`:
  - `Parse_GoldenCsv_ReturnsAllTransactions` — asserts row count, period (min/max date), currency;
    spot-checks 2–3 transactions (date, signed amount, joined description, merchant).
  - `Parse_DebitAndCredit_SignsCorrect` — negative for debit, positive for credit.
  - `Parse_EmbeddedCommaInQuotedField_NotSplit`.
  - `Parse_WrongHeaderShape_ThrowsUnsupportedCsvLayoutException`.
  - `Parse_Windows1250_DecodesPolishDiacritics` (e.g. `Świadczenie`, `Łubianka`).
- [x] 8.22 `FingerprintBankDetectorTests` — add CSV cases: PKO Historia header → `PKO_BP`; unknown
  CSV → `null`. Keep/port existing PDF cases to `StatementInput`.
- [x] 8.23 `StatementParserRegistryTests` — update to (bank, format) resolution; PKO+Csv → CSV parser;
  unknown → throws.
- [x] 8.24 Decide amount-parse home (8.13): add a `PolishAmountParserTests` case for dot-decimal
  signed input if the helper is extended, or a local parser test in the CSV parser tests.

### G. Manual verification

- [x] 8.25 Reuse the gitignored fixture folder: real export already at
  `tests/.local-fixtures/Zestawienie operacji za 01.01.2026 - 31.01.2026.csv`. Add a
  `[SkippableFact]` `PkoHistoriaCsvRealStatementManualHarness` that parses it (Windows-1250) and
  prints the `ParseResult` via `ITestOutputHelper`; skips when the file is absent.
- [x] 8.26 Run locally, eyeball: 39 transactions, correct signs, sane joined descriptions, period
  2026-01-01 → 2026-01-31. Confirm nothing leaks into git (`.local-fixtures/` stays ignored).

### H. Docs

- [x] 8.27 Update `docs/architecture/03-statement-parsers.md` — add a "Historia rachunku CSV" section
  documenting the schema and the `StatementInput` generalisation; note PKO is parsed via CSV, not
  PDF, and why. Revert/adjust the hard rule #3 PdfPig carve-out note in CLAUDE.md (Core no longer
  references PdfPig).

### I. Validation and merge

- [x] 8.28 `dotnet build` + `dotnet test` + `dotnet format --verify-no-changes` green locally.
- [x] 8.29 `gh issue create` for implementation — `feat(sprint-8): PKO BP Historia rachunku CSV
  parser`, labels `feat` + `sprint-8`.
- [ ] 8.30 Commit on `feature/sprint-8-pko-csv`, push, `gh pr create` with `Closes #<impl-issue>`.
- [ ] 8.31 CI green, squash-merge, branch deleted.
- [ ] 8.32 `gh issue create` for closure → `chore/close-sprint-8` PR.

## Definition of Done

1. `IStatementParser` / `IBankDetector` operate on `StatementInput` (format + stream); `PdfDocument`
   no longer appears in `Coffer.Core`. `Coffer.Core` has zero third-party runtime references again.
2. `StatementFormat` + `StatementInput` in `Coffer.Shared/Parsing/`.
3. `PkoHistoriaCsvParser` parses the PKO "Historia rachunku" CSV (Windows-1250, signed amounts, ISO
   dates, joined multi-column description, merchant extraction) → `ParseResult` with `Confidence =
   High`.
4. Speculative PKO PDF "Wyciąg" parser + PKO-PDF helpers + synthetic PDF builder + QuestPDF
   dependency + PDF manual harness removed; generic PDF helpers (`PdfLetterGrouping`) retained.
5. `FingerprintBankDetector` detects PKO from a CSV; `StatementParserRegistry` resolves PKO+Csv to
   the CSV parser.
6. Committed synthetic golden CSV (fake data) + passing `PkoHistoriaCsvParserTests`; detector +
   registry tests updated. All tests green locally and on CI Ubuntu.
7. Manual verification against the gitignored real CSV: 39 transactions, correct period/signs,
   nothing leaks into git history.
8. `docs/architecture/03-statement-parsers.md` updated for the CSV path; CLAUDE.md hard rule #3 note
   reverted.

## Files affected

**New:**
- `src/Coffer.Shared/Parsing/StatementFormat.cs`
- `src/Coffer.Shared/Parsing/StatementInput.cs`
- `src/Coffer.Infrastructure/Parsing/Pko/PkoHistoriaCsvParser.cs`
- `src/Coffer.Infrastructure/Parsing/Pko/UnsupportedCsvLayoutException.cs`
- `tests/Coffer.Infrastructure.Tests/Parsing/Pko/Fixtures/pko-historia.golden.csv`
- `tests/Coffer.Infrastructure.Tests/Parsing/Pko/PkoHistoriaCsvParserTests.cs`
- `tests/Coffer.Infrastructure.Tests/Parsing/Pko/PkoHistoriaCsvRealStatementManualHarness.cs`

**Modified:**
- `src/Coffer.Core/Parsing/IStatementParser.cs`, `IBankDetector.cs` — `StatementInput`
- `src/Coffer.Core/Coffer.Core.csproj` — remove `PdfPig`
- `src/Coffer.Infrastructure/Coffer.Infrastructure.csproj` — add `CsvHelper`, `System.Text.Encoding.CodePages`
- `src/Coffer.Infrastructure/Parsing/FingerprintBankDetector.cs` — format-aware
- `src/Coffer.Infrastructure/Parsing/StatementParserRegistry.cs` — (bank, format) resolution
- `src/Coffer.Infrastructure/DependencyInjection/ServiceRegistration.cs` — swap PKO parser registration
- `tests/Coffer.Infrastructure.Tests/Coffer.Infrastructure.Tests.csproj` — remove `QuestPDF`
- `tests/Coffer.Infrastructure.Tests/Parsing/FingerprintBankDetectorTests.cs`, `StatementParserRegistryTests.cs`
- `docs/architecture/03-statement-parsers.md`, `CLAUDE.md` (hard rule #3 note)
- `.ai/sprints/sprint-8/sprint-8.md`, `log.md`, `.ai/sprints/index.md`

**Deleted:**
- `src/Coffer.Infrastructure/Parsing/Pko/PkoBpStatementParser.cs`, `PkoColumnAnchors.cs`, `PkoColumnDetector.cs`, `PkoStandardCheckingHeader.cs`, `PkoTransactionRowParser.cs`, `UnsupportedPkoLayoutException.cs`
- `tests/Coffer.Infrastructure.Tests/Parsing/Pko/PkoBpStatementParserTests.cs`, `SyntheticPkoPdfBuilder.cs`, `PkoBpRealStatementManualHarness.cs`, `Parsing/SyntheticTextPdfBuilder.cs`

## Open questions

1. **`StatementInput` carries `Stream` or `byte[]`?** A `Stream` lets large PDFs avoid full buffering,
   but a CSV re-read (header probe by detector, then parse by parser) needs a seekable/re-readable
   source. **Recommendation:** `Stream` with the contract "must be seekable; reader resets position";
   the import flow hands a `MemoryStream`. Revisit only if a streaming PDF case appears.
2. **Detection coupling — one format-aware `FingerprintBankDetector`, or split `PdfBankDetector` /
   `CsvBankDetector`?** One class with a `switch (input.Format)` is simplest now (two branches).
   **Recommendation:** keep one detector; split only when a third format arrives.
3. **Where does dot-decimal signed amount parsing live?** Extend `PolishAmountParser` to accept dot
   decimals, or parse locally in the CSV parser. **Recommendation:** parse locally in the CSV parser
   (invariant `decimal.Parse` after stripping `+`/spaces) — the PKO CSV format is fixed and dot-based;
   don't muddy the Polish (comma) helper.
4. **`AccountNumber` source for the CSV.** The body rows have no account number; it may be in the PDF
   header only. **Recommendation:** leave empty + `Warning` for now; the import flow (Phase 2) can ask
   the user to confirm the target account, and `TransactionHash` can key on a user-selected account at
   import time. Confirm this is acceptable for the dedup design.
5. **Remove the PKO PDF parser entirely, or keep it dormant behind a flag?** **Recommendation:**
   remove — it is untestable against real data and the CSV parser is the PKO path. Git history keeps
   it recoverable if PKO PDF ever becomes free. (This is the headline decision of the sprint; flag for
   explicit sign-off.)
6. **Golden CSV fidelity — synthetic vs anonymised-real.** **Recommendation:** synthetic real-shape
   (committable with zero PII risk) for CI; gitignored real CSV for the manual harness. The Anonymizer
   CLI stays deferred until a second bank or AI baselines need real-quirk goldens.

## Notes

- Hard rule #1 (decimal money): `Kwota` parses to `decimal`. Hard rule #9 (currency always present):
  `Waluta` maps to `ParsedTransaction.Currency`. Hard rule #5/#11 (no real statements / public repo):
  the committed golden CSV is fully synthetic; the real CSV stays gitignored in `.local-fixtures/`.
- Hard rule #3 (Core free of framework/third-party refs): this sprint *restores* it by removing the
  PdfPig reference from Core — the Sprint-7 carve-out is no longer needed.
- After Sprint 8 the parser produces a `ParseResult` for the real PKO CSV; Phase 2 builds the import
  flow that maps it to `Transaction` entities and dedups via `TransactionHash`.
