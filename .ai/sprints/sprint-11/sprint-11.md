# Sprint 11 — Dashboard and charts

**Phase:** 6 (Dashboard and charts)
**Status:** Planned
**Depends on:** sprint-9 (`Transaction`, `Account`, `Category`, the read-side query pattern, the
post-login shell + sidebar), sprint-10 (categories are now populated, so a category breakdown is
meaningful)

## Goal

After login the owner lands on a Dashboard that shows the current month's KPIs (spend, income, net),
a 30-day spending trend, a 12-month bar, a top-categories doughnut, and the most recent transactions
— all computed from their real, now-categorised data, aggregated server-side in SQL.

## Background

Phases 0–4 are closed: the owner can set up an encrypted vault, import PKO "Historia rachunku" CSVs,
see them in a filterable grid, and have them auto-categorised (rules + learned cache + hybrid AI).
What is missing is the *overview* — the screen that turns rows into insight. Per `10-roadmap.md`
Phase 6 ("Dashboard and charts"), this sprint adds the visual layer: KPI cards, a spend-over-time
chart, a monthly bar, a category doughnut, and a recent-transactions panel. Charts use
`LiveChartsCore.SkiaSharpView.Avalonia` (the stack pick); **all aggregations run server-side
(`GROUP BY`)** per `docs/architecture/02-database-and-encryption.md` — no client-side summing over
materialised entities.

Phase 5 (receipts) is intentionally skipped for now because it requires standing up the MAUI mobile
project; Phase 3 (Google Drive sync) is likewise deferred. Dashboard is desktop-only and delivers
visible value on data that already exists, so it is the highest-leverage next step.

**Out of scope (later phases / explicitly deferred):**
- The mobile "simplified home" (`10-roadmap.md` Phase 6 bullet) — MAUI is not stood up yet
  (postponed since Phase 0). The view-models are shaped to be reusable when MAUI begins.
- Chat with data (Phase 7), anomalies/alerts (Phase 8), goals/advisor (Phase 9).
- Drill-down navigation from a chart slice into a filtered transaction list — nice-to-have, note
  in Open questions; not required for DoD.

## Layering note

