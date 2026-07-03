# Sprint 21 — Over-budget alert + `GetBudgetStatus` chat tool (the Sprint-20 tail)

**Phase:** — (beyond-roadmap; the optional **20-C** deferred at the close of Sprint 20 — the "over-budget alert detector + `GetBudgetStatus` chat tool" follow-up. Reuses the Sprint-13 anomaly engine per `docs/architecture/04-ai-strategy.md` and the Sprint-12 chat-tool pattern.)
**Status:** Planned
**Depends on:** sprint-20 (`CategoryBudget`, `BudgetTrackingEngine`, `IBudgetTrackingQuery`/`BudgetOverview`), sprint-13 (`IAnomalyDetector` engine, `Alert`, `AnomalyDetectionService` dedup+persist pipeline, the Alerty page), sprint-12 (the `ChatTool` base + `ChatService` tool-call loop)

## Goal

The two budget-surfacing hooks deferred from Sprint 20 land, so an over-budget category is not only visible on the dedicated Budgets page but also **surfaces on the Alerty page** and can be **asked about in chat**:

1. an **over-budget alert** — when a category's month-to-date spend crosses its `CategoryBudget` limit, an `Alert` appears on the Alerty page (one per category per month, dismissable, never resurrected), and
2. a read-only **`GetBudgetStatus` chat tool** — the assistant can answer "how am I doing against my budgets this month?" grounded in `IBudgetTrackingQuery` (limit, spent, remaining, projected, zone per category + the unbudgeted lines).

Both are deterministic: the tracking engine calculates, the alert text is templated, the chat tool only shapes query output. **No new AI cost** (the tool is read-only like the others; over-budget detection is arithmetic).

## Why this sprint exists

Sprint 20 shipped budgets on their own page, but budget state is inert everywhere else: the app already notices unusual spend (Alerty) and answers finance questions in prose (chat), yet neither knows about budgets. Crossing a self-set limit is exactly the kind of event the alert engine exists to raise, and "am I over budget?" is exactly the kind of question the chat assistant exists to answer. Both reuse machinery that already exists — no new subsystem, no UI, no migration.

## Design decisions (the shape we commit to)

