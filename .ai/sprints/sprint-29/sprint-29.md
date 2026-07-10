# Sprint 29 — Redesign rollout: migrate every remaining screen to the design system

**Phase:** — (cross-cutting UI redesign, continuation of Sprint 28. Driven by `.ai/design/Coffer desktop project design/`.)
**Status:** Planned
**Depends on:** sprint-28 (the token substrate, `Styles/Components.axaml`, fonts, `PhosphorIcon`, the shell + `NavItem` model, the preview-harness pattern). No database schema, no AI, no network.

## Goal

Every remaining desktop screen and pre-login window renders in the "pro terminal" design system, in **both light and dark**, so the whole app is visually cohesive and the owner can record a demo video of the app and its features. After this sprint no screen carries hardcoded light-only hex; the default theme can flip to Dark. Pure UI reskinning onto the existing view-models — no data, query, schema, AI, or network change.

## Why this sprint exists

Sprint 28 shipped the substrate + shell + one reference screen (Overview). The other ~11 section screens and all the pre-login windows still hardcode light-only colours, so they look inconsistent inside the new shell and break in dark. The owner needs the UI finished across the app to record a feature walkthrough. This sprint is the mechanical-but-broad rollout: apply the proven tokens + component library to every screen, matching the design language, validating each in both themes via env-var preview harnesses (no login/DB needed).

## Design decisions (the shape we commit to)

