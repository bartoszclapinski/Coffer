# Sprint 14 log

## 2026-06-25

- Plan written (`docs/plan-sprint-14`). Sprint 14 = Phase 9 (Financial advisor): a desktop
  **Doradca** page where the owner creates savings goals and sees a deterministic projection,
  simulates monthly saving, and reads AI-written risks + grounded cutting suggestions. The last
  desktop phase before the mobile-dependent ones (3 Sync, 5 Receipts).
- Decisions (planning):
  - **Three PRs** ("deterministic before AI", Sprint-10/12/13 cadence): 14-A = domain + migration +
    `GoalFeasibilityEngine` (six strategies) + `MortgagePrepaymentCalculator` + `FinancialContext`
    builder, no UI/AI; 14-B = Avalonia Doradca page (goals CRUD, simulator slider, scenarios,
    12-month projection chart); 14-C = `AdvisorReportGenerator` (LLM risks + cutting suggestions,
    budget-gated, ledgered as `advisor-report`, anonymised) + daily snapshot job + the `GetGoals`
    chat tool deferred in Sprint 12.
  - **Engine calculates, AI explains** (doc 07, non-negotiable): every number is deterministic C#;
    the LLM only writes Polish risk/suggestion text grounded in engine outputs and never invents
    figures.
  - **`FinancialContext` 6-month moving average** for variable + per-category averages, derived via
    Sprint-11 server-side aggregation. Reasoning-tier model (Sonnet) for the report.
  - **New ledger purpose `advisor-report`** added to `AiPurpose` (the existing five are
    categorization/chat/vision/parser-fallback/anomaly-comment).
