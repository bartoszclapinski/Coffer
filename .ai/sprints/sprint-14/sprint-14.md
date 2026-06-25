# Sprint 14 — Financial advisor

**Phase:** 9
**Status:** Planned
**Depends on:** sprint-9 (transactions/accounts), sprint-10 (categories + AI plumbing: ledger/budget gate/anonymizer), sprint-11 (server-side aggregation for the `FinancialContext` builder), sprint-12 (chat tools registry), sprint-13 (commentator pattern reused for the advisor report)

## Goal

Coffer turns the owner's transaction history into forward-looking guidance: the owner creates savings **goals** ("Wakacje Grecja — 8000 zł do lipca 2026") on a desktop **Doradca** page and the app reports a deterministic projection (status, projected date, required monthly saving), lets them simulate "what if I save X/month", and surfaces AI-written risks plus grounded cutting suggestions ("Restauracje do średniej 6 mies. → +329 zł/mies."). **The engine calculates; the AI only explains** — every number comes from deterministic C#, never the LLM (doc 07, non-negotiable).

## Approach — three PRs (Sprint-10/12/13 cadence, "deterministic before AI")

- **14-A — feasibility engine (no UI, no AI).** Domain `Goal` / `GoalContribution` / `GoalSnapshot` + migration, the `GoalFeasibilityEngine` with one `GoalStrategy` per `GoalType` (six strategies), the `MortgagePrepaymentCalculator`, and a `FinancialContext` builder that derives income / fixed / variable-avg / category-averages from the transaction history (reusing Sprint-11 server-side aggregation). Pure, deterministic, exhaustively unit- and property-tested; zero AI cost.
- **14-B — Avalonia Doradca page.** Goals list + detail panel, manual goal CRUD and contributions, a simulator slider (monthly-saving → live projection), alternative scenarios, and a 12-month projection chart (LiveCharts2), wired into the shell. Reads the engine; still no AI.
- **14-C — AI layer + chat integration.** An `AdvisorReportGenerator` feeds engine outputs to the LLM for per-goal risks and grounded cutting suggestions (Sonnet, budget-gated, ledgered as `advisor-report`, anonymised), persisted as a daily `AdvisorReport`; a daily snapshot job writes `GoalSnapshot` rows at startup; and a `GetGoals` chat tool slots into the Phase-7 registry (realising the Sprint-12 deferral).

