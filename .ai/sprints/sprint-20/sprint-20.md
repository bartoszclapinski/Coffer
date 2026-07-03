# Sprint 20 — Category budgets with mid-month tracking (budget zones)

**Phase:** — (beyond-roadmap; the "Budget zones per category with mid-month tracking" item from `docs/architecture/10-roadmap.md` → "Beyond the roadmap". The schema change follows `docs/architecture/02-database-and-encryption.md`; the optional over-budget alert reuses the Sprint-13 engine per `docs/architecture/04-ai-strategy.md`)
**Status:** Planned
**Depends on:** sprint-9 (`Transaction`, `Category`, import + `ImportSession` periods), sprint-19 (`ISpendingExplorerQuery` and the server-side per-category debit-sum pattern), sprint-11 (`IDashboardQuery` aggregation conventions, the current-month anchor), sprint-13 (the `IAnomalyDetector` engine, only if the deferred over-budget detector is pulled in), sprint-15 (i18n — every new string is a resource key)

## Goal

The owner sets a **monthly spending limit per category** (e.g. Groceries 1 500 zł, Entertainment 400 zł) and, at any point in the month, sees how much of each limit is already spent, whether they are on pace, a linear end-of-month projection, and a clear **ok / approaching / over** status — deterministically, so the everyday question "am I overspending this month?" has a grounded answer. The engine calculates; nothing here needs AI.

## Why this sprint exists

Sprint 19 made spend explorable by category, but the app still cannot answer the forward-looking everyday question: **"is this month's spend on track against what I meant to spend?"** There is no notion of an *intended* budget anywhere — only what was spent. Category budgets close that gap and sit directly on infrastructure that already exists:

- the **per-category debit sum over a window** is exactly Sprint 19's `ISpendingExplorerQuery` shape — a budget is that sum set against a limit;
- the **current-month anchor** and server-side `GROUP BY` conventions come from Sprint 11;
- an over-budget signal is a natural new **`IAnomalyDetector`** (Sprint 13) if we want it surfaced on the Alerty page.

So the new capability is mostly a small persisted entity + a deterministic tracking calculation + a page — no new analytical machinery.

## Design decisions (the shape we commit to)

