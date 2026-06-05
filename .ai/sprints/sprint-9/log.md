# Sprint 9 log

## 2026-06-02

- Plan written (`chore/plan-sprint-9`, issue #62). Sprint 9 = Phase 2 (import flow + transaction
  list): wire the Sprint-7/8 parsing backbone into a persisted, visible workflow. Lands the first
  schema-creating migration (Accounts / Transactions / ImportSessions / Category) applied at startup
  with a mandatory pre-migration backup, `ImportStatementUseCase` (parse → dedup → save under an
  `ImportSession`), an `IFilePicker` abstraction, and Avalonia Import + Transactions pages. AI
  categorisation, sync, and receipts stay out of scope; categories are a minimal entity only.
- Decisions (planning): ship Sprint 9 as **four phased PRs** (9-A schema → 9-B import logic →
  9-C import UI → 9-D transactions UI), never one monolithic commit; Import page built in the
  existing mockups' design language (no separate mockup); always require an explicit account choice
  at import (no auto-seeded default); keep the minimal `Category` entity + filter (categorisation in
  Phase 4); minimal encrypted-DB file-copy pre-migration snapshot now (full backup/restore deferred,
  doc 08); use `Avalonia.Controls.DataGrid`.
- **Phase 9-A merged** (issue #64, PR #65): domain entities (`Account`/`Transaction`/`ImportSession`/
  minimal `Category` + `AccountType`/`ImportStatus`), EF configs with the doc-02 indexes (unique
  `Hash`), the `AddTransactionsSchema` migration applied at startup behind a confirmation dialog with
  a mandatory `IPreMigrationBackup` snapshot, and persistence tests. Code review (non-blocking) fixes
  applied: corrected the `ClearAllPools`/WAL-checkpoint comment, flagged the non-atomic 3-file copy,
  and guarded the empty `newlyApplied` case in `MigrationRunner`.
- **Phase 9-B** (issue #66): headless `ImportStatementUseCase` (read → detect → parse → dedup → save
  under one `ImportSession` in a single DB transaction; dedup by account-scoped `Hash`; file-hash
  re-import flag), `IProgress<ImportProgress>` over five stages, and `GetTransactionsQuery`
  (6-month default window, account/category/text filters, newest-first, account list). Tested against
  the golden PKO CSV + a fresh SQLCipher DB (import count under one session, re-import → 0 added,
  unknown-account throws, progress order, parser warnings surfaced; query window/filters/ordering).
- **Divergence from plan file paths (9-B):** the plan put the use case + query under
  `Coffer.Application` / `Coffer.Application.Tests`, but `Coffer.Application` references only Core +
  Shared — it cannot see `CofferDbContext`, the parser registry, or `DescriptionNormalizer` (all
  Infrastructure). Followed the existing `ILoginService`(Core)→`LoginService`(Infrastructure)
  pattern instead: **abstractions + DTOs in `Coffer.Core`** (`Import/`, `Transactions/`, incl.
  `IFilePicker`), **implementations in `Coffer.Infrastructure`**, **tests in
  `Coffer.Infrastructure.Tests`** (where the golden CSV + SQLCipher harness already live). The 9-C/9-D
  view models in `Coffer.Application` consume the Core abstractions, so the layering rule holds.

## 2026-06-03