- **Over-budget fires on `Over` only, not `Warning`.** The alert is raised when month-to-date spend ≥ limit (`BudgetZone.Over`) — a discrete, actionable "you crossed it" event. "Approaching" (≥ 80% or projected-over) stays on the Budgets page as a coloured bar; turning it into an alert would fire mid-month, every month, for every healthy budget — noise. v1 alerts only on the crossing.
- **One alert per (category, month); dismissed stays dismissed.** Signature `over-budget:{categoryId}:{yyyyMM}` (month = the dashboard-anchored current month). The Sprint-13 pipeline already dedups by signature and never resurrects a dismissed signature, so re-running detection the same month is idempotent, and crossing again next month raises a fresh alert. Re-categorising spend below the limit does **not** retract an existing alert in v1 (alerts are point-in-time findings, consistent with every other detector).
- **The detector stays pure; the service feeds it the budget overview.** `IAnomalyDetector.Detect` is synchronous over `AnomalyDetectionContext` and cannot call the async `IBudgetTrackingQuery`. Rather than duplicate the month-to-date-per-category aggregation inside the detector, `AnomalyDetectionService` (already async and DB-bound) calls the **existing** `IBudgetTrackingQuery.GetOverviewAsync` once and hands the resulting `BudgetOverview` to the context; `OverBudgetDetector` just filters `Zone == Over` and emits candidates. The query is reused wholesale — no re-implementation of the anchoring/summing logic. (Alternative considered: raise alerts directly from the query, bypassing `IAnomalyDetector` — rejected, it would fork the persist/dedup pipeline the Alerty page depends on.)
- **Alert text is Polish, templated, baked at detection time.** Same convention as all five Sprint-13 detectors (`AnomalyFormatting.Pln`/`Category`). Runtime localization of alert text is a known, separately-tracked gap (#128) — this sprint follows the existing convention and does **not** attempt to fix it. So: no new `.resx` keys.
- **No UI, no migration.** `AlertsView` renders each alert's `Title`/`Description` generically (it does not switch on `AnomalyType`), so a new alert type appears with zero XAML/VM changes. `ChatService` discovers any registered `IChatTool` automatically. The `CategoryBudgets` table already exists (Sprint 20). Tool output is anonymized centrally by `ChatService` (rule #7) — the tool returns plain JSON.
- **`GetBudgetStatus` is parameterless.** Mirrors `GetGoalsTool`: returns the current-month overview (every budgeted category's status + the unbudgeted lines). Per-account scoping and a date/month parameter are deferred — the overview is the current dashboard-anchored month, which is what "am I overspending *this month*?" means.

## Approach — plan PR, then one headless implementation PR

- **Plan (this doc)** — a docs PR, per the sprint rhythm.
- **21-A — over-budget detector + `GetBudgetStatus` tool (headless).** Both hooks in one PR: they are small, share the "surface budget state" theme, touch only `Coffer.Core` + `Coffer.Infrastructure` + tests, and neither has a UI. Split into two PRs only if review flags it as too large.

## Steps

### 21-A — over-budget alert + chat tool

- [ ] 21.1 Add `OverBudget` to the `AnomalyType` enum (`Coffer.Core/Anomalies/AnomalyType.cs`) — persisted as the enum name on `Alert.Type`.
- [ ] 21.2 Extend `AnomalyDetectionContext` (`Coffer.Core/Anomalies/`) with an optional `BudgetOverview? Budgets` (default `null`) so existing detectors and their tests are unaffected; other detectors ignore it.
- [ ] 21.3 `AnomalyDetectionService` (`Coffer.Infrastructure/Anomalies/`) injects `IBudgetTrackingQuery`, calls `GetOverviewAsync(accountId: null, ct)` once while building the context, and passes the `BudgetOverview` in. Existing ctor-based tests updated to supply the new dependency.
- [ ] 21.4 `OverBudgetDetector : IAnomalyDetector` (`Coffer.Infrastructure/Anomalies/Detectors/`): for each `BudgetLine` whose `Status.Zone == Over`, emit an `AnomalyCandidate` — `AnomalyType.OverBudget`, `Score` = overspend magnitude (`Spent - Limit`), signature `over-budget:{categoryId}:{yyyyMM}`, Polish templated `Title`/`Description` (category name, limit, spent, overspend via `AnomalyFormatting.Pln`), `PeriodFrom`/`PeriodTo` = the anchored month's first/last day, and a `Context` dict of raw numbers for the 13-B commentator. Emits nothing when `context.Budgets` is null/empty.
- [ ] 21.5 Register the detector: one line in `ServiceRegistration.AddCofferAnomalies` (`services.AddTransient<IAnomalyDetector, OverBudgetDetector>()`).
- [ ] 21.6 `GetBudgetStatusTool : ChatTool` (`Coffer.Infrastructure/Chat/`): inject `IBudgetTrackingQuery`, `Name = "GetBudgetStatus"`, Polish `Description`, empty parameter schema (`{}`); `RunAsync` calls `GetOverviewAsync(null, ct)` and projects each `BudgetLine` (category, limit, spent, remaining, fraction, projected, `Zone.ToString()`) + the unbudgeted lines into an anonymous JSON object (amounts PLN, month `yyyy-MM`). Empty-budgets shape handled (`count = 0`).
- [ ] 21.7 Register the tool: one line in `ServiceRegistration.AddCofferChat` (`services.AddTransient<IChatTool, GetBudgetStatusTool>()`).
- [ ] 21.8 Tests:
  - `Coffer.Infrastructure.Tests/Anomalies/AnomalyDetectorTests.cs` — `OverBudgetDetector` over a hand-built context: only `Over` lines emit, `Warning`/`Ok` do not; signature is month-stable; empty/null budgets emit nothing; the templated text carries category + amounts.
  - `Coffer.Infrastructure.Tests/Anomalies/AnomalyDetectionServiceTests.cs` — end-to-end over a real SQLCipher DB: a category whose current-month debits exceed its `CategoryBudget` produces exactly one persisted `Over-budget` `Alert`; a second detection run adds no duplicate; a dismissed one is not resurrected.
  - `Coffer.Infrastructure.Tests/Chat/GetBudgetStatusToolTests.cs` — real DB: seed a budget + current-month transactions, assert the projected JSON fields (zone, spent, limit, projected) and the unbudgeted bucket; assert the tool is discoverable through `AddCofferChat`.

### Sweep

- [ ] 21.9 No new user-facing literals leak untemplated: alert text follows the existing `AnomalyFormatting` Polish templates (localization deferred, #128); the chat tool's `Description` is Polish like its siblings. `dotnet format --verify-no-changes` clean.
- [ ] 21.10 Manual DoD click-through (below) — expected to defer to manual (needs a running desktop app + real imported data + an API key for the chat leg).

## Definition of Done

- **21-A (automated):** `OverBudgetDetector` emits a candidate only for categories in `Over`, with a month-stable signature and templated Polish text; `AnomalyDetectionService` persists exactly one over-budget `Alert` per category per month over a real DB, idempotent across reruns and respecting dismissals; `GetBudgetStatusTool` returns the current-month per-category status + unbudgeted lines as JSON and is discoverable through `AddCofferChat`. Full suite green.
- **21-A (manual):** set a low limit for a category with current-month spend above it, run a detection (import/rescan), open **Alerty** → an over-budget alert appears with a clear Polish message; dismiss it, rescan → it does not come back. In the assistant, ask "jak stoję z budżetami w tym miesiącu?" → the model calls `GetBudgetStatus` and answers with the actual per-category numbers, with the call shown in the tool-trace.
- **Whole-sprint:** budget state is no longer trapped on the Budgets page — crossing a limit raises an alert, and the assistant can report budget status on demand, both grounded in the deterministic tracking engine.

## Files affected

- `src/Coffer.Core/Anomalies/AnomalyType.cs` (add `OverBudget`) + `AnomalyDetectionContext.cs` (optional `BudgetOverview?`)
- `src/Coffer.Infrastructure/Anomalies/Detectors/OverBudgetDetector.cs` (new) + `AnomalyDetectionService.cs` (inject query, feed context)
- `src/Coffer.Infrastructure/Chat/GetBudgetStatusTool.cs` (new)
- `src/Coffer.Infrastructure/DependencyInjection/ServiceRegistration.cs` (two `AddTransient` lines: one detector, one tool)
- `tests/Coffer.Infrastructure.Tests/Anomalies/AnomalyDetectorTests.cs` + `AnomalyDetectionServiceTests.cs` + `tests/Coffer.Infrastructure.Tests/Chat/GetBudgetStatusToolTests.cs`

## Open questions

To confirm with the owner before/while building (recorded as decisions in `log.md` once settled):

- **Alert trigger** → proposed **`Over` only** (crossing the limit), not `Warning`/approaching. Confirm.
- **PR split** → proposed **one headless PR** for both hooks (small, shared theme). Confirm vs two separate PRs.

## Deferred to a follow-up (kept out of scope)

- **Runtime localization of alert text** (#128) — over-budget text is Polish-templated like every other alert; the cross-cutting fix is separate.
- **A `Warning`/approaching alert** — v1 alerts only on the crossing.
- **Retraction** of an over-budget alert when spend later drops below the limit (re-categorisation) — alerts stay point-in-time.
- **Per-account / per-month parameters on `GetBudgetStatus`** — v1 mirrors `GetGoals` (parameterless, current month, all accounts).
- **AI commentary specific to budgets** ("why are you over, where to cut") — the advisor already does grounded cutting suggestions; a budget-specific tie-in is later.
