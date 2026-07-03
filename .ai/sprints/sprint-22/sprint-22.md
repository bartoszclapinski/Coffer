# Sprint 22 — Next-month expense forecast (per category, feeding suggested budgets)

**Phase:** — (beyond-roadmap; the "Forecasting next month's expenses based on recurring patterns" item from `docs/architecture/10-roadmap.md` → "Beyond the roadmap". Reuses the Sprint-16 recurring-flow model, the Sprint-18 variable-burn exclusion, and the Sprint-19/11 per-category / per-month aggregations; ties into the Sprint-20 `CategoryBudget`.)
**Status:** In progress
**Depends on:** sprint-16 (`RecurringFlow` + `MatchCategoryId` + cadence, `CashFlowProjectionEngine`), sprint-18 (`IVariableBurnQuery` — the recurring-flow exclusion pattern), sprint-19 (`ISpendingExplorerQuery` per-category debit sums), sprint-11 (`DashboardQuery` per-month group-by + the latest-transaction month anchor), sprint-20 (`ICategoryBudgetRepository.SetBudgetAsync`, `IBudgetTrackingQuery`), sprint-15 (i18n)

## Goal

The app answers the other forward-looking budgeting question — not "am I over *this* month?" (Sprint 20) but **"what will I likely spend *next* month, per category?"** A deterministic forecast predicts next calendar month's spend for each category, split into a **fixed** part (from the recurring flows that land next month) and a **variable** part (from recent per-category history), and turns each per-category total into a **suggested monthly budget limit** the owner can accept with one click. The engine calculates; nothing here calls AI.

## Why this sprint exists

Sprints 20/21 let the owner set limits and get alerted when they cross them — but setting a *sensible* limit is guesswork, and nothing anticipates a quarterly or annual charge that lands next month. Coffer already knows both halves of the answer and never joins them:

- the **fixed/recurring** side is the Sprint-16 `RecurringFlow` model (each active outflow carries a `TypicalAmount`, a cadence, and — crucially — an optional `MatchCategoryId`);
- the **variable** side is exactly what Sprint 18's `IVariableBurnQuery` already isolates (trailing spend with recurring-matched charges excluded) and what Sprint 19's per-category aggregation already sums.

No new machinery is needed — just a small deterministic engine that folds active flows into per-category next-month totals, adds a recent-history variable estimate, and rounds the sum into a suggested limit. That suggestion flows straight into the Sprint-20 `SetBudgetAsync` upsert.

## Design decisions (the shape we commit to)

