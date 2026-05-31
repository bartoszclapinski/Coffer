# Sprint 7 log

## 2026-05-30 — Implementation (PR #53, merged 2026-05-31)

Parsing foundations + deterministic PKO BP standard-checking parser landed in a single
implementation PR. All 12 sections (A–L) of the plan completed.

**Delivered:**
- Core contracts: `IStatementParser`, `IBankDetector`, `BankFingerprint`, `UnsupportedBankException`
  (`Coffer.Core/Parsing/`), `TransactionHash` (`Coffer.Core/Domain/`).
- Shared DTOs: `ParseResult`, `ParsedTransaction`, `ParserConfidence` (`Coffer.Shared/Parsing/` —
  `ParserConfidence` moved here from Core to avoid a Core↔Shared cycle).
- Polish-format helpers: `PolishAmountParser`, `PolishDateParser`, `DescriptionNormalizer`,
  `AccountNumberNormalizer`, `PdfLetterGrouping` + `PdfRowExtensions`.
- `FingerprintBankDetector` with 8 fingerprints (PKO_BP active; ING, MBANK, PEKAO, SANTANDER,
  MILLENNIUM, CITI, ALIOR inert until they get parsers).
- `StatementParserRegistry` — resolves by `BankCode`, throws `UnsupportedBankException` for
  null/unknown.
- `PkoBpStatementParser` for the "Wyciąg z rachunku" layout; other PKO layouts throw
  `UnsupportedPkoLayoutException`.
- DI: `AddCofferParsing` wired into `AddCofferInfrastructure`.
- Tests: 170 pass + 1 skipped (manual harness). FsCheck property-based amount/date round-trips,
  QuestPDF-generated synthetic PKO PDF, registry/detector/hash/normalizer units.

**Notable during implementation:**
- FsCheck v3 uses `[Property]` with generated method parameters, not v2's `Prop.ForAll`/`Arb.Default`.
- PKO column detection refactored from label-centre + fixed half-width bands to half-open
  `[label.X, nextLabel.X)` ranges (`PkoColumnAnchors`/`PkoColumnDetector`) — fixed description
  columns bleeding digits from the date column.

## 2026-05-31 — Manual verification finding → Sprint 8 pivot

Manual verification (step K) against a real PKO export surfaced a requirements mismatch that
reshapes Phase 1.

**Finding:** the freely-downloadable PKO BP export is **"Historia rachunku"** (on-demand
operation list via "Pobierz zestawienie"), **not** the formal monthly **"Wyciąg z rachunku"**
the parser was built for. The formal statement is a paid document for this account, so we will
never feed the PDF parser its intended input.

`PkoBpStatementParser` rejected the real file with `UnsupportedPkoLayoutException("unknown")` —
the layout gate working exactly as designed (the title is "HISTORIA RACHUNKU", not the
"Wyciąg z rachunku" marker). Not a bug. But it means the deterministic PKO **PDF** parser is
speculative: it is verified only against synthetic PDFs of an assumed layout, never real data.

**"Historia rachunku" layout** (different from what the parser expects):
- PDF columns: `Data operacji`, `Data waluty`, `Typ transakcji`, multi-line `Opis` with labelled
  sub-fields, single signed `Kwota w PLN`, `Saldo po transakcji` — not the
  `Data | Opis | Obciążenia | Uznania` model the detector looks for.
- The same export is offered as CSV/XML/XLS/HTML.

**CSV schema** (inspected from a real January export, gitignored):
- Comma-separated, every field quoted; **Windows-1250** encoding, no BOM.
- Fixed 12 columns: `Data operacji`, `Data waluty`, `Typ transakcji`, `Kwota`, `Waluta`,
  `Saldo po transakcji`, `Opis transakcji` + 5 unnamed overflow columns for description sub-fields.
- Amount: single **signed, dot-decimal** column (`-10.00`, `+2400.00`).
- Dates: ISO `yyyy-MM-dd`. Currency: explicit per row.
- Full description = join of non-empty columns from index 6 onward (labelled sub-fields:
  `Rachunek nadawcy/odbiorcy:`, `Nazwa ...:`, `Adres ...:`, `Tytuł:`, `Referencje ...:`, etc.).

**Decision:** Sprint 8 pivots PKO parsing to the **"Historia rachunku" CSV** export.
- CSV is far more robust than positional PDF parsing (explicit columns, no X-coordinate guessing).
- It enables proper golden-file tests (hard rule #10) from anonymized real CSV — the synthetic-only
  PDF fixtures never satisfied the spirit of that rule.
- `IStatementParser` must be generalised away from `PdfDocument` (→ `Stream` + format) — cheap, as
  nothing consumes it yet.
- Open decision for Sprint 8: remove the speculative PKO PDF "Wyciąg" parser (dead weight, untestable
  against real data) vs. keep it dormant. Leaning remove.

## Status

Sprint 7 **closed**. Foundations shipped and reused regardless of input format; the PKO **CSV**
parser and the interface generalisation move to Sprint 8.
