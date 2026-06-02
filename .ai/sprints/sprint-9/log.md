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