- **The entity is `CategoryBudget` — deliberately *not* "Budget".** The app already has `AiBudgetGate` / monthly AI **cost** cap; naming this `CategoryBudget` (and the feature "budgets / limity") keeps the spending budget distinct from the API-cost budget in code and UI.
- **One recurring monthly limit per category (per currency), v1.** `CategoryBudget = (CategoryId, LimitAmount decimal(18,2), Currency, IsActive)` — the same limit applies every calendar month; there is no per-month override table and no rollover in v1 (both deferred). At most one active budget per (category, currency).
- **Tracking is calendar-month, mid-month aware, and purely linear.** For the current month (anchored like the dashboard — latest transaction's month, so an idle current month is not empty), spend-to-date = the positive magnitude of that category's debits from the 1st to the as-of date. Pace/projection is a single linear extrapolation: `projected = spendToDate / daysElapsed * daysInMonth`. Status is `Ok` / `Warning` (≥ 80% spent **or** projected to exceed) / `Over` (≥ 100% spent). No ML, no seasonality — labelled a simple projection.
- **The engine is pure and lives in `Coffer.Core`.** A `BudgetTrackingEngine` takes (limit, spendToDate, daysElapsed, daysInMonth) → a `BudgetStatus` (spent, remaining, fraction, projected, zone). The Infrastructure query only assembles inputs (limits + the month's per-category spend) and hands them to the engine — mirroring the Sprint-14/16/18 "engine calculates, everything else assembles" rule.
- **Uncategorised spend is shown, never silently dropped.** The Budgets page surfaces the uncategorised bucket's month spend as an unbudgeted line (no limit), so a category-less charge cannot hide overspending. Only real categories can carry a limit in v1.
- **A schema change means a backed-up migration.** Adding the `CategoryBudgets` table runs `pre-migration-backup` first (hard rule #8) — the first migration since Sprint 18; verified by a migration integration test. Money is `decimal(18,2)` (rule #1); `Currency` is non-null (rule #9).
- **No new AI, no new cost.** Tracking is arithmetic. An optional LLM "why are you over?" comment is explicitly out of v1 scope.

## Approach — two PRs (entity + engine + query → UI), with an optional 20-C

- **20-A — `CategoryBudget` entity + migration + tracking engine + query (headless).** The entity + EF config + backed-up migration, the pure `BudgetTrackingEngine` in `Coffer.Core`, an `ICategoryBudgetRepository` (CRUD) and an `IBudgetTrackingQuery` that returns each active budget's current-month status plus the unbudgeted lines. Unit + integration tests; no pixels.
- **20-B — UI.** A dedicated **"Budżety / Budgets"** page (own nav entry + `BudgetsViewModel`): set/edit/remove a per-category limit, and a live progress view (spent / limit, a zone-coloured bar, remaining, projected end-of-month, unbudgeted lines). Fully localized (keys in both `.resx`, parity test green), reusing the `Border.card` styles and the `ShowXxx`/`IsXxxActive` nav wiring.
- **20-C (optional / may defer) — surfacing.** An `OverBudget` `IAnomalyDetector` that raises an Alerty item when a category crosses its limit, and a read-only `GetBudgetStatus` chat tool. Pulled in only if 20-A/B land comfortably; otherwise a clean follow-up.

## Steps

### 20-A — entity + migration + tracking engine + query

- [x] 20.1 `CategoryBudget` entity in `Coffer.Core/Domain/` (`Id`, `CategoryId`, `LimitAmount` `decimal`, `Currency`, `IsActive`, `CreatedAt` UTC) + EF configuration (`decimal(18,2)`, FK to `Category`, index on `CategoryId`).
- [x] 20.2 EF migration adding `CategoryBudgets`. The `pre-migration-backup` callback runs first (rule #8) — verified in a migration integration test.
- [x] 20.3 `BudgetTrackingEngine` in `Coffer.Core/Budgeting/`: pure. Input (limit, spendToDate, daysElapsed, daysInMonth) → `BudgetStatus` (Spent, Remaining, Fraction, Projected, Zone ∈ {Ok, Warning, Over}). Linear projection; zone thresholds 80% / 100% or projected-over. Guards thin data (daysElapsed ≥ 1, limit > 0).
- [x] 20.4 `ICategoryBudgetRepository` (Core) + impl (Infrastructure): list active budgets, upsert a limit for a category, deactivate/remove. At most one active budget per (category, currency).
- [x] 20.5 `IBudgetTrackingQuery` (Core) + impl (Infrastructure): resolve the as-of month (dashboard anchor), sum each budgeted category's month-to-date debits server-side (`GROUP BY`, `AsNoTracking`, positive magnitudes), run each through the engine, and return the statuses plus the unbudgeted lines (real categories + the uncategorised bucket with month spend and no limit). Reuse Sprint-19 conventions.
- [x] 20.6 DI registration (Infrastructure) for the repository, the query, and the engine (singleton).
- [x] 20.7 Tests (`Coffer.Core.Tests` + `Coffer.Infrastructure.Tests`): engine zones at fixed fractions/projections (under/near/over, mid-month vs month-end); the query sums only the current month's debits per budgeted category, excludes credits, never blends other accounts if scoped; unbudgeted lines include the uncategorised bucket; the repository enforces one active budget per category; the migration ran a pre-migration backup and created the table.

### 20-B — UI

- [x] 20.8 A dedicated **"Budżety / Budgets"** page (`BudgetsView.axaml` + `.axaml.cs`) + `BudgetsViewModel`: a category picker + limit input to add/edit a budget, a list of budgeted categories each with spent/limit text, a zone-coloured progress bar, remaining, and projected end-of-month; an "unbudgeted this month" section. Money via `CashFlowDisplay`; labels localized at the VM boundary.
- [x] 20.9 Wire navigation: `BudgetsViewModel` ctor param + null-check + property + `IsBudgetsActive` + `[NotifyPropertyChangedFor]` + `ShowBudgets` `[RelayCommand]` in `MainViewModel`; DataTemplate + sidebar `<Button Classes="nav">` in `MainWindow.axaml` (label `Nav.Budgets`); VM registered in the Desktop container.
- [x] 20.10 Localization: every label via `{l:Localize}`, keys in **both** `.resx` (Nav.Budgets, Budgets.* title/subtitle/add/edit/remove/limit/spent/remaining/projected/zone captions/unbudgeted/empty/errors). Parity test stays green.
- [x] 20.11 Tests (`Coffer.Application.Tests`): the budgets VM surfaces statuses + zones + unbudgeted lines from a real engine over fakes, round-trips add/edit/remove; `ShowBudgets_SwitchesActivePage`; resource-key parity green.

### Sweep

- [x] 20.12 No residual hardcoded user-facing literals in the new surfaces (all via `{l:Localize}` / `ILocalizer`). Money renders `pl-PL` "zł" via `CashFlowDisplay` regardless of UI language.
- [ ] 20.13 Manual DoD click-through (below) — expected to defer to manual (needs a running desktop app + real imported data).

## Definition of Done

- **20-A (automated):** the `BudgetTrackingEngine` returns the correct zone/projection for fixed fractions and mid-month vs month-end inputs; the tracking query sums only the current month's per-category debits (credits excluded, other accounts not blended when scoped), runs them through the engine, and lists the uncategorised bucket as unbudgeted; the repository keeps one active budget per category; the migration ran a pre-migration backup and created `CategoryBudgets`.
- **20-B (automated):** the budgets VM surfaces spent/limit/remaining/projected + zone and the unbudgeted lines, and round-trips add/edit/remove; `ShowBudgets` activates the page; resource-key parity holds.
- **20-C (manual):** set a limit for a category, import a statement whose charges land in the current month, open **Budżety**, and see the spent/limit bar in the right zone with a sensible end-of-month projection; a category with no limit still shows its month spend as unbudgeted; every label switches PL↔EN live; money shows "zł".
- **Whole-sprint:** the app answers "am I overspending this month?" per category — a limit, month-to-date spend, a linear projection, and an ok/approaching/over zone — deterministically, with unbudgeted spend visible rather than hidden.

## Files affected

- `src/Coffer.Core/Domain/CategoryBudget.cs` (new) + `Infrastructure/Persistence/Configurations/…` + new migration
- `src/Coffer.Core/Budgeting/BudgetTrackingEngine.cs` + `BudgetStatus.cs` (new)
- `src/Coffer.Core/Budgeting/ICategoryBudgetRepository.cs` + `IBudgetTrackingQuery.cs` (+ result records) (new)
- `src/Coffer.Infrastructure/Budgeting/…` repository + query (new) + Infrastructure `ServiceRegistration`
- `src/Coffer.Application/ViewModels/Budgets/BudgetsViewModel.cs` (new) + `MainViewModel.cs` (nav) + Desktop DI
- `src/Coffer.Desktop/Views/BudgetsView.axaml` (+ `.axaml.cs`) (new) + `MainWindow.axaml` (DataTemplate + nav button)
- `src/Coffer.Application/Localization/Strings.resx` + `Strings.pl.resx` (new keys, parity)
- `tests/Coffer.Core.Tests/Budgeting/**`, `tests/Coffer.Infrastructure.Tests/Budgeting/**`, `tests/Coffer.Application.Tests/ViewModels/{Budgets,Main}/**`

## Open questions

To confirm with the owner before/while building (recorded as decisions in `log.md` once settled):

- **Period basis** → proposed **calendar month, anchored on the latest transaction's month** (dashboard rule). Confirm vs a rolling 30-day window.
- **Warning threshold** → proposed **80% spent or projected to exceed**. Confirm the percentage.
- **Where budgets live** → proposed a **dedicated "Budżety / Budgets" page**. Confirm vs folding progress bars into the dashboard / spending explorer.

## Deferred to a follow-up (kept out of scope so the PRs stay digestible)

- **Per-month overrides and rollover** (unused budget carrying into next month).
- **The `OverBudget` alert detector + `GetBudgetStatus` chat tool** — 20-C, pulled in only if 20-A/B land comfortably.
- **Budgets on the dashboard / spending-explorer surfaces** — v1 is a dedicated page.
- **AI commentary** ("why are you over, where to cut") — the advisor already does grounded cutting suggestions; a budget-specific comment is a later tie-in.
- **Multi-currency budgets** — v1 mirrors the rest of the app (PLN).