- **Phase 9-C** (issue #68): the import UI. Adds `IAccountService` (Core) + `AccountService`
  (Infrastructure) so a fresh, account-less vault can create a target account inline before importing;
  `AvaloniaFilePicker` implements `IFilePicker` over `IStorageProvider` and copies the picked file into
  a `MemoryStream` so its lifetime is independent of the OS handle (hard rule #4 — Avalonia storage
  types stay in Desktop). `ImportViewModel` (Application) drives account pick/create → browse → import
  with `IProgress<ImportProgress>` and a Polish per-stage label, a summary (added/skipped/already-imported/
  warnings), and error translation that surfaces `UnsupportedBankException` and a generic parse-failure
  message **without leaking statement row content** into the UI or logs. `MainViewModel` became the
  sidebar shell (Import / Transakcje nav, `CurrentPage` swap, kept logout); `TransactionsViewModel` is a
  load-path placeholder fleshed out in 9-D. Views: `MainWindow.axaml` sidebar shell (240px, accent
  `#1D4ED8`) with `DataTemplate` VM→View mapping, `ImportView`, `TransactionsView`. 9 new Import VM tests
  (load/browse/happy-path/running-state/inline-create/unsupported-bank/bad-extension/no-leak); existing
  `MainViewModelTests` updated for the shell ctor. Full suite green (207 tests).
- **Manual DoD note (9-C):** the Avalonia GUI cannot be visually verified in CI/headless — the import
  real-PKO-CSV end-to-end check (DoD 9.17) is performed by hand in 9-D once the transactions grid lands.
- **Phase 9-D** (issue #70): the transactions list UI. Fleshes out `TransactionsViewModel` with a filter
  bar — search text, account dropdown, and a date-range dropdown (`DateRangeOption`, default "Ostatnie
  6 mies.", plus 3/12 months and "Cały okres") — where each filter change re-runs `IGetTransactionsQuery`
  with a rebuilt `TransactionQueryFilter` (trimmed/blank-to-null search, account id, lower-bound date;
  "Cały okres" → `DateOnly.MinValue`). A filter that changes mid-load sets a pending flag so the latest
  values are re-queried once the in-flight load finishes (no dropped keystroke). `TransactionsView`
  replaces the placeholder list with a virtualized `DataGrid` (Data / Opis / Sprzedawca / Konto /
  Kategoria / Kwota), the amount column coloured via `AmountToBrushConverter` (income green, expense
  primary text per the mockup), plus loading/error/empty surfaces. Added `Avalonia.Controls.DataGrid`
  + its Fluent theme `StyleInclude`. 8 new VM tests (populate, 6-month default, empty, search/account/
  range re-query, no-query-on-construction); full Application suite green (48 tests). Category dropdown
  deferred — no category-list query exists yet.
- **Manual DoD (9.17)** still pending the owner: run the app, import a real PKO CSV (→ 39 transactions),
  filter by search, re-import the same file → no duplicates.
- **9-D code-review fixes** (PR #71, COMMENTED, no blockers): (1) the coalescing reload was untested
  because the fake completes synchronously — added a `Gate` (`TaskCompletionSource`) to
  `FakeGetTransactionsQuery` and a mid-load test asserting exactly one trailing reload with the latest
  filter. To make that path robust under the command, `ReloadAsync` is now
  `[RelayCommand(AllowConcurrentExecutions = true)]` with a `do/while` loop on `_reloadRequested`
  (re-entrant `Execute` trips the `IsLoading` guard instead of being swallowed by the default
  no-concurrent CanExecute). (2) The account filter loaded once per VM lifetime, so an account created
  inline on Import never appeared until restart — split navigation (`LoadCommand`: refresh accounts +
  rows) from filter re-query (`ReloadCommand`: rows only), dropping the `_accountsLoaded` gate so each
  navigation refreshes accounts. Minor fixes: added a "Wszystkie konta" sentinel so the account filter
  can be cleared; `BuildFilter` uses `DateTime.Now` (user-facing window, not UTC); `App.axaml` final
  newline. Kept `DateOnly.MinValue` for "Cały okres" (reviewer's null suggestion would hit the query's
  six-month default). Application suite 48→51 green.

## 2026-06-05

- **Sprint closed.** All four phased PRs are merged to `main` (9-A #65, 9-B #67, 9-C #69, 9-D #71);
  every plan step 9.1–9.18 is complete and the DoD code criteria (1–7) are met with the full suite
  green and CI green. Phase 2's import-flow + transaction-list backbone is in place: the first
  schema-creating migration (`AddTransactionsSchema`) applies at startup behind a confirmation dialog
  with a mandatory pre-migration backup; `ImportStatementUseCase` reads → detects → parses → dedups →
  saves under one `ImportSession`; `IFilePicker` abstracts the OS picker; and the Avalonia Import and
  Transactions pages drive the flow end-to-end inside the sidebar shell. The owner-run hands-on smoke
  test (9.17 — import a real PKO CSV → 39 transactions, filter, re-import → no duplicates) is the one
  remaining manual check; closure is authorised by the owner on the strength of the green automated
  suite, and anything the smoke test surfaces is logged and fixed as a Phase-2 follow-up. Next:
  Phase 3 (sync via Google Drive).
