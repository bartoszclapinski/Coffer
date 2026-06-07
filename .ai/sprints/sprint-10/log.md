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

### Phase 10-A — deterministic core (rules + learned cache + manual re-categorisation)

- Shipped the deterministic categoriser (cache → rules, **no AI / keys / network**), issue #82.
- Domain: `Rule`, `CategoryCache`, `CacheSource` (Rule < Ai < Manual precedence) + `AddCategorizationSchema`
  migration (Rules, CategoryCache tables; unique index on `CategoryCache.NormalizedDescription`,
  index on `Rules.Priority`).
- Core abstractions: `ICategoryRuleEngine` (pure matcher), `ICategoryCacheStore`, `ICategorizer`,
  `ICategoryService` (UI-facing), `ICategorySeed`, `CategoryListItem`. Implementations in Infrastructure
  over `IDbContextFactory`; respects Core-can't-see-`CofferDbContext`.
- Rule matching: enabled rules by ascending `Priority`, first match wins, case-insensitive, 100 ms regex
  timeout, malformed patterns skipped + logged (never throw into an import).
- Learning loop: a rule hit is written back as a `Rule` cache entry (free next time); a manual correction
  writes a `Manual` entry that outranks any later rule/AI write.
- Wired into `ImportStatementUseCase` (folded into the existing 5-stage flow, no new stage) — added rows
  are categorised at import; `ImportSummary` gained a `Categorized` count. Plus a "Kategoryzuj istniejące"
  path over already-imported uncategorised rows.
