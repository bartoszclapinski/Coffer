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
