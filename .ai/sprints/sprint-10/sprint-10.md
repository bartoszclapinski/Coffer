# Sprint 10 — Auto-categorisation (rules + cache + hybrid AI)

**Phase:** 4 (Auto-categorisation)
**Status:** Planned
**Depends on:** sprint-9 (`Transaction.NormalizedDescription` / `CategoryId`, minimal `Category`, `ImportStatementUseCase`, Transactions grid), sprint-7 (`DescriptionNormalizer`), sprint-2/3 (`IKeyVault` for API-key storage)

## Goal

Imported transactions get a category automatically: deterministic rules and a learned cache
handle the bulk for free, and an AI batch fills the rest within a user-set monthly budget. The
owner sees categories in the grid, can re-categorise by hand (which the app learns), and a
re-import of the same merchants costs nothing.

## Background

Sprint 9 (Phase 2) landed `Transaction` with a nullable `CategoryId`, `NormalizedDescription`, and a
minimal `Category` entity that exists only so the list filter has something to bind to — transactions
are all uncategorised today. Phase 4 makes categorisation real, following the hybrid pipeline in
`docs/architecture/04-ai-strategy.md`: **cache → rules → AI batch**, with aggressive normalisation,
prompt anonymisation (hard rule #7), a cost ledger, and a monthly budget cap. Per `10-roadmap.md`,
Phase 4 closes the "first usable MVP (Phases 0–4)".

The phase is sizeable, so — like Sprint 9 — it ships as **three incremental PRs**. The deterministic
core (10-A) is independently shippable and adds visible value with **zero API keys, zero cost, zero
network**: rules + a learned cache already categorise most recurring Polish merchants. The AI plumbing
(10-B) and the hybrid categoriser (10-C) build on top. Each phase ends green and self-contained.

**Out of scope (later phases):** chat with data (Phase 7), anomaly detection (Phase 8), receipts and
vision (Phase 5), goals (Phase 9). This sprint adds the `IAiProvider` abstraction and a cost ledger
that those phases will reuse, but wires AI only to categorisation.

## Layering note

Per the Sprint 9 finding, `Coffer.Application` references only Core + Shared and cannot see
`CofferDbContext`. So: **abstractions + DTOs in `Coffer.Core`** (`Categorization/`, `Ai/`),
**implementations in `Coffer.Infrastructure`** (rule engine, cache, categoriser, AI providers,
anonymiser, ledger, budget gate), **view models in `Coffer.Application`** consuming the Core
abstractions, **tests in `Coffer.Infrastructure.Tests`** (DB + provider harness) and
`Coffer.Application.Tests` (VM). The same pattern as `ILoginService`(Core)→`LoginService`(Infra).

## Steps

### Phase 10-A — Deterministic core (rules + cache + manual)

- [ ] 10.1 Extend domain in `Coffer.Core/Domain`: add `Rule` (Id, Priority, Pattern, CategoryId,
  IsEnabled) and `CategoryCache` (Id, NormalizedDescription, CategoryId, LastUsedAt, HitCount,
  `CacheSource` enum `Rule | AI | Manual`) per docs 02/04. Keep `Category` minimal (already exists);
  add `ParentId` only if needed (defer hierarchy — note in Open questions).
- [ ] 10.2 EF configuration + migration `AddCategorizationSchema` (pre-migration backup runs at
  startup, hard rule #8): `DbSet`s for Rules + CategoryCache; indexes — `Rule.Priority`,
  `CategoryCache.NormalizedDescription` **unique**. (`Transaction.CategoryId` and
  `Transaction.NormalizedDescription` indexes already exist from Sprint 9.)
- [ ] 10.3 `ICategoryRuleEngine` (Core) + `RuleEngine` (Infra): regex match of
  `NormalizedDescription` against enabled rules ordered by `Priority` (lower = higher), case-insensitive,
  first match wins; returns `CategoryId` or none. Compile + guard against bad user regex (invalid
  pattern is skipped + logged, never throws into import).
- [ ] 10.4 `ICategoryCacheStore` (Core) + `CategoryCacheStore` (Infra): exact `NormalizedDescription`
  lookup; upsert that bumps `HitCount` / `LastUsedAt`; write-back with `CacheSource`. A `Manual`
  entry overrides an existing `AI`/`Rule` entry for the same key (the learning loop).
- [ ] 10.5 Default content seed (idempotent, on first run after the migration): the doc-04 Polish
  category set (Spożywcze, Paliwo, Restauracje, Subskrypcje, Edukacja, Rozrywka, Zdrowie, Transport,
  Mieszkanie, Ubrania, Kredyt hipoteczny, Inwestycje, Wpływy, Inne) + a starter `Rule` pack
  (`LIDL|BIEDRONKA|ŻABKA|KAUFLAND → Spożywcze`, `ORLEN|SHELL|BP|CIRCLE\sK → Paliwo`,
  `NETFLIX|SPOTIFY|ANTHROPIC|OPENAI → Subskrypcje`, `PKO.*RATA|.*KREDYT → Kredyt hipoteczny`, …).
  Seed only when the tables are empty; never overwrite user edits.
- [ ] 10.6 `ICategorizer` (Core) with a deterministic implementation `RuleCacheCategorizer` (Infra):
  per transaction run **cache → rules**; on a rule hit, write the result to cache (`Source = Rule`).
  Returns the chosen `CategoryId` or `null` (unknown — left for 10-C's AI stage).
- [ ] 10.7 Wire categorisation into `ImportStatementUseCase`: after dedup, run the deterministic
  categoriser over the new transactions and set `CategoryId` before saving (same DB transaction).
  Unknown transactions are saved with `CategoryId = null`. Add a categorisation count to
  `ImportSummary` (e.g. "categorised N of M").
- [ ] 10.8 Transactions grid: render the resolved category (name + colour chip) in the existing
  Category column, and add **manual re-categorisation** (a category picker per row / context action)
  that updates `Transaction.CategoryId` and writes a `Manual` cache entry (10.4). Make the existing
  category **filter** functional now that categories are populated.
- [ ] 10.9 A "recategorise existing" path: a command that re-runs the deterministic categoriser over
  already-imported uncategorised transactions (so the owner's current 39 rows get categorised without
  a re-import). Surfaced as a button on the Transactions page or run once on first launch after seed.

### Phase 10-B — AI provider abstraction + budget/ledger

- [ ] 10.10 `IAiProvider` (Core) per doc 04 (`CompleteAsync` / `CompleteJsonAsync<T>` / `StreamAsync`,
  `AiRequest` record) on top of `Microsoft.Extensions.AI`. Streaming may be a stub until Phase 7 — only
  `CompleteJsonAsync` is needed for categorisation; mark the rest accordingly.
- [ ] 10.11 `ClaudeProvider` (Infra) via `Anthropic.SDK` / `Microsoft.Extensions.AI`. API key resolved
  through secure storage (10.13), model id from settings (default Haiku 4.5 for categorisation).
- [ ] 10.12 `PromptAnonymizer` (Infra, `Infrastructure/AI/PromptAnonymizer.cs` per hard rule #7):
  redact account numbers / IBAN / NIP from any text before it leaves the process; keep merchant names.
  Unit-tested against realistic Polish description samples (no real data — synthetic).
- [ ] 10.13 Secure API-key storage: extend `IKeyVault` (or a focused `ISecretStore`) with
  get/set for a named secret (`ai.claude.apiKey`, `ai.openai.apiKey`), DPAPI-encrypted on Windows,
  same `CurrentUser` scope as the master-key cache. Keys never logged, never committed (hard rule #6/#11).
  **Decision needed — see Open questions.**
- [ ] 10.14 `AiUsageEntry` table + `IAiUsageLedger` (Core) / `AiUsageLedger` (Infra): record provider,
  model, purpose, token counts, estimated USD + PLN per call; migration `AddAiUsageLedger`.
- [ ] 10.15 `AiBudgetGate` (Infra) per doc 04: compare month-to-date ledger spend + estimate against the
  user cap; `Critical` priority (categorisation during import) is allowed to exceed with a warning;
  non-critical is blocked + notified.
- [ ] 10.16 Settings page (Avalonia, new nav entry in the shell): enter/clear API key (masked),
  pick active provider per use case, set the monthly PLN cap, and show month-to-date AI spend from the
  ledger. Matches the existing light design language.

### Phase 10-C — Hybrid AI categorisation

- [ ] 10.17 `HybridCategorizer` (Infra) implementing `ICategorizer`: **cache → rules → AI batch**.
  Unknown `NormalizedDescription`s are buffered into batches of 20–50, sent via `CompleteJsonAsync`
  with the doc-04 prompt (anonymised, index-array response), parsed, validated, and written to cache
  (`Source = AI`). One JSON-retry on malformed output, then surface an error (never silently mislabel).
- [ ] 10.18 Wire `HybridCategorizer` into the import background flow behind `AiBudgetGate`
  (`AiPriority.Critical`); each AI call writes the ledger. Categorisation stays off the UI thread and
  reports through the existing `IProgress<ImportProgress>` (a "categorising" stage).
- [ ] 10.19 Cache pre-warm: a background job that seeds the cache from existing transactions' resolved
  categories on first launch after the categorisation migration (doc 04 "caching = cost control").
- [ ] 10.20 Failure handling per doc 04: retry with backoff (1s/2s/4s, max 3), honour `Retry-After`,
  on exhaustion queue the unknowns locally and surface a non-blocking "AI niedostępne — w kolejce"
  notice; categorisation resumes later. Bad-JSON path logs the raw response only in opt-in diagnostic mode.
- [ ] 10.21 `OpenAiProvider` (Infra) as the second `IAiProvider` (OpenAI SDK, `json_object` mode),
  selectable in Settings; provider-switch on Claude 429 where a configured fallback exists.

### Tests + bookkeeping (per phase)

- [ ] 10.22 10-A tests: `RuleEngine` (priority order, first-match, invalid-regex skipped),
  `CategoryCacheStore` (exact hit, upsert hit-count, Manual overrides AI/Rule), `RuleCacheCategorizer`
  (cache-then-rule, unknown → null), migration/index persistence (real SQLCipher), and
  `ImportStatementUseCase` categorising the golden PKO CSV (known merchants categorised, unknowns null);
  Transactions VM manual-recategorise + filter tests.
- [ ] 10.23 10-B tests: `PromptAnonymizer` redaction (account/IBAN/NIP gone, merchant kept),
  `AiUsageLedger` accumulation, `AiBudgetGate` (under cap proceeds, over cap blocks non-critical /
  allows critical), secret store round-trip (fake/in-memory vault on non-Windows), Settings VM.
- [ ] 10.24 10-C tests: `HybridCategorizer` with a **fake `IAiProvider`** — cache/rule hits skip AI,
  unknowns batch correctly (size, order preserved), AI results cached with `Source = AI`, malformed
  JSON retries once then errors, budget gate respected; ledger written per batch. No real API calls in CI.
- [ ] 10.25 Each phase: `dotnet build` + `dotnet test` + `dotnet format --verify-no-changes` green
  locally before its PR; full suite stays green.
- [ ] 10.26 Manual DoD (owner, end of 10-C): set a Claude API key + a small PLN cap in Settings, import
  the real PKO CSV → most rows categorised via rules/cache, the remainder via one AI batch within budget;
  re-import the same file → **0 API calls** (all cache hits); re-categorise a row by hand → the new label
  sticks and recurs on the next matching import. Ledger shows the per-import cost.
- [ ] 10.27 `gh issue create` per phase (labels `feat` + `sprint-10`) → `feature/` branch → PR
  `Closes #<phase-issue>` → CI green → squash-merge. Closure issue + `chore/close-sprint-10` after 10-C.

## Delivery — phased PRs (not one monolithic commit)

Three incremental implementation PRs, each its own issue + `feature/` branch + squash-merge, each green
and self-contained. Standard flow: issue → branch → commits → push → `gh pr create` → CI green →
`gh pr merge --squash --delete-branch`. (When several PRs merge in one sitting, branch protection
requires each later PR to be updated against `main` and re-pass CI first.)

- **Phase 10-A — Deterministic core** (`feature/sprint-10a-rules-cache`): steps 10.1–10.9 + the 10-A
  tests (10.22). Deliverable: rules + learned cache categorise imports and existing rows for free;
  grid shows categories, manual re-categorisation works. No AI, no keys.
- **Phase 10-B — AI plumbing** (`feature/sprint-10b-ai-provider`): steps 10.10–10.16 + 10.23.
  Deliverable: `IAiProvider`/`ClaudeProvider`, anonymiser, ledger, budget gate, secure key storage, and
  a Settings page — tested with a fake provider, not yet wired to categorisation.
- **Phase 10-C — Hybrid categorisation** (`feature/sprint-10c-hybrid`): steps 10.17–10.21 + 10.24 +
  the manual DoD (10.26). Deliverable: cache→rules→AI end to end; OpenAI as the second provider.

## Definition of Done

1. `Rule`, `CategoryCache` (+ `CacheSource`), and `AiUsageEntry` exist in `Coffer.Core/Domain`;
   committed migrations create their tables with the doc-02/04 indexes (unique
   `CategoryCache.NormalizedDescription`, `Rule.Priority`), applied at startup with a pre-migration backup.
2. A default Polish category set + starter rule pack seed idempotently on first run; user edits are
   never overwritten.
3. `RuleEngine` + `CategoryCacheStore` + the deterministic categoriser assign categories during import
   for free; unknown transactions persist with `CategoryId = null`.
4. The Transactions grid shows categories and supports manual re-categorisation, which writes a `Manual`
   cache entry that overrides AI/rule on the next encounter; the category filter works.
5. `IAiProvider` (Claude + OpenAI), `PromptAnonymizer`, `AiUsageLedger`, and `AiBudgetGate` are in place;
   API keys are stored encrypted via secure storage and never logged.
6. `HybridCategorizer` categorises unknowns via an anonymised AI batch behind the budget gate, caches
   results, and reports cost to the ledger.
7. Manual DoD met: first import categorises (rules/cache + one AI batch within budget); re-import →
   0 API calls; a hand re-categorisation sticks and recurs.
8. All tests green locally and on CI; no real API calls in tests (fake provider).

## Files affected

**New (Core):**
- `src/Coffer.Core/Domain/Rule.cs`, `CategoryCache.cs`, `CacheSource.cs`, `AiUsageEntry.cs`
- `src/Coffer.Core/Categorization/ICategorizer.cs`, `ICategoryRuleEngine.cs`, `ICategoryCacheStore.cs`
- `src/Coffer.Core/Ai/IAiProvider.cs`, `AiRequest.cs`, `IAiUsageLedger.cs`, `AiPriority.cs`
- secure-secret abstraction (extend `IKeyVault` or new `ISecretStore.cs`) — see Open questions

**New (Infrastructure):**
- `src/Coffer.Infrastructure/Categorization/RuleEngine.cs`, `CategoryCacheStore.cs`,
  `RuleCacheCategorizer.cs`, `HybridCategorizer.cs`, `DefaultCategorySeed.cs`
- `src/Coffer.Infrastructure/AI/PromptAnonymizer.cs`, `ClaudeProvider.cs`, `OpenAiProvider.cs`,
  `AiUsageLedger.cs`, `AiBudgetGate.cs`
- `src/Coffer.Infrastructure/Persistence/Configurations/*` (Rule, CategoryCache, AiUsageEntry)
- `src/Coffer.Infrastructure/Persistence/Migrations/*_AddCategorizationSchema.*`,
  `*_AddAiUsageLedger.*`

**New (Application/Desktop):**
- `src/Coffer.Application/ViewModels/Settings/SettingsViewModel.cs`
- `src/Coffer.Desktop/Views/Settings/*`

**Modified:**
- `src/Coffer.Core/Domain/Category.cs` (optional `ParentId`)
- `src/Coffer.Infrastructure/Import/ImportStatementUseCase.cs` (categorisation step + summary count)
- `src/Coffer.Application/ViewModels/Transactions/TransactionsViewModel.cs` (category render, manual
  recategorise, working filter, recategorise-existing command)
- `src/Coffer.Desktop/Views/Transactions/*`, `MainWindow.*` (Settings nav entry)
- `ServiceRegistration.cs` in Application / Infrastructure / Desktop
- `src/Coffer.Core/Import/ImportSummary.cs` (categorised count)
- `docs/architecture/02-database-and-encryption.md` / `04-ai-strategy.md` if the realised schema or
  pipeline diverges from the docs

## Decisions (resolved at planning, 2026-06-05)

- **Scope:** full Phase 4 as one Sprint 10, shipped as three phased PRs (10-A → 10-C). 10-A is
  independently shippable and pausable (deterministic, no keys/cost), per the not-time-boxed convention.
- **Provider priority:** Claude first (owner uses Anthropic; `Anthropic.SDK` already referenced), OpenAI
  second in 10-C.
- **Categorisation timing:** runs in the import background task (cache+rules always; AI batch behind the
  budget gate at `Critical` priority), plus a one-off "recategorise existing" path for already-imported rows.
- **Default content:** ship an opinionated Polish category set + starter rule pack, seeded idempotently
  (never overwrites user edits).

## Open questions

- **Secret storage shape:** extend the existing `IKeyVault` with named get/set, or add a dedicated
  `ISecretStore`? `IKeyVault` is documented as the umbrella for "OAuth refresh tokens etc. in their
  respective sprints", which argues for extending it; a separate interface keeps the master-key vault
  focused. Resolve at the start of 10-B.
- **Category hierarchy:** doc 02 has an optional `Category.ParentId`. Defer (flat categories) unless a
  parent/child need appears during 10-A?
- **AI cost confirmation UX:** for a first big import with many unknowns, do we show a pre-flight
  "~X PLN to categorise N transactions, proceed?" prompt, or rely solely on the silent budget gate +
  ledger? Lean pre-flight for the first import only.
- New questions that surface during a phase are logged in `log.md`.