- Transactions page: inline per-row category picker (colour chip + combo), category filter dropdown,
  recategorise-existing button. Default Polish category set (14) + starter rule pack (8) seeded
  idempotently at startup (never overwrites the owner's edits).
- Tests: RuleEngine (priority/first-match/invalid-regex/disabled), CategoryCacheStore (hit/miss/hit-count/
  Manual-overrides-Rule/Rule-doesn't-override-Manual), RuleCacheCategorizer (cache>rule, rule write-back,
  unknown→null), CategoryService (set+learn, recategorise count), DefaultCategorySeed (idempotency, leaves
  user categories alone), categorisation schema/indexes over real SQLCipher, import-categorises-golden-CSV,
  Transactions VM (filter + manual recategorise + recategorise-existing). Full suite green (250), format clean.

### Phase 10-B — AI plumbing (provider + anonymiser + secret store + cost ledger + budget gate + Settings)

- Shipped the AI infrastructure with **no real API calls in CI** and **not yet wired into categorisation**
  (that is 10-C), issue #84. Packages: `Microsoft.Extensions.AI` 9.10.2 + `Anthropic.SDK` 5.10.0 (pinned to
  the 9.x line to avoid pulling 10.x DI abstractions over the project's 9.* pins).
- Secret storage decision: **new `ISecretStore`** rather than extending `IKeyVault`. `IKeyVault` holds the
  single TTL'd master-key cache; API keys are durable named secrets with no expiry. Windows impl is DPAPI
  (`CurrentUser`), one file per secret named by SHA-256 of the secret name (name never written to disk);
  `InMemorySecretStore` is the non-Windows/test fallback (mirrors the `IKeyVault` branch).
- Core abstractions (`Coffer.Core/Ai/`): `IAiProvider` (`CompleteAsync` / `CompleteJsonAsync<T>` /
  `StreamAsync`), `AiRequest`, `AiResult<T>` + `AiUsage`, `IPromptAnonymizer`, `IAiPricing` + `AiCost`,
  `IAiUsageLedger` + `AiSpendByPurpose`, `IAiBudgetGate` + `AiPriority`, `IAiSettings`, `AiDefaults` /
  `AiPurpose`. **Divergence from the doc-04 sketch:** `IAiProvider` returns `AiResult<T>` (value + token
  usage) instead of a bare value, so every call can be priced and ledgered.
- Infrastructure (`Coffer.Infrastructure/AI/`): `PromptAnonymizer` (IBAN → NIP → ACCOUNT redaction order,
  merchant names deliberately preserved — hard rule #7), `AiPricing` (static per-model USD/Mtok, fixed
  USD→PLN, unknown model falls back to the dearer Sonnet rate so it never under-reports), `AiUsageLedger`
  (one `AiUsageEntry` per call, month-to-date in UTC), `AiBudgetGate` (`Critical` bypasses the cap and warns;
  `Normal` blocked over cap), `AppSettingsStore` (`IAiSettings` over an `AppSetting` KV table, defaults from
  `AiDefaults`), `ClaudeProvider` (over `Microsoft.Extensions.AI` `IChatClient` = `AnthropicClient.Messages`;
  API key resolved **per-call** from `ISecretStore` so a key entered in Settings works without a restart and
  is never held in a field; `StreamAsync` stubbed until Phase 7).
- Schema: `AiUsageEntry` + `AppSetting` entities, `AddAiUsageLedger` migration (AiUsageEntries +
  AppSettings tables, indexes on `At` / `Purpose`). **Cost columns overridden to `decimal(18,6)`** — the
  global `(18,2)` convention would round a single sub-grosz categorisation call to 0.00 and make the ledger
  under-report.
- UI: Settings page (`SettingsViewModel` in Application, `SettingsView` in Desktop) — provider pick,
  categorisation model, masked API-key entry (write-only; never read back — hard rule #6), monthly PLN cap,
  month-to-date spend. New "Ustawienia" sidebar entry in `MainWindow`. **GUI not headlessly verifiable.**
- DI: `AddCofferAi()` registers the secret store (Windows/in-memory branch) + provider + anonymiser +
  pricing + ledger + budget gate + settings; `SettingsViewModel` in Desktop DI.
- Tests (27 new): PromptAnonymizer (IBAN/NIP/account redaction, merchant preserved, null/empty),
  AiPricing (known + fallback + sub-grosz), AiUsageLedger (accumulation + by-purpose over real SQLCipher),
  AiBudgetGate (under/over cap × Normal/Critical), AppSettingsStore (defaults + round-trip + upsert),
  InMemorySecretStore (round-trip/overwrite/delete/missing), SettingsViewModel (load/save/key save+clear/
  CanExecute). `MigrationRunnerTests` updated for the new latest migration. Full suite green (277),
  format clean. **No vendor API hit in CI; categorisation still deterministic until 10-C wires the gate in.**

### Phase 10-C — hybrid AI categorisation (cache → rules → AI batch)

- Shipped the `HybridCategorizer` and wired it into import behind the budget gate, issue #86. **No real
  API calls in CI** (fake `IAiProvider`). **Scope split:** this PR is the hybrid *core*; `OpenAiProvider`
  (10.21), the cache pre-warm job (10.19), and the full retry/backoff + local queue + `Retry-After` (10.20)
  are deferred to a 10-D follow-up — this PR keeps only the safe non-blocking subset of failure handling.
- `HybridCategorizer` (Infra, `ICategorizer`): **cache → rules → AI batch**. Cache/rule resolution reuses
  the exact `RuleCacheCategorizer` logic (hit-count bump, rule write-back as `CacheSource.Rule`). Unknowns
  are anonymised (hard rule #7), batched (`_batchSize = 30`, within doc-04's 20–50), and sent via
  `CompleteJsonAsync<int[]>` with the doc-04 index-array prompt; valid results are mapped by index and
  cached as `CacheSource.Ai`. **The DB connection is not held open across the network calls** — three
  spans: (A) one short context for cache/rules + loading the category list, (B) the AI batches over the
  network, (C) a second short context to persist the AI cache rows.
- **Budget gate:** each batch estimates input/output tokens (chars/4 heuristic) → `IAiPricing.Estimate`
  → `IAiBudgetGate.CanProceedAsync(..., AiPriority.Critical)`. Critical never hard-blocks (import the
  owner asked for), but a denial stops the AI loop and leaves the rest uncategorised. Each successful
  call writes the ledger (`AiPurpose.Categorization`).
- **Never breaks an import (divergence from doc-04's "return an error to the caller"):** a denied budget,
  missing key, network error, or malformed output **after one retry** leaves the affected descriptions
  `null` and logs (warn per attempt, error on give-up). The import completes; the owner can recategorise
  later. Categories are ordered by name to give the prompt a stable index→`CategoryId` map.
- Import flow: new `ImportStage.Categorizing` (between `Deduplicating` and `Saving`), reported only when
  there are new rows — VM label "Kategoryzowanie transakcji…". DI swaps `ICategorizer`
  `RuleCacheCategorizer` → `HybridCategorizer`.
- Tests (9 new): cache-hit/rule-hit skip AI, unknown → AI → cached as `Ai` + ledger written, anonymisation
  before send, batching (35 → 30+5 = 2 calls), malformed-then-valid retries once, malformed-twice leaves
  null + uncached, provider-throws doesn't break import, budget-denied skips AI. `ImportStatementUseCase`
  stage-sequence test updated for the new stage. Full suite green (286), build 0 warnings, format clean.
