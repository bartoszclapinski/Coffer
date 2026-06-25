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