- **Reskin, don't rewrite.** Each screen keeps its existing view-model and behaviour; only the `.axaml` (and any code-behind converters) change — hardcoded hex → `{DynamicResource Coffer.*}` tokens, ad-hoc cards → `Border.panel`, bespoke labels/figures → the component styles (`label` / `title` / `muted` / `money` / KPI / chips / progress bars / icon tiles). Money figures gain the `money` class + the `$parent[Window]` privacy-blur binding.
- **Both themes, every screen.** Nothing hardcodes a colour; the theme toggle is correct everywhere. The default stays **Light** until the last screen lands, then flips to **Dark** (the design's hero) in the final step.
- **Charts stay readable in both themes.** LiveCharts paints built in the framework-free VMs use the fixed pine-accent family (as Overview) unless a screen already themes acceptably; per-theme chart tuning remains a Sprint-32 polish item.
- **Validation without a login.** Each migrated screen gets a dev-only preview entry (env-var guarded, like `COFFER_OVERVIEW_PREVIEW`) backed by a small canned-data stub query, so it can be screenshotted in both themes. Harnesses are dev-only and never on the normal flow.
- **Pre-login windows too.** Login, the setup wizard + its step views, and the Settings/recovery dialogs are migrated so the app is coherent from first launch (they currently hardcode light hex).
- **`Accounts` (the mock's new screen) is out of scope here** — it needs real accounts/balance modelling, not a reskin; tracked as a follow-up.

## Approach — grouped PRs, screen families, preview-validated

- **29-A — the ledgers: Transactions + Spending.** The dense table (filter chips, sortable Date/Merchant/Amount headers, category dots, In/Out totals) + the spending drill-down. The most design-defining screens.
- **29-B — budgets & forecast: Budgets + Forecast.** Zone-coloured progress bars, summary panels, set-as-budget.
- **29-C — advisor & planning: Goals (Advisor) + Cash-flow Planning + Affordability.** Goal cards, the cash-flow timeline + running-balance chart, the afford/not verdict.
- **29-D — the rest: Import + Alerts + Assistant (chat).** Drag-and-drop import progress, alert cards with accept/dismiss, the chat transcript + tool-trace.
- **29-E — Settings + pre-login.** The long Settings page (AI, budget, backup/recovery, language, theme), then Login, the setup wizard + steps, and the recovery/settings dialogs. Ends with flipping the default theme to Dark.

## Steps

### 29-A — ledgers
- [x] 29.1 `TransactionsView` on tokens + components: filter chips, sortable headers (active column brightens + arrow), category dots, In/Out totals, `money` + privacy blur; DataGrid (or item rows) restyled.
- [x] 29.2 `SpendingExplorerView` on tokens: breadcrumb, drill-down rows, category/merchant/amount, `money` + blur.
- [x] 29.3 Preview harnesses (`COFFER_TX_PREVIEW`, `COFFER_SPENDING_PREVIEW`) + canned stub queries; captured in both themes.

### 29-B — budgets & forecast
- [x] 29.4 `BudgetsView` on tokens: summary panel, per-category rows with zone-coloured `ProgressBar.bar` (+ `over`), remaining/over labels, unbudgeted lines.
- [x] 29.5 `ForecastView` on tokens: per-category fixed/variable/total, suggested vs current, set-as-budget button.
- [x] 29.6 Preview harnesses + captures (both themes).

### 29-C — advisor & planning
- [x] 29.7 `GoalsView` (Advisor) on tokens: goal cards (% funded, saved/target, target date, progress bar, add-money), simulator, projection chart.
- [x] 29.8 `CashFlowPlanningView` on tokens: dated timeline, running-balance chart, tight-window/accrual badges.
- [x] 29.9 `AffordabilityView` on tokens: afford/not verdict, headroom, safety floor, what-pushes-under.
- [x] 29.10 Preview harnesses + captures (both themes).

### 29-D — the rest
- [ ] 29.11 `ImportView` on tokens: drag-and-drop zone, 5-step progress, account picker, review banners.
- [ ] 29.12 `AlertsView` on tokens: alert cards (severity, title/description, accept/dismiss).
- [ ] 29.13 `ChatView` on tokens: message bubbles, tool-trace, input, budget/usage line.
- [ ] 29.14 Preview harnesses + captures (both themes).

### 29-E — Settings + pre-login
- [ ] 29.15 `SettingsView` on tokens: sectioned panels (AI provider/keys/cap, budget, Backup & Recovery, language, **theme selector**), all actions.
- [ ] 29.16 `LoginWindow` + setup wizard (`SetupWizardWindow` + `WelcomeStepView` / `MasterPasswordStepView` / `BipSeedDisplayStepView` / `BipSeedVerificationStepView` / `ConfirmStepView`) on tokens.
- [ ] 29.17 Dialog windows on tokens: `RestoreWindow`, `RestoreFromSeedWindow`, `EnableSeedRecoveryWindow`, `ChangeMasterPasswordWindow`.
- [ ] 29.18 Flip the default theme to **Dark** (`FileThemeStore` default) now that every surface is migrated; a Settings theme selector persists the owner's choice.

### Sweep
- [ ] 29.19 No hardcoded user-facing hex left in any migrated view (grep for `#` colours); resx parity green; `dotnet format` clean; every screen captured in both themes.
- [ ] 29.20 Manual full click-through by the owner (login → each screen) — deferred to manual.

## Definition of Done

- Every section screen + pre-login window renders correctly in **both** themes with no hardcoded hex; money figures honour the privacy blur.
- Each screen is captured (dev preview) in light and dark as evidence.
- The default theme is Dark; a Settings selector switches + persists.
- **Whole-sprint:** the app is visually cohesive end-to-end and demo-ready; behaviour, data, and tests are unchanged (green).

## Files affected

- `src/Coffer.Desktop/Views/*.axaml` (every section view + the login/setup/dialog windows) + a few code-behind converters
- `src/Coffer.Desktop/Preview/*` (per-screen stub queries) + `src/Coffer.Desktop/Views/*PreviewWindow.axaml(.cs)` (dev-only) + the `App.axaml.cs` env-var branches
- `src/Coffer.Infrastructure/Theming/FileThemeStore.cs` (default → Dark, final step)
- `src/Coffer.Application/Localization/Strings*.resx` (any new labels, e.g. the theme selector; parity)
- Possibly small chart-colour tweaks in the section view-models (paints only)

## Open questions

- **Record in dark or light?** → owner undecided; migrating for **both** so it doesn't matter. Default flips to Dark at the end (revertable).
- **Include the new `Accounts` screen?** → **deferred** — it's a new feature (accounts/balance modelling), not a reskin. Confirm whether it's wanted before the video.
- **Chat in the video?** → live chat needs a real API key; the screen migrates regardless, the owner decides whether to demo the live call.

## Deferred to a follow-up (kept out of scope)

- The `Accounts` screen (new feature), interaction depth (transaction drill-in side panel, real create forms behind New/⌘K), empty/skeleton states, the accent-colour picker, per-theme chart tuning — Sprint 32.
- Resuming **Sprint 27** (disaster-recovery tail) — after the redesign.