**Engine windows / conventions:** `FinancialContext` uses a **6-month moving average** for variable expenses and per-category averages (matching doc 07's `CategoryAverages6m` / `MonthlyVariableAvg`). Money stays `decimal` (hard rule #1); `TargetDate` / contribution `Date` are `DateOnly`, snapshot/report timestamps are UTC `DateTime` (hard rule #2). Goal enums persist as strings. The advisor **never** predicts returns, recommends instruments, or gives tax advice (doc 07 "what this advisor never does" + CLAUDE.md "what this app is NOT").

## Steps

### 14-A — feasibility engine

- [ ] 14.1 Domain (`Coffer.Core/Goals/`): `GoalType` enum (`Purchase`, `LargeExpense`, `EmergencyFund`, `MortgagePrepayment`, `Investment`, `LongTerm`), `GoalStatus` enum (`OnTrack`, `NeedsAttention`, `AtRisk`, `Late`, `Achieved`, `Paused`), `Priority` enum (`Low=1`, `Medium=2`, `High=3`), `ContributionSource` enum (`Manual`, `LinkedTransaction`, `Tag`, `AutoDetect`).
- [ ] 14.2 Domain entities (`Coffer.Core/Domain/`): `Goal` (`Id`, `Name`, `Type`, `decimal TargetAmount`, `Currency`, `DateOnly TargetDate`, `Priority`, `string? Notes`, `bool IsArchived`, `DateTime CreatedAt`, nav `Contributions`/`Snapshots`), `GoalContribution` (`Id`, `GoalId`, `decimal Amount`, `DateOnly Date`, `ContributionSource Source`, `Guid? TransactionId`), `GoalSnapshot` (`Id`, `GoalId`, `DateOnly Date`, `decimal CurrentAmount`, `decimal MonthlySaving`, `DateOnly ProjectedDate`, `GoalStatus Status`, `decimal ConfidenceScore`).
- [ ] 14.3 Persistence: `GoalConfiguration` / `GoalContributionConfiguration` / `GoalSnapshotConfiguration` (`decimal(18,2)` on money, enums as strings, FK + cascade on `GoalId`, index on `Goal.IsArchived` and `GoalSnapshot (GoalId, Date)`), `DbSet`s on `CofferDbContext`, `ApplyConfiguration`. Migration `AddGoals` (pre-migration backup runs automatically — hard rule #8).
- [ ] 14.4 Engine contracts (`Coffer.Core/Goals/`): `FinancialContext` (income, fixed, variable avg + stddev, other active goals, `CategoryAverages6m`, `SeasonalityModifiers`, `Today`), `GoalFeasibilityResult` (status, projected date, required/current monthly saving, confidence, `AlternativeScenarios`, `Risks`, `DiagnosticSummary`), `Scenario` and `RiskFactor` records, abstract `GoalStrategy` (`Type`, `Evaluate(goal, ctx)`).
- [ ] 14.5 Six strategies (`Infrastructure/Goals/Strategies/`, pure + synchronous):
  - [ ] 14.5.a `PurchaseGoalStrategy` — remaining / months-available vs free cash; OnTrack/NeedsAttention/AtRisk thresholds per doc 07.
  - [ ] 14.5.b `LargeExpenseStrategy` — Purchase + `SeasonalityModifiers` applied to the projection.
  - [ ] 14.5.c `EmergencyFundStrategy` — target = multiple (default 6×) of monthly expenses; target re-derived from current expenses each evaluation.
  - [ ] 14.5.d `MortgagePrepaymentStrategy` — wraps `MortgagePrepaymentCalculator`, shows **both** shorten-vs-reduce outcomes, recommends neither.
  - [ ] 14.5.e `InvestmentStrategy` — deliberately limited: free-cash-to-invest + committed-vs-invested + inflation opportunity-cost; no instrument advice, no return prediction.
  - [ ] 14.5.f `LongTermStrategy` — 5y+ horizon with inflation modelling (nominal vs real target); no investment advice.
- [ ] 14.6 `MortgagePrepaymentCalculator` (`Infrastructure/Goals/`): standard amortization with extra principal; outputs interest saved, new payment (reduce mode) or new payoff date (shorten mode), break-even. Pure math, no strategy decisions.
- [ ] 14.7 `GoalFeasibilityEngine` (`Infrastructure/Goals/`): strategy dictionary keyed by `GoalType`, `Evaluate(goal, ctx)` and `EvaluateAll(goals, ctx)` (sets `ctx.OtherActiveGoals` per goal excluding self, skips archived).
- [ ] 14.8 `IFinancialContextBuilder` + implementation (`Infrastructure/Goals/`): derives income / fixed / 6-month variable avg + stddev / `CategoryAverages6m` / seasonality from the transaction history via server-side aggregation (Sprint-11 patterns). PLN-scoped. Registered with the engine + strategies via a new `AddCofferGoals()` in `ServiceRegistration`.
- [ ] 14.9 Tests: `[Theory]` status tables per strategy over synthetic contexts (doc 07's table as a starting set), `MortgagePrepaymentCalculator` cases against hand-computed amortization, and FsCheck property tests — saved amount monotonic ⇒ projected date never regresses; per-goal feasibility sum cannot exceed free cash beyond the defined ratio; archived goals are never evaluated.

### 14-B — Avalonia Doradca page

- [ ] 14.10 Read/command side: `IGoalsQuery` (active + archived with latest result) and `IGoalService` (create / edit / archive goal, add / remove contribution), implementations in `Infrastructure/Goals/`, registered in `AddCofferGoals()`.
- [ ] 14.11 UI: `GoalsViewModel` + `GoalDetailViewModel` (`Coffer.Application/ViewModels/Goals/`) with `LoadAsync`, goal CRUD commands, an `AddContributionCommand`, a simulator (`MonthlySavingInput` → recomputed `GoalFeasibilityResult`), scenario list, and a 12-month projection series; empty/loading/error states.
- [ ] 14.12 Views: `GoalsView.axaml` (list with status badge, target, projected date, priority) + detail panel (simulator slider, scenarios, `CartesianChart` 12-month projection) matching the dashboard design language and `docs/mockups`.
- [ ] 14.13 Shell wiring: `Doradca` sidebar entry + `IsAdvisorActive`/`ShowAdvisor` in `MainViewModel`, `DataTemplate` + nav button in `MainWindow.axaml`, transient DI in `DesktopServiceRegistration`.
- [ ] 14.14 Tests: `GoalsViewModel` / `GoalDetailViewModel` tests with fakes (load, create/archive, simulator recompute, scenario projection); `MainViewModelTests` updated for the new shell page.

### 14-C — AI layer + chat integration

- [ ] 14.15 `AiPurpose.AdvisorReport = "advisor-report"` constant; `AdvisorReport` + `AdvisorSuggestion` entities (`Coffer.Core/Domain/`) storing the day's per-goal risks + suggestions (title, `decimal Savings`, description, `CategoryAffected`); `AdvisorReportConfiguration` + migration `AddAdvisorReports`.
- [ ] 14.16 `IAdvisorReportGenerator` + `AdvisorReportGenerator` (`Infrastructure/AI/`): feed `GoalFeasibilityResult`s + top-categories-above-6m-average to the provider (`AiDefaults.ChatModel`), **anonymised** prompt (hard rule #7), gated via `IAiBudgetGate` (`AiPriority.Normal`), metered once as `AiPurpose.AdvisorReport`, parse the `{perGoalRisks, suggestions}` JSON. On any failure persist engine-only results with no AI text (graceful fallback). Suggestions must cite a category + comparison; decline tax/investment asks.
- [ ] 14.17 Daily snapshot job (`Infrastructure/Goals/`): on app startup (once per day) run `EvaluateAll`, write `GoalSnapshot` rows, and regenerate the day's `AdvisorReport` (LLM not called on every UI refresh). Registered as a hosted/startup task in `DesktopServiceRegistration`.
- [ ] 14.18 `GetGoalsTool : ChatTool` (`Infrastructure/Chat/`) — returns active goals with their latest projection (status, target, projected date, required/current monthly saving); register `AddTransient<IChatTool, GetGoalsTool>()` in `AddCofferChat`. Realises the `GetGoals` tool deferred in Sprint 12.
- [ ] 14.19 Tests: `AdvisorReportGenerator` with a scripted fake provider (anonymised prompt, ledger metered as `advisor-report`, budget gate blocks over cap, fallback to engine-only on bad JSON/offline, numbers never invented); `GetGoalsTool` over a real SQLCipher DB + a DI-discoverability case proving the tool is found by `ChatService`; a snapshot-job test asserting one `GoalSnapshot` per active goal per day (idempotent within a day).

## Definition of Done

- **14-A (automated):** each strategy has a `[Theory]` asserting the correct `GoalStatus` for planted contexts; the mortgage calculator matches hand-computed amortization; property tests hold (monotonic saved ⇒ non-regressing projected date, free-cash bound, archived-skipped).
- **14-B (manual):** create "Wakacje Grecja — 8000 zł do lipca 2026" on the **Doradca** page → the engine reports a realistic projection; move the simulator slider → status, projected date, and the 12-month chart update live; archive the goal → it leaves the active list.
- **14-C (automated):** the report generator meters exactly one `advisor-report` ledger entry per run and falls back to engine-only text when the provider errors; the snapshot job writes one snapshot per active goal per day; the `GetGoals` tool returns goals and is discoverable by `ChatService`.
- **14-C (manual):** open Doradca → AI suggests 2–3 specific cuts grounded in actual category history (each citing a category + comparison); ask the assistant "jak idzie mój cel na wakacje" → it invokes `GetGoals` (visible in the tool-trace) and answers with the engine's number.

## Files affected

- `src/Coffer.Core/Goals/` (new): `GoalType.cs`, `GoalStatus.cs`, `Priority.cs`, `ContributionSource.cs`, `FinancialContext.cs`, `GoalFeasibilityResult.cs`, `Scenario.cs`, `RiskFactor.cs`, `GoalStrategy.cs`, `IGoalsQuery.cs`, `IGoalService.cs`, `IFinancialContextBuilder.cs`, `IAdvisorReportGenerator.cs`
- `src/Coffer.Core/Domain/`: `Goal.cs`, `GoalContribution.cs`, `GoalSnapshot.cs`, `AdvisorReport.cs`, `AdvisorSuggestion.cs` (new)
- `src/Coffer.Core/Ai/AiDefaults.cs` (`AiPurpose.AdvisorReport`)
- `src/Coffer.Infrastructure/Persistence/CofferDbContext.cs`, `Configurations/*` (new goal/report configs), `Migrations/*_AddGoals.*` + `*_AddAdvisorReports.*` (new)
- `src/Coffer.Infrastructure/Goals/` (new): the six strategies, `MortgagePrepaymentCalculator.cs`, `GoalFeasibilityEngine.cs`, `FinancialContextBuilder.cs`, `GoalsQuery.cs`, `GoalService.cs`, daily snapshot job
- `src/Coffer.Infrastructure/AI/AdvisorReportGenerator.cs` (new, 14-C)
- `src/Coffer.Infrastructure/Chat/GetGoalsTool.cs` (new, 14-C)
- `src/Coffer.Infrastructure/DependencyInjection/ServiceRegistration.cs` (`AddCofferGoals`, register tool + report generator)
- `src/Coffer.Application/ViewModels/Goals/` (new): `GoalsViewModel.cs`, `GoalDetailViewModel.cs`
- `src/Coffer.Application/ViewModels/Main/MainViewModel.cs`
- `src/Coffer.Desktop/Views/GoalsView.axaml(.cs)` (new), `MainWindow.axaml`, `DependencyInjection/DesktopServiceRegistration.cs`
- `tests/Coffer.Infrastructure.Tests/Goals/`, `tests/Coffer.Application.Tests/ViewModels/Goals/` (new)

## Open questions

All four resolved by the owner (2026-06-25), recorded as decisions in `log.md`:
- **Default aggressiveness profile** → **Balanced only** in v1; Conservative/Aggressive deferred (see "Deferred to a follow-up" below).
- **Goal-transaction linkage scope** → **manual + tag** only; `ContributionSource.AutoDetect` stays modelled in the enum but unwired.
- **Seasonality source** → **neutral 1.0 stub** for `SeasonalityModifiers` this sprint; real per-month modelling deferred (see below).
- **Snapshot job host** → **desktop startup task** once per day, no background scheduler (lands in 14-C).

## Deferred to a follow-up

Explicitly out of Sprint 14 but to be picked up later within Phase 9 (or a Phase-9 follow-up sprint):
- **Conservative / Aggressive aggressiveness profiles** — only Balanced ships in v1. The strategies must take the free-cash buffer / risk multipliers as inputs so the other two profiles drop in later without changing strategy logic or contracts.
- **Real seasonality model** — `SeasonalityModifiers` is a 1.0 stub this sprint; deriving per-month modifiers from historical spend (so `LargeExpenseStrategy` adjusts for "save less in December") is a separate, testable follow-up.
- **Savings-account auto-detect linkage** — `ContributionSource.AutoDetect` is modelled but unwired; needs the "savings sub-account associated with a goal" concept first.
