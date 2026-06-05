# Sprint 10 log

## 2026-06-05

- Plan written (`chore/plan-sprint-10`, issue #80). Sprint 10 = Phase 4 (auto-categorisation): make
  the Sprint-9 `CategoryId` real via the doc-04 hybrid pipeline **cache → rules → AI batch**, with
  aggressive normalisation, prompt anonymisation (hard rule #7), a cost ledger, and a monthly budget
  cap. Closes the "first usable MVP (Phases 0–4)".
- Decisions (planning): ship Sprint 10 as **three phased PRs** (10-A deterministic core → 10-B AI
  plumbing → 10-C hybrid categorisation), never one monolithic commit; **10-A is independently
  shippable** (rules + learned cache + manual re-categorisation, zero API keys / cost / network) so the
  owner sees categorisation on real data before the AI investment; **Claude first, OpenAI second**;
  categorisation runs in the import background task (cache+rules always, AI batch behind the budget gate
  at `Critical` priority) plus a one-off "recategorise existing" path for the rows already imported in
  Sprint 9; ship an opinionated Polish category set + starter rule pack, seeded idempotently (never
  overwrites user edits).
- Layering (carried from Sprint 9): abstractions + DTOs in `Coffer.Core` (`Categorization/`, `Ai/`),
  implementations in `Coffer.Infrastructure` (rule engine, cache, categoriser, AI providers, anonymiser,
  ledger, budget gate), view models in `Coffer.Application`, tests split Infrastructure (DB + fake
  provider) / Application (VM). No real API calls in CI.
- Open questions parked for implementation: secret-storage shape (extend `IKeyVault` vs new
  `ISecretStore`), `Category.ParentId` hierarchy (defer unless needed), and the first-import AI cost
  pre-flight UX. See `sprint-10.md`.
