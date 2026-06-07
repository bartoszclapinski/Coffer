# Sprint 11 log

## 2026-06-07

- Plan written (`chore/plan-sprint-11`). Sprint 11 = Phase 6 (Dashboard and charts): after login the
  owner lands on a Dashboard with current-month KPIs (spend / income / net), a 30-day spend trend, a
  12-month bar, a top-categories doughnut, and a recent-transactions panel — all aggregated
  server-side (`GROUP BY`) over the now-categorised data.
- Decisions (planning): ship as a **single PR** (Phase 6 is ~one weekend, thin layers) rather than
  the phased split Sprints 9–10 used; charts via `LiveChartsCore.SkiaSharpView.Avalonia` (stack pick),
  with the platform-neutral `LiveChartsCore` series built **in the view-model** (testable + reusable
  for a future MAUI home), the SkiaSharp/Avalonia package confined to `Coffer.Desktop`; Dashboard
  becomes the **default landing page** after login (Import moves one click away); aggregations are SQL
  `GROUP BY` only (doc 02), never client-side summation.
- Scope: **skip Phase 3 (Drive sync) and Phase 5 (receipts)** for now — sync is a larger infra lift
  and receipts need the MAUI project stood up. Dashboard is desktop-only and delivers value on data
  that already exists. The mobile "simplified home" Phase-6 bullet is deferred with MAUI.
- Layering (carried from Sprints 9–10): read DTOs + `IDashboardQuery` in `Coffer.Core/Dashboard/`,
  `DashboardQuery` in `Coffer.Infrastructure` over `IDbContextFactory`, `DashboardViewModel` in
  `Coffer.Application`, `DashboardView` in `Coffer.Desktop`. `Coffer.Core` stays UI/chart-free.
- Open questions parked for implementation: multi-currency scope (lean PLN-only v1), account scope
  (lean combined v1), chart drill-down (lean out of scope), default landing page (lean Dashboard).
  See `sprint-11.md`.