- Scope cuts (mirroring Sprint 13's desktop-first trims), recorded as proposals pending owner
  confirmation in the plan's Open questions:
  - **Mobile push notifications out of scope** — desktop-first.
  - **Aggressiveness profile = Balanced only** in v1; Conservative/Aggressive multipliers deferred.
  - **Goal-transaction linkage = manual + tag only**; savings-account auto-detect modelled in the
    `ContributionSource` enum but not wired.
  - **Daily snapshot job = desktop startup task** (once per day), no background scheduler — matching
    the Sprint-13 "post-import + manual" stance.
- Open questions (4) recorded in the plan for the owner: default aggressiveness profile, linkage
  scope, seasonality source (real vs 1.0 stub for `LargeExpenseStrategy`), and snapshot-job host.

## 2026-06-25 — planning questions resolved (owner)

- **Aggressiveness profile = Balanced only** in v1. Conservative/Aggressive deferred — owner asked
  to record them as a follow-up so they are not lost. Captured under "Deferred to a follow-up" in
  the plan. Strategies must accept the free-cash buffer / risk multipliers as inputs so the other
  two profiles drop in later without touching strategy logic.
- **Goal-transaction linkage = manual + tag** only; `ContributionSource.AutoDetect` stays modelled
  but unwired in v1.
- **Seasonality = neutral 1.0 stub** for `SeasonalityModifiers` this sprint; real per-month
  modelling deferred (also under "Deferred to a follow-up").
- **Daily snapshot job = desktop startup task** once per day, no background scheduler (implemented
  in 14-C).
- None open. 14-A (engine) starts next.

## 2026-06-25 — 14-A engine implemented (PR pending)

- Shipped the deterministic engine: six `GoalStrategy` types (Purchase, LargeExpense, EmergencyFund,
  MortgagePrepayment, Investment, LongTerm) over a shared `EvaluateSavingsGoal` base, the
  `GoalFeasibilityEngine` (`Evaluate` + `EvaluateAll` with cross-goal free-cash pull, skips
  archived), the `MortgagePrepaymentCalculator`, the `FinancialContextBuilder`, the `Goal`/
  `GoalContribution`/`GoalSnapshot` entities + EF configs + `AddGoals` migration, and `AddCofferGoals`
  DI. 20 new Goals tests (status tables, calculator vs hand-computed annuity, two FsCheck properties)
  plus updated `MigrationRunnerTests` for the new migration; full suite green (294 infra + 82 app).
- Deviation 1 — **`SeasonalityModifiers` keyed by `int` month (1–12)**, not the doc's `DateOnly`.
  A per-calendar-month modifier is what the v1 stub and the deferred real model both need; a date key
  would force callers to materialise every day. `SeasonalityFor(int month)` returns the neutral 1.0.
- Deviation 2 — **`MortgagePrepaymentStrategy` does savings-feasibility, not amortization.** The goal
  accumulates the planned prepayment amount (fixed-target feasibility like Purchase). The
  shorten-vs-reduce comparison is produced separately by `MortgagePrepaymentCalculator` from loan
  terms the 14-B UI supplies (principal, rate, months remaining) — those terms are not stored on the
  `Goal`. Keeps the entity lean and the calculator pure.
- Deviation 3 (minor) — `ProjectDateAtPace` caps at 12 000 months and returns `DateOnly.MaxValue`
  beyond that; an FsCheck property surfaced a `DateOnly.AddMonths` overflow at near-zero pace.
- Deviation 4 (minor) — `GoalFeasibilityResult` carries a `GoalId` (not in the doc sketch) so
  `EvaluateAll` can map results back to goals. Variable std-dev's square root drops to `double`
  (then back to `decimal`), matching the anomaly engine's convention for statistical spread; all
  stored/compared money stays `decimal`.

## 2026-06-25 — 14-B Doradca page implemented (PR pending)

- Shipped the Avalonia **Doradca** page end to end: `IGoalsQuery`/`IGoalService` (Core) +
  `GoalsQuery`/`GoalService` (Infrastructure, `IDbContextFactory`, contributions soft-deleted via
  `IsArchived`), `GoalsViewModel` + `GoalDetailViewModel` + `GoalScenarioViewModel`
  (CommunityToolkit.Mvvm), `GoalsView.axaml` (goals list, create form, metrics, simulator slider,
  12-month LiveCharts projection, scenarios, risks, add-contribution), and shell wiring
  (`MainViewModel.Advisor`/`ShowAdvisor`, `MainWindow.axaml` nav button + data template, desktop DI).
- Engine extension for the simulator: added `GoalStrategy.Simulate` / `IGoalFeasibilityEngine.Simulate`
  (re-runs the projection at an arbitrary monthly pace) and an `EffectiveTarget` field on
  `GoalFeasibilityResult` (the strategy-resolved target — e.g. EmergencyFund's 6× expenses — so the
  UI charts the real goal line, not the stated one). The VM never duplicates engine math: the slider
  calls `engine.Simulate`.
- Deviation — **`GoalsQuery` orders client-side, not in SQL.** `Goal.Priority` is persisted as a
  string (`HasConversion<string>()`), so `OrderByDescending(g => g.Priority)` in the DB sorts
  alphabetically (Medium > Low > High) instead of by urgency. A `GoalsQueryTests` ordering test
  caught this; the query now materialises active goals then orders in memory where the enum compares
  by its int value. The active-goals set is small, so the cost is negligible.
- Single VM for both list and detail: one `GoalDetailViewModel` serves the list row and the detail
  panel (`Goals` collection + `SelectedGoal`), avoiding a separate row VM. `StatusColor` is exposed
  as a hex string consumed by the existing `HexColorToBrushConverter` rather than a new converter.
- Tests: 6 `GoalsViewModelTests`, 5 `GoalDetailViewModelTests`, `MainViewModelTests` updated for the
  new shell page (Application layer), plus `GoalsQueryTests` (3) and `GoalServiceTests` (6) over a
  real SQLCipher DB and 4 new engine tests (Simulate monotonicity + unregistered-type throw,
  EmergencyFund `EffectiveTarget`). Full suite green (306 infra + 94 app + 8 core).
- **UI not interactively verified by the agent** — the manual DoD (create "Wakacje Grecja" 8000 zł,
  move the simulator slider, archive a goal) must be exercised on the owner's machine before merge.

## 2026-06-25 — 14-C advisor AI + chat integration (PR pending)

- Shipped the AI layer: `AiPurpose.AdvisorReport = "advisor-report"`; `AdvisorReport` +
  `AdvisorSuggestion` entities (one report per day, `AdvisorEntryKind` discriminator: `Risk` ties a
  `GoalId` + description, `Suggestion` carries title / `decimal Savings` / description /
  `CategoryAffected`) with EF configs (unique index on `Date`, cascade FK, enum-as-string) and the
  `AddAdvisorReports` migration; `IAdvisorReportGenerator` + `AdvisorReportGenerator`
  (`Infrastructure/AI/`) mirroring the 13-B commentator pattern — budget gate (`AiPriority.Normal`) →
  anonymised prompt (hard rule #7) → `CompleteJsonAsync` → metered once as `advisor-report`, with a
  graceful engine-only fallback (`GeneratedByAi=false`) on a blocked gate, a provider throw, or
  malformed JSON. The fallback never fabricates suggestion numbers; risks keyed by an unknown goal id
  are dropped.
- Daily snapshot job (`Infrastructure/Goals/GoalSnapshotJob.cs`, `IGoalSnapshotJob`): idempotent
  within a day (gated on whether any `GoalSnapshot` exists for the date), writes one snapshot per
  active goal, builds `CategorySpending` (current-month debits vs `CategoryAverages6m`, anchored on
  the latest transaction date), regenerates the day's report, and replaces any existing report for
  the day. Wired as a fire-and-forget desktop-startup task in `App.axaml.cs` (off the bootstrap
  thread because it may make an LLM call) — once per day, no background scheduler (per the resolved
  open question).
- `GetGoalsTool : ChatTool` (`Infrastructure/Chat/`) — realises the `GetGoals` tool deferred in
  Sprint 12. Returns active goals with the **live** engine projection (status, target, projected
  date, required/current monthly saving) rather than the persisted snapshot, since `GoalSnapshot`
  lacks `RequiredMonthlySaving`. Registered `AddTransient<IChatTool, GetGoalsTool>()` in
  `AddCofferChat`; the `FindAnomaliesTool` discoverability test now also calls `AddCofferGoals()`
  because enumerating `IChatTool` constructs `GetGoalsTool`'s goals-engine dependencies.
- **Doradca UI wiring (DoD).** Added `IAdvisorReportQuery` + `AdvisorReportQuery` (latest report by
  date, entries included), an `AdvisorSuggestionViewModel` (formats title / "+N zł/mies." savings /
  category), and surfaced the day's suggestions in `GoalsViewModel` (`Suggestions` collection,
  `HasSuggestions`, `SuggestionsAreEngineOnly`). `GoalsView.axaml` now shows a "Sugestie doradcy —
  gdzie przyciąć" card at the top of the right column (always visible, independent of goal
  selection), each suggestion citing its category badge and monthly savings. Only `Suggestion`
  entries are surfaced; per-goal risks stay on the deterministic engine card.
- Tests: 8 `AdvisorReportGeneratorTests` (scripted fake provider — meters once, anonymises prompt,
  budget-denied/throw/malformed-JSON → engine-only, no fabricated numbers, unknown-goal risk dropped,
  no-results short-circuit), 3 `GetGoalsToolTests` + 1 DI-discoverability over a real SQLCipher DB,
  3 `GoalSnapshotJobTests` (one snapshot per active goal, archived skipped, idempotent within a day),
  2 new `GoalsViewModelTests` (suggestions surfaced from the latest report; none when no report).
  `MigrationRunnerTests` updated for `AddAdvisorReports`. Full suite green (320 infra + 96 app + 8 core).
- **UI not interactively verified by the agent** — the manual DoD ("open Doradca → AI suggests 2–3
  specific cuts grounded in actual category history"; "ask the assistant 'jak idzie mój cel na
  wakacje' → it invokes `GetGoals`") must be exercised on the owner's machine before merge.
