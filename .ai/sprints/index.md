# Sprint index

Status as of: 2026-06-07

| Sprint | Phase | Goal | Status |
|---|---|---|---|
| [sprint-0](sprint-0/sprint-0.md) | 0 | Repo on GitHub, `Coffer.sln` with 5 production + 3 test projects, `dotnet build` green, CI green | Closed (2026-05-12) |
| [sprint-1](sprint-1/sprint-1.md) | 0 | DI plumbing, Serilog (console + file), Avalonia booted via DI, `MainWindow` with `ILogger<MainWindow>` | Closed (2026-05-13) |
| [sprint-2](sprint-2/sprint-2.md) | 0 | `IKeyVault` in Core, `WindowsDpapiKeyVault` (DPAPI, 7-day TTL), `InMemoryKeyVault` (cross-platform fallback), round-trip tests | Closed (2026-05-13) |
| [sprint-3](sprint-3/sprint-3.md) | 0 | `Argon2KeyDerivation`, `Bip39SeedManager`, `AesGcmCrypto`, `DekFile` format + serializer, 17 tests (including official BIP39 vectors) | Closed (2026-05-14) |
| [sprint-4](sprint-4/sprint-4.md) | 0 | `CofferDbContext` + `_SchemaInfo`, `SqlCipherKeyInterceptor` (PRAGMA key per connection), `MigrationRunner` with pre-migration backup callback, first migration `InitialCreate`, 9 integration tests | Closed (2026-05-17) |
| [sprint-5](sprint-5/sprint-5.md) | 0 | Avalonia setup wizard (welcome → master password with zxcvbn → BIP39 display + verification → confirm), `IDekHolder`, `SetupService` orchestrator, `IScreenCaptureBlocker` (Win32 P/Invoke); first interactive UI | Closed (2026-05-18) |
| [sprint-6](sprint-6/sprint-6.md) | 0 | `ILoginService` (DPAPI cache → password fallback), `ILastActivityTracker` + `AutoLockMonitor` (15-min idle), `LoginWindow`, `MainWindow` upgrade ("Zalogowano" + version + "Wyloguj"); closes Phase 0 | Closed (2026-05-20) |
| [sprint-7](sprint-7/sprint-7.md) | 1 | Parsing foundations: `IStatementParser` / `IBankDetector` / `StatementParserRegistry`, Polish-format helpers, `FingerprintBankDetector` (PKO + 6 inert), `PkoBpStatementParser` standard checking, `TransactionHash`, FsCheck property-based tests + synthetic QuestPDF fixtures | Closed (2026-05-31) |
| [sprint-8](sprint-8/sprint-8.md) | 1 | Pivot PKO parsing to the "Historia rachunku" **CSV** export: generalise `IStatementParser` / `IBankDetector` off `PdfDocument` → `StatementInput`, `PkoHistoriaCsvParser` (Windows-1250, signed amounts, multi-column description), remove the speculative PKO PDF parser, synthetic golden CSV + real-CSV manual harness | Closed (2026-05-31) |
| [sprint-9](sprint-9/sprint-9.md) | 2 | Import flow + transaction list: domain entities (`Account` / `Transaction` / `ImportSession` / minimal `Category`) + first schema migration applied at startup with pre-migration backup, `ImportStatementUseCase` (parse → dedup → save), `IFilePicker`, Avalonia Import page (drag-and-drop + 5-step progress) and Transactions list (6-month default, search/account/category filters) | Closed (2026-06-05) |
| [sprint-10](sprint-10/sprint-10.md) | 4 | Auto-categorisation: `Rule` / `CategoryCache` / `AiUsageEntry` schema + migrations, deterministic `RuleEngine` + learned cache wired into import, manual re-categorisation in the grid, default Polish category/rule seed, `IAiProvider` (Claude + OpenAI) + `PromptAnonymizer` + cost ledger + `AiBudgetGate` + Settings, `HybridCategorizer` (cache → rules → AI batch). Three phased PRs (10-A deterministic → 10-B AI plumbing → 10-C hybrid) | Closed (2026-06-07) |
| [sprint-11](sprint-11/sprint-11.md) | 6 | Dashboard and charts: `IDashboardQuery` + server-side (`GROUP BY`) aggregations, `DashboardViewModel`, Avalonia Dashboard page (current-month spend/income/net KPI cards, 30-day spend trend, 12-month bar, top-categories doughnut, recent transactions) via LiveCharts2; Dashboard becomes the post-login landing page. Single PR | Planned |

**Phase 0 closed end-to-end on 2026-05-20.** Cold-start setup → wizard → `MainWindow`; subsequent cold start → DPAPI cache bypass OR login window → `MainWindow`; 15-min idle → auto-lock → login. ~104 automated tests, CI green.

**Phase 1 opened with Sprint 7** — format-agnostic parsing foundations (interfaces, Polish helpers, registry, dedup hash) plus a deterministic PKO BP **PDF** parser for the "Wyciąg z rachunku" layout. Manual verification then revealed the freely-available PKO export is **"Historia rachunku"** (CSV/PDF/XML), not the paid "Wyciąg z rachunku" — so the PDF parser is speculative (synthetic-verified only). **Sprint 8 closed that pivot** — `IStatementParser` / `IBankDetector` now operate on a format-neutral `StatementInput` (PdfPig dropped from `Coffer.Core`), the speculative PKO PDF parser is gone, and `PkoHistoriaCsvParser` parses the "Historia rachunku" CSV (Windows-1250, signed amounts, joined descriptions), verified by a committed synthetic golden CSV in CI and a gitignored real CSV manually (39 transactions). The AI fallback, Anonymizer CLI, and remaining PKO layouts follow. See [sprint-7/log.md](sprint-7/log.md) for the finding and the CSV schema.

## Beyond Phase 1

- **Phase 2** — Import flow + transaction list
- **Phase 3** — Sync via Google Drive
- **Phase 4** — Auto-categorisation
- **Phase 5+** — Receipts (from this phase, mobile/MAUI is needed)

Mobile (`Coffer.Mobile`, MAUI) — postponed until Phase 5 is approaching or it is needed earlier.

> Note: Sprint plans and logs for Sprints 0-3 are in Polish (historical artefacts from the early phase of the project). All sprints from Sprint 4 onward are in English. See [.ai/sprints/README.md](README.md) for the language policy.
