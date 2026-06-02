# Sprint 9 — Import flow + transaction list

**Phase:** 2 (Import flow + transaction list)
**Status:** Planned
**Depends on:** sprint-8 (`PkoHistoriaCsvParser`, `IBankDetector` / `StatementParserRegistry`, `StatementInput`), sprint-4 (`CofferDbContext` + SQLCipher + `MigrationRunner`), sprint-7 (`TransactionHash`, `DescriptionNormalizer`)

## Goal

The owner opens the desktop app, drops a real PKO BP "Historia rachunku" CSV onto the Import page,
picks the target account, watches the pipeline run, and then sees every parsed transaction in a
filterable list. Re-importing the same file adds no duplicates.

## Background

Sprints 7–8 produced a tested parsing backbone that is not yet wired into the UI or persisted.
Phase 2 turns it into a usable workflow: parse → dedup → save under an `ImportSession`, then show
the data. This is the first sprint that writes real financial rows to the encrypted database, so it
also lands the first schema-creating migration and wires migration-at-startup (with the mandatory
pre-migration backup, hard rule #8). AI categorisation, sync, and receipts are explicitly out of
scope (Phases 3–5); categories exist only as a minimal entity so the list filter has something to
bind to.

## Steps

### Domain + persistence

- [ ] 9.1 Add domain entities to `Coffer.Core/Domain`: `Account` (Id, Name, BankCode, AccountNumber,
  Currency, `AccountType`, IsArchived, CreatedAt), `Transaction` (per `02-database-and-encryption.md`
  — money `decimal`, `Date`/`BookingDate` `DateOnly`, `CreatedAt` UTC `DateTime`, nullable
  `CategoryId`, required `ImportSessionId`, `Hash`, `NormalizedDescription`), `ImportSession`
  (FileName, FileHash, BankCode, PeriodFrom/To, ImportedAt, TransactionsAdded/Skipped, `ImportStatus`),
  and a minimal `Category` (Id, Name, Color, IsArchived). Enums `AccountType` + `ImportStatus`.
  **Deferred to their own phases:** `Receipt`/`ReceiptItem`/`TransactionSplit` (Phase 5),
  `Tag` (Phase 9 advisor / later), `Rule` (Phase 4) — and the corresponding nav properties on
  `Transaction` are omitted now to keep the schema lean.
- [ ] 9.2 EF configuration in `Coffer.Infrastructure/Persistence`: add `DbSet`s (Accounts,
  Transactions, ImportSessions, Categories); global `decimal(18,2)` convention; indexes per doc 02
  (`Date`, `AccountId`, `(Date, AccountId)`, `Hash` **unique**, `NormalizedDescription`, `CategoryId`);
  verify `DateOnly` round-trips through the SQLCipher provider (EF Core 9 native support).
- [ ] 9.3 `dotnet ef migrations add AddTransactionsSchema` → commit under `Persistence/Migrations/`.
  Migration creates Accounts, Categories, ImportSessions, Transactions with the indexes above.
- [ ] 9.4 Wire migration-at-startup in the desktop bootstrap: after the DEK is available, run
  `MigrationRunner.RunPendingMigrationsAsync` behind the doc-02 user-facing flow — confirmation
  dialog ("Database update required. A backup will be created automatically. Continue?") → mandatory
  pre-migration snapshot via the existing `_preMigrationBackup` callback → migrate → cancel closes
  the app. Implement a minimal `IPreMigrationBackup` (encrypted-DB file copy into a retained
  subfolder); the full backup/restore system stays Phase-later (doc 08).

### Application

- [ ] 9.5 `IFilePicker` abstraction (open-file, returns a `StatementInput`-friendly stream + file
  name) + Avalonia `StorageProvider` implementation in Desktop, registered behind the interface
  (hard rule #4). No platform file-dialog types leak past Infrastructure/Desktop.
- [ ] 9.6 `ImportStatementUseCase` (Application): given a `StatementInput` + chosen/created `Account`,
  run `IBankDetector.Detect` → `StatementParserRegistry.Resolve` → `IStatementParser.ParseAsync`;
  map each `ParsedTransaction` → `Transaction` (`Hash` via `TransactionHash`, `NormalizedDescription`
  via `DescriptionNormalizer`); skip rows whose `Hash` already exists (dedup); compute the file hash
  and reject/flag a re-imported identical file via `ImportSession.FileHash`; persist the
  `ImportSession` + new transactions in one transaction via `IDbContextFactory`; return a summary
  (added / skipped / warnings). Surfaces the PKO "account number absent — confirm at import" warning
  by requiring the caller to supply the account.
- [ ] 9.7 Report progress through `IProgress<ImportProgress>` with the five pipeline stages
  (file read → bank detected → parsed → dedup → saved).
- [ ] 9.8 `GetTransactionsQuery` (Application): default last-6-months window, optional filters (text
  over Description/Merchant, `AccountId`, `CategoryId`), `AsNoTracking`, server-side `OrderByDescending(Date)`;
  plus a small query returning the account list for the filter dropdown.

### Desktop UI

- [ ] 9.9 Turn `MainWindow` into a shell with navigation between **Import** and **Transactions**
  (nav rail or tabs), matching the design language of `docs/mockups/desktop/transactions.html`
  and `dashboard.html`. Keep the existing auto-lock activity hooks and Logout.
- [ ] 9.10 Import page (View + ViewModel): drag-and-drop drop zone + "Browse…" via `IFilePicker`;
  account selection (pick existing or create a new account inline); five-step progress UI bound to
  `IProgress<ImportProgress>`; result summary (added / skipped, parser warnings). Errors
  (`UnsupportedBankException`, `UnsupportedCsvLayoutException`) shown without leaking row content.
- [ ] 9.11 Transactions list page (View + ViewModel): virtualized `DataGrid` (Date, Description,
  Merchant, Amount with sign colour, Account, Category) with a filter bar (search box, account
  dropdown, category dropdown, date range defaulting to 6 months) and an empty state. Match
  `docs/mockups/desktop/transactions.html`.
- [ ] 9.12 DI registration: use cases + queries in `Coffer.Application/DependencyInjection`;
  `IFilePicker`, new windows/pages, and view models in `Coffer.Desktop`.

### Tests

- [ ] 9.13 Persistence tests (`Coffer.Infrastructure.Tests`, real SQLCipher per existing pattern):
  migration applies on a fresh DB; expected indexes exist; the `Hash` unique index rejects a
  duplicate; `decimal(18,2)` precision; `DateOnly` round-trip.
- [ ] 9.14 `ImportStatementUseCase` tests (`Coffer.Application.Tests`): import the committed golden
  PKO CSV into a fresh encrypted DB → correct count saved under one `ImportSession`; **re-import the
  same file → 0 added, all skipped** (dedup); account creation/selection path; the five progress
  stages are emitted in order; mixed-currency / empty warnings surfaced.
- [ ] 9.15 `GetTransactionsQuery` tests: 6-month default window; each filter narrows results
  correctly; ordering is newest-first.
- [ ] 9.16 ViewModel tests: Import VM state transitions (idle → running → done / error); Transactions
  VM filter changes re-query.

### Verification + bookkeeping

- [ ] 9.17 Manual DoD (end of Phase D): launch desktop, import the gitignored real PKO CSV, pick an
  account, see **39 transactions** in the list, filter by search; re-import the same file → no
  duplicates added.
- [ ] 9.18 Each delivery phase below: `dotnet build` + `dotnet test` + `dotnet format
  --verify-no-changes` green locally before its PR.
- [ ] 9.19 `gh issue create` for closure → `chore/close-sprint-9` PR (after all phases merged).

## Delivery — phased PRs (not one monolithic commit)

Sprint 9 is sizeable, so it ships as **four incremental implementation PRs**, each its own issue +
`feature/` branch + squash-merge, each green and self-contained. Per-phase bookkeeping follows the
standard flow: issue (labels `feat` + `sprint-9`) → branch → commits → push → `gh pr create` with
`Closes #<phase-issue>` → CI green → `gh pr merge --squash --delete-branch`. Closure (9.19) runs only
after Phase D merges.

- **Phase 9-A — Schema foundation** (`feature/sprint-9a-schema`): steps 9.1–9.4 + 9.13.
  Deliverable: entities, EF config + indexes, the `AddTransactionsSchema` migration applied at
  startup with a pre-migration backup. No UI; persistence tests prove the schema.
- **Phase 9-B — Import logic** (`feature/sprint-9b-import-usecase`): steps 9.5 (`IFilePicker`
  *interface* only), 9.6, 9.7, 9.8 + 9.14, 9.15. Deliverable: headless `ImportStatementUseCase`
  (+ progress) and `GetTransactionsQuery`, tested against the golden CSV and a fresh encrypted DB.
- **Phase 9-C — Import UI** (`feature/sprint-9c-import-ui`): the Avalonia `IFilePicker`
  implementation, steps 9.9, 9.10, the import half of 9.12 + the Import VM tests from 9.16.
  Deliverable: a working Import screen wired to Phase 9-B.
- **Phase 9-D — Transactions list UI** (`feature/sprint-9d-transactions-ui`): step 9.11, the
  transactions half of 9.12, the Transactions VM tests from 9.16, and the manual DoD (9.17).
  Deliverable: the list + filters; full Phase 2 DoD met.

## Definition of Done

1. `Account`, `Transaction`, `ImportSession`, and a minimal `Category` exist in `Coffer.Core/Domain`
   (money `decimal`, transaction dates `DateOnly`, timestamps UTC `DateTime`).
2. A committed migration creates the schema with the doc-02 indexes (incl. unique `Hash`); it is
   applied at startup behind a confirmation dialog with a mandatory pre-migration backup.
3. `ImportStatementUseCase` parses a statement, dedups by `Hash`, and persists new transactions plus
   an `ImportSession` in a single DB transaction; a five-stage `IProgress` is reported.
4. The Import page imports a file via drag-and-drop or `IFilePicker`, lets the user choose the
   account, shows progress, and reports added/skipped + warnings.
5. The Transactions page shows a virtualized list with a 6-month default window and working
   search / account / category filters.
6. Re-importing the same statement adds zero duplicates.
7. File picker sits behind `IFilePicker`; no platform dialog types leak into Core/Application.
8. All tests green locally and on CI; manual import of the real PKO CSV shows 39 transactions.

## Files affected

**New (Core/Domain):**
- `src/Coffer.Core/Domain/Account.cs`, `AccountType.cs`
- `src/Coffer.Core/Domain/Transaction.cs`
- `src/Coffer.Core/Domain/ImportSession.cs`, `ImportStatus.cs`
- `src/Coffer.Core/Domain/Category.cs`
- `src/Coffer.Core/Abstractions/IFilePicker.cs` (or `Coffer.Application`)

**New (Infrastructure):**
- `src/Coffer.Infrastructure/Persistence/Configurations/*` (entity configs)
- `src/Coffer.Infrastructure/Persistence/Migrations/*_AddTransactionsSchema.*`
- `src/Coffer.Infrastructure/Persistence/PreMigrationBackup.cs` (+ `IPreMigrationBackup`)

**New (Application):**
- `src/Coffer.Application/Import/ImportStatementUseCase.cs`, `ImportProgress.cs`, `ImportSummary.cs`
- `src/Coffer.Application/Transactions/GetTransactionsQuery.cs`
- `src/Coffer.Application/ViewModels/Import/ImportViewModel.cs`
- `src/Coffer.Application/ViewModels/Transactions/TransactionsViewModel.cs`

**New (Desktop):**
- `src/Coffer.Desktop/Views/Import/*`, `src/Coffer.Desktop/Views/Transactions/*`
- `src/Coffer.Desktop/Platform/AvaloniaFilePicker.cs`

**Modified:**
- `src/Coffer.Infrastructure/Persistence/CofferDbContext.cs` (DbSets + model config)
- `src/Coffer.Desktop/App.axaml.cs` / `MainWindow.*` (navigation shell + startup migration)
- `ServiceRegistration.cs` in Application, Infrastructure, Desktop
- `src/Coffer.Desktop/Coffer.Desktop.csproj` (`Avalonia.Controls.DataGrid` added — see decisions)
- `docs/architecture/02-database-and-encryption.md` if the realised schema diverges from the doc

## Decisions (resolved at planning, 2026-06-02)

- **Delivery:** one Sprint 9, but shipped as four phased PRs (9-A…9-D above) — never one monolithic
  commit.
- **Import page design:** no mockup exists; build it in the design language of the existing
  `desktop/transactions.html` + `dashboard.html` (no separate mockup step).
- **Account bootstrapping:** always require an explicit account choice at import (no auto-seeded
  default) — multi-bank is a real requirement and PKO CSV has no account number.
- **Category in Phase 2:** ship the minimal `Category` entity and keep the category filter in the UI;
  transactions stay uncategorised until Phase 4.
- **Pre-migration backup:** minimal encrypted-DB file-copy snapshot now (satisfies hard rule #8); the
  full backup/restore system is deferred to its own sprint (doc 08).
- **DataGrid:** use `Avalonia.Controls.DataGrid` (built-in virtualization) rather than a hand-rolled
  `ItemsControl`.

## Open questions

- None outstanding at plan time. New questions that surface during a phase are logged in `log.md`.
- **Sprint size:** Phase 2 is sizeable for one sprint — acceptable to run it as one (per the
  not-time-boxed convention), or split list-vs-import into 9a/9b?