- **Target month = the calendar month *after* the anchor month.** The anchor is the dashboard/budget convention (the latest transaction's month, so an idle current month is not empty); the forecast is for `anchor + 1 month`. All money `decimal` (rule #1), PLN scope (rule #9), debits as positive magnitudes (Sprint-19 convention).
- **Per-category forecast = fixed + variable.**
  - **Fixed** = the sum of `TypicalAmount` over active `Outflow` `RecurringFlow`s attributed to that category (`MatchCategoryId`) that have an **occurrence in the target month** (monthly flows always; interval > 1 only when the cadence phase lands in that month). Flows with a null `MatchCategoryId` become an "unattributed fixed" line so they are shown, not dropped. The flow's **payment** occurrence is what counts (consistent with budgets counting spend by transaction date); the accrual offset is ignored in v1.
  - **Variable** = each category's trailing-3-month debit total (same window as `IVariableBurnQuery`) **excluding transactions attributable to an active recurring flow**, averaged to a monthly figure (`window total / 3`). Excluding the recurring portion here is what prevents double-counting it with the fixed part.
- **Exclusion is by merchant, not whole category.** `IVariableBurnQuery` excludes an entire matched *category* (it only needs an aggregate); a per-category forecast must be finer, or a category holding one recurring merchant (e.g. a streaming charge in "Rozrywka") would lose *all* its variable spend. v1 excludes only the transactions whose merchant matches an active flow's `MatchMerchant`, keeping the rest of the category's discretionary spend in the variable estimate. (Documented divergence from Sprint 18, intentional.)
- **Suggested limit = the per-category total rounded up to the nearest 10 zł.** A limit a little above the forecast gives headroom rather than guaranteeing an immediate `Over`. The suggestion is surfaced against the category's current budget (if any) so the owner sees "forecast 1 240 zł · suggested 1 250 zł · current limit 1 000 zł" and can accept with one click via `SetBudgetAsync`.
- **It is explicitly a *simple* projection.** Flat trailing averages plus known recurring charges — no seasonality, no trend/regression, no per-category burn curves. Labelled as such in the UI, exactly like `BudgetTrackingEngine`'s linear projection. This is distinct from and complementary to Sprint-20's *intra-month* projection (that extrapolates the current month; this forecasts the next one).
- **The engine is pure and lives in a new `Coffer.Core/Forecasting`.** `ExpenseForecastEngine` takes assembled per-category (fixed, variable) inputs + existing budgets → a `ExpenseForecast` (per-category fixed/variable/total/suggested-limit + current-limit, ordered by total). The Infrastructure query only assembles inputs (active flows → per-category fixed via cadence; trailing per-category variable; active budgets). Mirrors the Sprint-20 "engine calculates, query assembles" split. No migration (all data already exists).

## Approach — headless engine/query first, then UI

- **22-A — forecast engine + query (headless).** A new `Coffer.Core/Forecasting` namespace: the pure `ExpenseForecastEngine`, the result records (`ExpenseForecast`, `CategoryForecast`), and `IExpenseForecastQuery`; the Infrastructure impl in `Coffer.Infrastructure/Forecasting` assembling flows + trailing history + budgets; a small `OccursInMonth(flow, month)` cadence helper. Registered via a new `AddCofferForecasting`. Unit + integration tests over `PlanningDbTestBase`; no pixels.
- **22-B — UI + one-click "set as budget".** A surface (see open question) listing each category's next-month forecast (fixed / variable / total), its suggested limit vs current limit, and an "accept suggestion" action that calls `SetBudgetAsync` and refreshes. Fully localized (keys in both `.resx`, parity green), reusing the `Border.card` styles and nav wiring. An optional read-only `GetExpenseForecast` chat tool folds in here if it lands comfortably (otherwise a clean follow-up).

## Steps

### 22-A — forecast engine + query (headless)

- [x] 22.1 `Coffer.Core/Forecasting/` result records: `CategoryForecast(Guid? CategoryId, string? CategoryName, string? CategoryColor, decimal Fixed, decimal Variable, decimal Total, decimal SuggestedLimit, decimal? CurrentLimit)` and `ExpenseForecast(DateOnly Month, IReadOnlyList<CategoryForecast> Categories, decimal Total)`.
- [x] 22.2 `ExpenseForecastEngine` (pure, `Coffer.Core/Forecasting/`): given the target month and per-category assembled `(fixed, variable, currentLimit)` inputs, produce `CategoryForecast`s (`Total = Fixed + Variable`, `SuggestedLimit = round up Total to nearest 10 zł`), ordered by `Total` desc, plus the grand `Total`. Deterministic; guards zero/empty.
- [x] 22.3 `OccursInMonth(RecurringFlow flow, DateOnly month)` helper (`Coffer.Core/Forecasting/` or reuse/lift the projection engine's cadence phase): `true` for monthly flows; for interval > 1, `true` only when the `AnchorMonth` phase aligns with `month`. Unit-tested for monthly / quarterly / yearly.
- [x] 22.4 `IExpenseForecastQuery` (Core) + impl (`Coffer.Infrastructure/Forecasting/`): resolve the anchor month (latest transaction) → target = anchor + 1; per-category **fixed** = active outflow flows grouped by `MatchCategoryId` whose `OccursInMonth(target)`, summed by `TypicalAmount` (null category → unattributed line); per-category **variable** = trailing-3-month debit magnitude per category excluding active-flow merchants, `/ 3`; pull active budgets for `CurrentLimit`; feed the engine. Reuse Sprint-19/dashboard aggregation shapes and the Sprint-18 exclusion idea (by merchant).
- [x] 22.5 DI: a new `AddCofferForecasting` (engine singleton, query transient) chained into `AddCofferInfrastructure`.
- [x] 22.6 Tests (`Coffer.Core.Tests` + `Coffer.Infrastructure.Tests`): engine combines fixed+variable and rounds the suggested limit correctly; `OccursInMonth` for each cadence; the query attributes a monthly flow's `TypicalAmount` to its category's fixed part, adds a quarterly/annual flow only in a month it lands, computes variable from trailing history with flow-merchant transactions excluded (no double count), surfaces the uncategorised/unattributed lines, and carries the current budget limit. Real SQLCipher via `PlanningDbTestBase`.

### 22-B — UI + set-as-budget

- [ ] 22.7 A forecast surface + view model: per category the next-month fixed / variable / total, the suggested limit vs current limit, and an "accept" action calling `SetBudgetAsync` then refreshing. Money via `CashFlowDisplay`; month via `CashFlowDisplay.AccrualPeriod`; labels localized at the VM boundary.
- [ ] 22.8 Wire navigation / placement per the resolved open question (own page vs a Budgets-page section), reusing the `ShowXxx`/`IsXxxActive` pattern if a new page; VM registered in the Desktop container.
- [ ] 22.9 Localization: every label via `{l:Localize}`, keys in **both** `.resx` (parity test green).
- [ ] 22.10 Optional `GetExpenseForecast` chat tool (read-only over `IExpenseForecastQuery`), registered in `AddCofferChat` — pulled in only if 22-A/B land comfortably.
- [ ] 22.11 Tests (`Coffer.Application.Tests`): the VM surfaces per-category forecast + suggested vs current limit and round-trips "accept suggestion" → `SetBudgetAsync`; nav/registration; resource-key parity.

### Sweep

- [ ] 22.12 No residual hardcoded user-facing literals; money renders `pl-PL` "zł" via `CashFlowDisplay`. `dotnet format --verify-no-changes` clean.
- [ ] 22.13 Manual DoD click-through (below) — expected to defer to manual (needs a running desktop app + real imported data with recurring history).

## Definition of Done

- **22-A (automated):** `ExpenseForecastEngine` returns `Total = Fixed + Variable` and the rounded suggested limit; `OccursInMonth` is correct per cadence; the query attributes recurring outflows to their category's fixed part only in months they land, derives the variable part from trailing per-category history with recurring-merchant charges excluded (no double-count), lists uncategorised/unattributed spend, and carries current budget limits. Full suite green.
- **22-B (automated):** the VM surfaces the per-category forecast + suggested vs current limit and round-trips accepting a suggestion into `SetBudgetAsync`; the surface is reachable; resource-key parity holds.
- **Manual:** open the forecast, see next month's per-category prediction (a monthly subscription in its category's fixed part; everyday spend in variable; a quarterly/annual charge appearing only in the month it lands); accept a suggested limit and see it become the category's budget on the Budgets page; every label switches PL↔EN live; money shows "zł".
- **Whole-sprint:** the app both tracks the current month against limits (Sprint 20/21) and now *anticipates* next month per category, turning that anticipation into a one-click budget suggestion — deterministically.

## Files affected

- `src/Coffer.Core/Forecasting/` — `ExpenseForecast.cs` (+ `CategoryForecast`), `ExpenseForecastEngine.cs`, `IExpenseForecastQuery.cs`, cadence helper (new)
- `src/Coffer.Infrastructure/Forecasting/ExpenseForecastQuery.cs` (new) + `DependencyInjection/ServiceRegistration.cs` (`AddCofferForecasting`, chained)
- `src/Coffer.Application/ViewModels/Forecast/…` (new) + `MainViewModel.cs` (if a new page) + Desktop DI
- `src/Coffer.Desktop/Views/…` forecast surface + `MainWindow.axaml` (if a new page)
- `src/Coffer.Application/Localization/Strings.resx` + `Strings.pl.resx` (new keys, parity)
- (optional) `src/Coffer.Infrastructure/Chat/GetExpenseForecastTool.cs` + `AddCofferChat`
- `tests/Coffer.Core.Tests/Forecasting/**`, `tests/Coffer.Infrastructure.Tests/Forecasting/**`, `tests/Coffer.Application.Tests/ViewModels/Forecast/**`

## Open questions

To confirm with the owner before/while building (recorded as decisions in `log.md` once settled):

- **Where the forecast lives** → **DECIDED: a dedicated "Prognoza / Forecast" page** (own nav entry after Budżety), keeping the churn small and mirroring the sprint pattern; the "accept suggestion" action still calls `SetBudgetAsync` so a limit set there shows on the Budgets page.
- **Variable window / basis** → proposed **trailing 3 months, flat average** (matching `IVariableBurnQuery`). Confirm vs 6 months or a weighted recent bias.
- **Suggested-limit rounding** → proposed **round up to the nearest 10 zł**. Confirm the step / direction.
- **Chat tool** → include `GetExpenseForecast` in 22-B or defer to a follow-up?

## Deferred to a follow-up (kept out of scope)

- **Seasonality / trend modelling** (regression, month-of-year effects, per-category burn curves) — v1 is flat averages + known recurring charges.
- **Accrual-aware forecasting** (using `AccrualOffsetMonths` to shift a cost into the month it belongs vs is paid) — v1 forecasts by payment month, matching how budgets count spend.
- **Multi-month / horizon forecasting** — v1 is strictly the next single calendar month.
- **Auto-applying suggestions** — v1 always requires the owner to accept each suggested limit.
- **Multi-currency** — v1 mirrors the rest of the app (PLN).
