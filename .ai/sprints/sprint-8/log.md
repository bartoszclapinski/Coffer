# Sprint 8 log

## 2026-05-31

- Plan written (`chore/plan-sprint-8`, issue #56). Sprint 8 pivots PKO parsing to the
  "Historia rachunku" CSV export per the Sprint 7 manual-verification finding.
- Implementation (`feature/sprint-8-pko-csv`, issue #58):
  - **Parser-input generalisation.** `StatementFormat` (`Pdf | Csv`) + `StatementInput`
    (`Stream` + format + optional file name) added in `Coffer.Shared/Parsing`.
    `IStatementParser` now exposes `Format` and `ParseAsync(StatementInput, …)`;
    `IBankDetector.Detect(StatementInput)`. **PdfPig removed from `Coffer.Core`** — Core
    is back to zero third-party runtime deps (hard rule #3 restored). PdfPig stays an
    Infrastructure-only dependency.
  - **Speculative PKO PDF "Wyciąg" parser removed**: `PkoBpStatementParser` +
    `PkoColumnAnchors` / `PkoColumnDetector` / `PkoStandardCheckingHeader` /
    `PkoTransactionRowParser` / `UnsupportedPkoLayoutException`, and the tests
    `PkoBpStatementParserTests` / `SyntheticPkoPdfBuilder` / `PkoBpRealStatementManualHarness`.
    Generic `PdfLetterGrouping` / `PdfRowExtensions` retained as the multi-bank PDF backbone.
  - **`PkoHistoriaCsvParser`** (`Format => Csv`): Windows-1250 via CsvHelper, positional
    field read (overflow columns share an empty header), signed dot-decimal `Kwota`, ISO
    dates, joined multi-column description (Excel `'` text-guard stripped), merchant from
    the `Nazwa …:` sub-field, period from min/max operation date, empty `AccountNumber` +
    warning, `Confidence = High`. Bad header → `UnsupportedCsvLayoutException` (hint only).
  - **Detector + registry**: `FingerprintBankDetector` is format-aware (PDF first-page
    text · CSV header signature); `StatementParserRegistry` resolves on (`BankCode`,
    `Format`); DI swaps the PKO parser registration to `PkoHistoriaCsvParser`.
  - **Tests**: committed synthetic golden CSV (`pko-historia.golden.csv`, fake data,
    Windows-1250) + `PkoHistoriaCsvParserTests` (count/period/currency, signs,
    embedded-comma, Excel-guard strip, diacritics, wrong-header, empty), detector +
    registry tests ported to `StatementInput`, and a `[SkippableFact]` real-CSV harness.
    All 141 Infrastructure tests green; full solution 177 green; `dotnet format` clean.
  - **Manual verification** against the gitignored real January CSV: **39 transactions
    (8 credits + 31 debits)**, period 2026-01-04 → 2026-01-31, PLN, `High` — matches the
    Sprint-7 finding. Nothing real leaks (`tests/.local-fixtures/` stays ignored).
  - **Plan deviation (8.9/8.10):** `SyntheticTextPdfBuilder` + the QuestPDF test dependency
    were **kept**, not removed — the detector still supports PDF (8.17) and its ported PDF
    cases (8.22) need synthetic text PDFs. Removal was conditional ("if unused after"); they
    are still used, so they stay. CLAUDE.md hard rule #3 had no PdfPig carve-out note to
    revert (the carve-out lived only in the Sprint-7 interface XML docs, now rewritten).
