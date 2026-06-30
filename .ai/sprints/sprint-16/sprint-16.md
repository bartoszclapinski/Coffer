# Sprint 16 — Cash-flow planning (timing-aware) + AI assistant

**Phase:** — (beyond roadmap — "Forecasting next month's expenses based on recurring patterns" + "What-if scenarios", `docs/architecture/10-roadmap.md`)
**Status:** Closed (2026-06-30)
**Depends on:** sprint-9 (transactions), sprint-11 (dashboard + LiveCharts2), sprint-13 (recurrence-detection logic to recycle), sprint-14 (the "engine calculates, AI explains" pattern + Doradca page shape), sprint-15 (i18n — every new string is a resource key)

## Goal

The owner sees, on a dedicated planning page, **what is paid when and when money comes in** over a forward horizon — a dated timeline of recurring inflows and outflows with a running balance, "tight-window" warnings (where the balance dips before income arrives), and the **accrual period each obligation belongs to** (so fuel/leasing/tax paid the month after the period they relate to are attributed correctly). The numbers are computed deterministically in `Coffer.Core`; the AI assistant only **explains the timing** in prose and answers questions about it. The flat "income − fixed − variable" monthly figure is replaced by a within-month picture of where the money actually goes and how much is left.

## Why this sprint exists

Today the only forecast is the flat monthly aggregate in `FinancialContext` (`MonthlyIncome − MonthlyFixedExpenses − MonthlyVariableAvg`). It has **zero timing awareness**: it can't show that a payment leaves on the 7th while salary lands on the 10th (within-month squeeze), nor that July's fuel charge is really a June cost (accrual offset). The recurrence knowledge that exists is trapped inside `MissingRecurrenceDetector` with no reusable model. This sprint extracts a first-class recurring-flow model, builds a deterministic dated projection on top, surfaces it, and lets the assistant narrate it.

## Design decisions (the shape we committed to)

- **`RecurringFlow` is a persisted, user-editable entity**, not a read-model recomputed from history. Reason: the **accrual offset** ("which period this payment belongs to") is the owner's domain knowledge and is **not derivable from bank data** — `BookingDate` is the bank's posting date (a day or two after the operation), never the accrual period. Detection only *proposes* flows; the owner confirms/edits and sets the offset.
- **Flows cover both directions.** Income timing is half the problem ("kasa wpływa później niż muszę zapłacić"), so a flow has a direction (inflow/outflow), not just "obligations".
- **The projection engine lives in `Coffer.Core` and is fully deterministic.** AI never produces a number — it only explains the engine's output (same hard rule as Sprint 14's advisor).
- **Cadence is modelled as `IntervalMonths` (1 = monthly, 3 = quarterly, 12 = yearly) + an anchor day-of-month** (+ anchor month for interval > 1), avoiding an enum explosion while covering the real cases (salary monthly, tax quarterly, insurance yearly).
- **Accrual offset semantics:** `AccrualOffsetMonths` = number of months the cost *belongs before* its payment date. `0` = paid in the period it relates to; `1` = paid the month after the period (leasing/fuel/tax-on-prior-month). The payment date drives the cash timeline; the accrual offset drives "which month this really costs".
- **Starting balance is derived from the statements** — the running sum of the account's transactions, not a user-entered figure. The owner accepts the responsibility of keeping imports contiguous. To make that safe, a deterministic **statement-continuity check** flags gaps (using `ImportSession.PeriodFrom`/`PeriodTo` per account) and **warns** when the imported history is non-contiguous, since a gap silently corrupts the running-sum balance.

## Approach — three PRs ("foundation before breadth", the Sprint-10/12/13/14 cadence)

