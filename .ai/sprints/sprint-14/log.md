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