Same pattern as Sprints 9–10: **read-side DTOs + the query interface in `Coffer.Core`**
(`Dashboard/`), **the EF implementation in `Coffer.Infrastructure`** (`Dashboard/DashboardQuery.cs`
over `IDbContextFactory<CofferDbContext>`), the **view-model in `Coffer.Application`**
(`ViewModels/Dashboard/`), the **view in `Coffer.Desktop`**. `Coffer.Core` stays free of any UI /
charting dependency (hard rule #3). The SkiaSharp/Avalonia charting package lives only in
`Coffer.Desktop`; the chart *series* are built in the view-model using the platform-neutral
`LiveChartsCore` types so the VM stays testable and reusable for MAUI (see Decisions).

## Steps

- [ ] 11.1 Add `LiveChartsCore.SkiaSharpView.Avalonia` to `Coffer.Desktop` and `LiveChartsCore` to
  `Coffer.Application` (pinned versions; keep in step with the existing 9.x pin discipline). Register
  the SkiaSharp Avalonia chart controls in `App.axaml` if the package requires it.
- [ ] 11.2 Core read model in `Coffer.Core/Dashboard/`: `IDashboardQuery` plus DTOs —
  `MonthlySummary` (Spend, Income, Net, Currency, TransactionCount), `CategorySlice`
  (CategoryId, Name, Color, Total, Share), `TrendPoint` (Date/Period bucket, Total), and a
  `DashboardFilter` (display currency, optional account, "as-of" month). Money is `decimal`,
  transaction dates `DateOnly`, every monetary DTO carries `Currency` (hard rules #1/#2/#9).
- [ ] 11.3 `DashboardQuery` (Infra) over `IDbContextFactory`, `AsNoTracking`, **everything aggregated
  server-side**:
  - 11.3.a current-month KPI: `SUM` of negative amounts (spend), positive amounts (income), net, count.
  - 11.3.b top categories for the current month: `GROUP BY CategoryId`, ordered by absolute spend
    descending, top N + an "Inne/Pozostałe" remainder bucket; nulls → an "Bez kategorii" slice.
  - 11.3.c 30-day daily spend trend: `GROUP BY Date` over the rolling 30 days (gaps filled with 0 in
    the VM, not the DB).
  - 11.3.d 12-month bar: `GROUP BY` year-month over the last 12 months (spend; optionally income).
  - 11.3.e recent transactions: reuse the existing projection shape (latest 5–10) — either call the
    existing `IGetTransactionsQuery` or a small dedicated projection; do not materialise entities.
  - Spend sign convention: expenses are `Amount < 0`, income `Amount > 0`; surface spend as a
    positive magnitude in DTOs and label it.
- [ ] 11.4 `DashboardViewModel` (Application): a `LoadCommand` that fills KPI properties, the
  `ISeries[]` for the cartesian (line + bar) and pie charts, axis definitions, and a recent-
  transactions collection (reuse `TransactionRowViewModel`). Busy + empty-state flags. Series are
  built from the 11.2 DTOs using `LiveChartsCore` types in the VM.
- [ ] 11.5 `DashboardView.axaml` (+ minimal code-behind) in `Coffer.Desktop`: KPI cards row, a
  `CartesianChart` (30-day line + a 12-month bar — one or two charts), a `PieChart` doughnut for
  categories, and a recent-transactions list. Match the light design language of the existing
  Import/Transactions/Settings views and the Dashboard mockup in `docs/mockups/index.html`.
- [ ] 11.6 Wire Dashboard into the shell (`MainViewModel` + `MainWindow`): new "Pulpit" sidebar entry
  with `ShowDashboardCommand` / `IsDashboardActive`, and make Dashboard the **default landing page**
  after login (replaces Import as the initial `CurrentPage`). Register `DashboardViewModel` in
  Application + Desktop DI; inject `IDashboardQuery` (register `DashboardQuery` in Infrastructure).
- [ ] 11.7 Empty / first-run state: a fresh vault (no transactions) shows a friendly "Brak danych —
  zaimportuj wyciąg" panel instead of empty/crashing charts; single-month data still renders.
- [ ] 11.8 Tests:
  - 11.8.a `DashboardQuery` over real SQLCipher with seeded multi-category, multi-month,
    mixed-sign (and a second-currency) data: KPI sums, category breakdown order + remainder bucket +
    null-category slice, daily buckets within the 30-day window, monthly buckets across 12 months,
    currency scoping, and the empty-DB case (all zeros / empty collections).
  - 11.8.b `DashboardViewModel` (Application): load populates KPIs + series counts; empty state flag
    set when there are no transactions; busy flag toggles.
  - **Charts render is not headlessly verifiable** → covered by the manual DoD, stated explicitly.
- [ ] 11.9 `dotnet build` + `dotnet test` + `dotnet format --verify-no-changes` green locally before
  the PR; full suite stays green; build with 0 warnings.
- [ ] 11.10 `gh issue create` (labels `feat` + `sprint-11`) → `feature/sprint-11-dashboard` branch →
  PR `Closes #<issue>` → CI green → squash-merge. Then `chore/close-sprint-11` (log + index update).

## Delivery

Single PR (Phase 6 is ~one weekend and the layers are thin). Standard flow: issue → branch →
commits → push → `gh pr create` → CI green → `gh pr merge --squash --delete-branch`. If `main` has
moved when merging, `gh pr update-branch` + re-run CI first.

## Definition of Done

1. After login the app opens on the **Dashboard** (not Import); a "Pulpit" sidebar entry navigates
   back to it.
2. KPI cards show the current month's **spend, income, and net** computed server-side from real data,
   with currency shown.
3. A **30-day spend trend**, a **12-month bar**, and a **top-categories doughnut** render from the
   owner's real categorised data; a **recent-transactions** panel lists the latest rows.
4. A fresh vault (no transactions) shows a clean empty state — no exceptions, no blank charts.
5. All aggregations are SQL `GROUP BY` (no client-side summation over materialised entities).
6. Tests green locally and on CI; no real API calls; `dotnet format` clean; 0 build warnings.

## Files affected

**New (Core):**
- `src/Coffer.Core/Dashboard/IDashboardQuery.cs`, `MonthlySummary.cs`, `CategorySlice.cs`,
  `TrendPoint.cs`, `DashboardFilter.cs`

**New (Infrastructure):**
- `src/Coffer.Infrastructure/Dashboard/DashboardQuery.cs`

**New (Application/Desktop):**
- `src/Coffer.Application/ViewModels/Dashboard/DashboardViewModel.cs`
- `src/Coffer.Desktop/Views/DashboardView.axaml` (+ `.axaml.cs`)

**Modified:**
- `src/Coffer.Application/ViewModels/Main/MainViewModel.cs` (Dashboard nav + default page)
- `src/Coffer.Desktop/Views/MainWindow.axaml` (sidebar entry, content host)
- `src/Coffer.Desktop/Coffer.Desktop.csproj` (charting package), `App.axaml(.cs)` if needed
- `src/Coffer.Application/Coffer.Application.csproj` (`LiveChartsCore`)
- `ServiceRegistration.cs` (Infrastructure: `IDashboardQuery`; Desktop: `DashboardViewModel`)
- `docs/architecture/02-database-and-encryption.md` / `06`/`10` if the realised aggregation shape
  diverges from the docs

## Open questions

- **Multi-currency:** the vault is PLN-dominant but `Currency` is mandatory (hard rule #9). For v1 do
  we scope the dashboard to a single display currency (PLN) and ignore other-currency rows, or split
  KPIs/charts per currency? **Lean:** single display currency (PLN) for v1, with a note; per-currency
  later. Resolve before 11.3.
- **Account scope:** combined across all accounts, or an account selector on the dashboard? **Lean:**
  combined for v1; selector later (the data model already supports it via `DashboardFilter`).
- **Chart-slice drill-down:** click a category slice / month bar → open the Transactions page
  pre-filtered? **Lean:** out of scope for DoD; revisit if cheap.
- **Default landing page:** Dashboard vs keeping Import as the landing page. **Lean:** Dashboard
  (the whole point of the sprint); Import stays one click away.
- New questions that surface during the sprint are logged in `log.md`.