- **16-A — domain + detection + projection engine + persistence (all deterministic, headless, test-covered).** Build the whole calculation spine and prove it with unit tests before any pixels: the `RecurringFlow` entity, its EF mapping + migration (pre-migration-backup, rule #8), a detection service that proposes flows from transaction history (recycling the `MissingRecurrenceDetector` grouping), a repository/query to persist + read them, and the `CashFlowProjectionEngine` that turns active flows + a starting balance + a horizon into a dated event timeline with a running balance and tight-window flags.
- **16-B — planning UI.** An Avalonia "Plan przepływów" page: the forward timeline (upcoming inflows/outflows by date), a running-balance line chart (LiveCharts2), tight-window warnings, and flow management — confirm detected suggestions, edit amount / day / accrual offset, add a manual flow, disable one. Fully localized (keys in both `.resx`). VM + tests.
- **16-C — AI assistant.** A read-only `GetCashFlowProjection` / `GetUpcomingObligations` chat tool so the assistant can answer timing questions, plus a `CashFlowExplainer` that narrates the projection in prose (budget-gated, anonymised, ledgered as `cashflow-explain`, deterministic numbers only, engine-only fallback on any failure) — the Sprint-14 advisor pattern. Tests.

## Steps

### 16-A — domain + detection + projection engine + persistence

- [ ] 16.1 `RecurringFlow` domain entity (`Coffer.Core/Planning/`): `Id`, `Name`, `Direction` (Inflow/Outflow), `MatchMerchant`/`MatchCategoryId` (how a flow ties back to transactions), `IntervalMonths`, `AnchorDayOfMonth`, `AnchorMonth` (nullable, for interval > 1), `TypicalAmount` (decimal, magnitude), `AmountStdDev` (decimal), `AccrualOffsetMonths` (int), `Currency` (non-null, rule #9), `IsActive`, `Source` (Detected/Manual), `CreatedAt` (UTC). No presentation strings (rule #3).
- [ ] 16.2 `Cadence` / `FlowDirection` value types in `Coffer.Core/Planning/` (pure enums/records, no UI).
- [ ] 16.3 EF mapping + migration: configure `RecurringFlow` in `CofferDbContext`, add the migration, ensure it runs behind `pre-migration-backup` (rule #8). `decimal(18,2)` on money columns (rule #1), `Currency` non-null (rule #9).
- [ ] 16.4 `IRecurringFlowDetector` (Core) + implementation (Infrastructure): propose `RecurringFlow` candidates from transaction history — group by merchant/category, require presence in ≥ N distinct months (reuse `AnomalyThresholds.MinRecurrenceMonths` rationale), derive `TypicalAmount` + `AmountStdDev`, infer `AnchorDayOfMonth` from the day-of-month distribution (median), default `AccrualOffsetMonths = 0` and `Source = Detected`. **Refactor** the recurrence grouping out of `MissingRecurrenceDetector` into a shared helper both consume, so the logic lives in one place.
- [ ] 16.5 `IRecurringFlowRepository` (Core) + EF implementation (Infrastructure): CRUD + "get active flows". Read-side query for the UI.
- [ ] 16.6 `CashFlowProjectionEngine` (`Coffer.Core/Planning/`, deterministic): given active flows + a starting balance + an anchor date + a horizon (days), expand each flow into dated occurrences across the horizon, sort into a single timeline of `CashFlowEvent`s (date, direction, amount, flow name, accrual period), compute the running balance after each event, and flag `TightWindow`s (running balance below a configurable floor, e.g. ≤ 0 or below a buffer). Output a `CashFlowProjection` record. Pure decimal, `DateOnly` dates (rules #1, #2).
- [ ] 16.7 Starting balance: derive the projection's opening balance from the **running sum of the account's transactions** up to the anchor date (per primary account). The engine takes the opening balance as an input and stays pure; the Infrastructure read-side computes the sum.
- [ ] 16.7.a `IStatementContinuityChecker` (Core) + EF implementation (Infrastructure): order each account's `ImportSession` periods by `PeriodFrom`, detect gaps where `next.PeriodFrom > prev.PeriodTo.AddDays(1)`, and return the gap ranges. Used to warn when the running-sum balance is unreliable. Pure `DateOnly` (rule #2). Unit-tested with synthetic sessions (contiguous, single gap, overlap-is-fine).
- [ ] 16.8 Unit tests (`Coffer.Core.Tests` + `Coffer.Infrastructure.Tests`): flow expansion across a multi-month horizon (monthly/quarterly/yearly); accrual-offset attribution; running-balance maths; tight-window detection when an outflow precedes an inflow; detector proposes the right flows from a synthetic history (Bogus) and infers the median day; repository round-trips; migration applies on a fresh DB.

### 16-B — planning UI

- [ ] 16.9 `CashFlowPlanningViewModel` (`Coffer.Application/ViewModels/Planning/`): loads active flows, derives the opening balance from the running transaction sum, runs the continuity check, builds a projection via the engine, and exposes the timeline, the running-balance series, tight-window warnings, the **statement-continuity warning** (gap ranges, when present), and the horizon selector. Injects `ILocalizer`.
- [ ] 16.10 Flow-management VM surface: confirm a detected suggestion (Detected → active/Manual), edit amount / day / interval / accrual offset, add a manual flow, disable/delete. Validation at the VM boundary.
- [ ] 16.11 Avalonia `CashFlowPlanningView.axaml` + nav entry: forward timeline list (date, name, amount, direction, accrual-period badge), running-balance line chart (LiveCharts2, matching the Dashboard's chart language), tight-window warning callouts, a **non-contiguous-statements warning banner** (when the continuity check finds gaps), and the flow editor. All strings via `{l:Localize}`; keys added to **both** `.resx` (parity test must stay green).
- [ ] 16.12 Wire the page into `MainWindow` nav under the label **"Plan przepływów" / "Cash flow"** (keys `Nav.CashFlow` in both `.resx`).
- [ ] 16.13 Tests (`Coffer.Application.Tests`): VM builds a projection from fake flows via a fake repository; editing an accrual offset / day re-runs the projection; tight-window surfaces; uses `FakeLocalizer`. Resource-key parity test still passes.

### 16-C — AI assistant

- [ ] 16.14 `GetUpcomingObligations` / `GetCashFlowProjection` read-only chat tool(s) (`Coffer.Infrastructure/Chat/`): return active flows and/or the dated projection for a horizon, anonymised, registered in `AddCofferChat()`, auto-discovered via `IEnumerable<IChatTool>` like the existing six tools.
- [ ] 16.15 `CashFlowExplainer` (`Coffer.Infrastructure`, Sprint-14 advisor pattern): turns the deterministic projection into prose explaining the timing ("on the 7th leasing leaves; salary lands on the 10th; the tightest point is …"), budget-gated via `IAiBudgetGate`, anonymised via `IPromptAnonymizer`, metered once as `cashflow-explain` in the ledger, **engine-only fallback** on any failure. No numbers invented — the prose only narrates engine output.
- [ ] 16.16 Surface the explanation in the planning page (a narrative panel), localized, refreshable.
- [ ] 16.17 Tests: the chat tool returns the right shape; the explainer falls back to a deterministic summary when the budget gate denies or the provider throws; metering writes one `cashflow-explain` entry.

### Sweep

- [ ] 16.18 Resource-key parity holds; no residual hardcoded user-facing literals in the new views/VMs.
- [ ] 16.19 Manual DoD click-through (below).

## Definition of Done

- **16-A (automated):** engine + detector + repository unit tests pass — flow expansion across monthly/quarterly/yearly cadences, accrual-period attribution, running-balance from the transaction sum, tight-window detection, statement-continuity gap detection (contiguous / gap / overlap), detection proposes correct flows from a synthetic history, migration applies on a fresh DB behind a backup.
- **16-B (automated + manual):** VM tests assert a projection built from fakes and re-computed on edits, plus a continuity warning when sessions have a gap. Manual: open the planning page, see upcoming inflows/outflows on their dates with a running balance seeded from the statement history, a tight window flagged where an outflow precedes the next inflow, a warning banner when imports are non-contiguous, confirm a detected suggestion and set its accrual offset, watch the timeline update.
- **16-C (automated):** chat tool returns the projection; explainer falls back to deterministic text on gate-deny / provider failure; one `cashflow-explain` ledger entry per run.
- **Whole-sprint (manual):** with real imported data, the planning page answers "what leaves when, when does money arrive, where am I tight, and what does this month actually cost me once accruals are attributed" — and the assistant explains the same timing in prose. Money still shows "1 234,50 zł"; every label switches PL↔EN live.

## Files affected

- `src/Coffer.Core/Planning/` (new): `RecurringFlow.cs`, `FlowDirection.cs`, `CashFlowEvent.cs`, `CashFlowProjection.cs`, `CashFlowProjectionEngine.cs`, `IRecurringFlowDetector.cs`, `IRecurringFlowRepository.cs`, `IStatementContinuityChecker.cs`
- `src/Coffer.Infrastructure/Planning/` (new): `RecurringFlowDetector.cs`, `RecurringFlowRepository.cs`, `StatementContinuityChecker.cs`, opening-balance read-side; shared recurrence-grouping helper extracted from `Anomalies/Detectors/MissingRecurrenceDetector.cs`
- `src/Coffer.Infrastructure/Persistence/` : `CofferDbContext` config + new migration
- `src/Coffer.Infrastructure/Chat/` (new): `GetCashFlowProjectionTool.cs` (+ registration in `ServiceRegistration.cs` `AddCofferChat()`)
- `src/Coffer.Infrastructure/Planning/CashFlowExplainer.cs` (new)
- `src/Coffer.Application/ViewModels/Planning/` (new): `CashFlowPlanningViewModel.cs`, flow-editor VM(s)
- `src/Coffer.Desktop/Views/CashFlowPlanningView.axaml` (+ `.cs`); `MainWindow.axaml` nav entry
- `src/Coffer.Application/Localization/Strings.resx` + `Strings.pl.resx` (new keys, parity)
- `src/Coffer.*/DependencyInjection/*ServiceRegistration.cs` (register detector, repository, explainer, tool)
- `tests/Coffer.Core.Tests/Planning/**`, `tests/Coffer.Infrastructure.Tests/Planning/**`, `tests/Coffer.Application.Tests/ViewModels/Planning/**`

## Open questions

All resolved by the owner (2026-06-30), recorded as decisions in `log.md`:
- **Starting balance** → derived from the **running sum of the account's transactions** (not user-entered). The owner keeps imports contiguous; a `StatementContinuityChecker` warns on gaps so the balance stays trustworthy.
- **Projection horizon** → selectable, default **60 days**.
- **Income as flows** → **yes**, modelled as `Inflow` `RecurringFlow`s, detected like outflows.
- **Variable spending** → **discrete recurring flows only in v1**; smeared daily variable burn is a deferred follow-up.
- **Nav label** → **"Plan przepływów" / "Cash flow"** (`Nav.CashFlow`).
- **Accrual surfacing** → **per-event accrual-period badge in v1**; a monthly "true cost" rollup is a fast-follow if wanted.

## Deferred to a follow-up

- **Variable-spend overlay** — smearing `MonthlyVariableAvg` as a daily burn over the projection so the running balance reflects day-to-day spending, not just discrete recurring flows.
- **Monthly accrual rollup** — a "what this month really costs" view that re-buckets payments by their accrual period (beyond the per-event badge).
- **Per-account known-balance anchoring** — a stored, reconciled balance per account as an alternative to the running sum, removing the contiguity dependency.
