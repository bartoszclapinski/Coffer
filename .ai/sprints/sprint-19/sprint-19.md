# Sprint 19 — Spending explorer: selectable-window category breakdown + merchant drill-down

**Phase:** — (beyond-roadmap; deepens Phase 6 "Dashboard and charts" from a fixed overview into an interactive analysis surface — see `docs/architecture/10-roadmap.md` "Beyond the roadmap" → "Budget zones per category")
**Status:** Planned
**Depends on:** sprint-9 (`Transaction` incl. `Merchant`, `Account`, `IAccountService`, the transactions list + `TransactionListItem`), sprint-11 (`IDashboardQuery` server-side `GROUP BY` aggregation pattern, `CategorySlice`, LiveCharts wiring), sprint-15 (i18n — every new string is a resource key)

## Goal

The owner picks a **time window** (presets + a custom range) and sees where the money went in that window: a category breakdown that sums to the window's spend, and can **drill down** — click a category to see its merchants, click a merchant to see the underlying transactions — all aggregated server-side, with no schema change. It answers "what did I actually spend on, in *this* period?" interactively, where the dashboard only answers it for fixed windows and the chat only answers it as prose.

## Why this sprint exists

Three surfaces already touch category spend, and none of them lets the owner *explore* it:

1. **The dashboard is fixed-window and non-interactive.** Sprint 11 gives a current-month top-categories doughnut, a 30-day trend, and a 12-month bar — all anchored windows the owner cannot re-scope, and none drills below the category line.
2. **The chat `GetSpendingByCategory` tool is textual and category-only.** It answers "wydatki po kategoriach od X do Y" in prose, but there is no merchant dimension and no clickable path from a category to the transactions inside it.
3. **`Transaction.Merchant` exists in the model but is a dead dimension in the UI.** It is populated at parse time and used by anomaly detectors, yet the owner can never ask "which shops make up my *Groceries* line?" — the single most natural follow-up to any category number.

This sprint turns the existing, already-aggregated category data into an interactive drill-down and finally surfaces `Merchant` as a spending dimension — with **zero migration** (the data is all present) and no new AI spend (pure read-side SQL).

## Design decisions (the shape we commit to)

- **One read-side query object, three server-side aggregations.** A new `ISpendingExplorerQuery` in `Coffer.Core` (impl in `Coffer.Infrastructure`, mirroring `IDashboardQuery`) exposes: (1) categories in a window, (2) merchants within one category in that window, (3) the transactions of one merchant in that window. Every level is a server-side `GROUP BY` / filtered `SELECT` with `AsNoTracking`, on debits (`Amount < 0`) in the display currency, returned as **positive magnitudes** in PLN, largest first — reusing the exact conventions of `GetSpendingByCategoryTool` (`null` category → "Bez kategorii" / localized "Uncategorized"; `null`/blank merchant → localized "Unknown merchant").
- **The window is owner-driven.** Presets (this month, last month, last 3 months, last 12 months, this year) plus a **custom range** via two `DatePicker`s. The default is last-month (a complete period, unlike an idle current month — same reasoning the dashboard used to anchor on the latest transaction's month). An optional **account filter** (all accounts / one account) rides alongside, reusing the account-option pattern from `AffordabilityViewModel`.
- **Drill-down is navigational state, not new pages.** One `SpendingExplorerViewModel` holds the window, the account filter, and a breadcrumb (`All → {Category} → {Merchant}`). Selecting a category loads its merchants; selecting a merchant loads its transactions; a back/breadcrumb step pops the level. No per-level page, no route explosion — the Avalonia view swaps the right-hand panel on the current level.
- **Reuse, don't reinvent, the leaf.** The transaction level renders `TransactionListItem` (the same projection the transactions list and dashboard "recent" use), so the leaf is consistent with the rest of the app and needs no new row shape.
- **Presentation stays out of `Coffer.Core`.** The query returns data records (name, total, share, count); "Uncategorized"/"Unknown merchant" fallbacks and money formatting resolve at the VM boundary via `ILocalizer` + `CashFlowDisplay.Money`, exactly as the other VMs do — keeping the hard rule that `Coffer.Core` is presentation-free.
- **No migration, no AI, no new cost.** The data is already imported and indexed; this is pure read-side. Money renders `pl-PL` "zł" regardless of UI language.

## Approach — two PRs (query + VM headless → UI)

- **19-A — spending-explorer query + view model (headless).** `ISpendingExplorerQuery` + its EF implementation (three aggregations, per-window, per-optional-account), plus `SpendingExplorerViewModel` driving window selection, the account filter, and the category→merchant→transaction drill-down over fakes. Unit + integration tests; no pixels.
- **19-B — UI.** A dedicated **"Wydatki / Spending"** page with its own nav entry: the window/preset bar + optional account selector, a category list (amount + share), a merchant panel on drill-down, and the transaction leaf, with a breadcrumb to step back. Fully localized (keys in both `.resx`, parity test green), reusing the `Border.card` styles and the `ShowXxx`/`IsXxxActive` nav wiring from Sprint 18-C.

## Steps

### 19-A — spending-explorer query + view model

- [ ] 19.1 `ISpendingExplorerQuery` in `Coffer.Core/Spending/` with three methods (or one façade returning a level-shaped result): `GetCategoriesAsync(window, accountId?, ct)`, `GetMerchantsAsync(window, categoryId?, accountId?, ct)`, `GetTransactionsAsync(window, categoryId?, merchant?, accountId?, ct)`. `window` is a small `(DateOnly From, DateOnly To)` record; result records carry raw data only (id/name-or-null, total `decimal`, share `decimal`, count `int`) — no display strings.
- [ ] 19.2 `SpendingExplorerQuery` in `Coffer.Infrastructure/Spending/`: each level a server-side aggregation on `Amount < 0` + `Currency == DisplayCurrency` + `Date` in window (+ `AccountId` when scoped), `AsNoTracking`, positive magnitudes, ordered by total desc. Category names resolved via a second `Categories` lookup (mirror `GetSpendingByCategoryTool`); `null` category groups under a stable sentinel the VM localizes. Merchant grouping keys on `Merchant` with `null`/whitespace collapsed to one "unknown" bucket. Transactions project to `TransactionListItem`.
- [ ] 19.3 `SpendingExplorerViewModel` in `Coffer.Application/ViewModels/Spending/`: `[ObservableProperty]` window preset + custom `From`/`To` (`DateTimeOffset?`), an `Accounts` option list (all-accounts + one per account, via `IAccountService`), the current drill level + breadcrumb, and observable collections for categories / merchants / transactions. `[RelayCommand]` `LoadAsync`, `ApplyWindowAsync`, `SelectCategoryAsync`, `SelectMerchantAsync`, `BackAsync`. Amounts formatted via `CashFlowDisplay.Money`; fallbacks via `ILocalizer`.
- [ ] 19.4 Register `ISpendingExplorerQuery` (Infrastructure `ServiceRegistration`) and `SpendingExplorerViewModel` (Desktop `DesktopServiceRegistration`, `AddTransient`).
- [ ] 19.5 Tests (`Coffer.Infrastructure.Tests/Spending/` over real SQLCipher, + `Coffer.Application.Tests/ViewModels/Spending/` over fakes): category totals sum to the window's debit total and exclude credits; a window boundary is inclusive on both ends; a second account's transactions never leak when scoped to one; merchant grouping collapses `null`/blank into the unknown bucket and sums correctly; drilling category→merchant→transactions narrows to the right rows; the VM breadcrumb pushes/pops levels and re-queries on a window change.

### 19-B — UI

- [ ] 19.6 A dedicated **"Wydatki / Spending"** page (`Coffer.Desktop/Views/SpendingExplorerView.axaml` + `.axaml.cs`) with its own nav entry: a preset bar + custom-range `DatePicker`s + optional account `ComboBox`; a category list showing name, `CashFlowDisplay.Money` total, and a share bar/percent; a merchant panel shown on drill-down; the `TransactionListItem` leaf; a breadcrumb (`All → Category → Merchant`) with back. Reuse `Border.card`/`.sectionTitle`/`.metric*` local styles.
- [ ] 19.7 Wire navigation: `SpendingExplorerViewModel` ctor param + null-check + get-only property + `IsSpendingActive` + `[NotifyPropertyChangedFor]` on `_currentPage` + `ShowSpending` `[RelayCommand]` in `MainViewModel`; DataTemplate + sidebar `<Button Classes="nav">` in `MainWindow.axaml` (label `Nav.Spending`).
- [ ] 19.8 Localization: every label via `{l:Localize}`, keys added to **both** `Strings.resx` and `Strings.pl.resx` (Nav.Spending, window presets, custom-range labels, column headers, Uncategorized/UnknownMerchant fallbacks, empty-state, breadcrumb "All"). Parity test stays green.
- [ ] 19.9 Tests (`Coffer.Application.Tests/ViewModels/Main/`): `ShowSpending_SwitchesActivePage`; resource-key parity green.

### Sweep

- [ ] 19.10 No residual hardcoded user-facing literals in the new surfaces (all via `{l:Localize}` / `ILocalizer`). Money renders `pl-PL` "zł" via `CashFlowDisplay.Money` regardless of UI language.
- [ ] 19.11 Manual DoD click-through (below) — expected to defer to manual (needs a running desktop app + real imported data).

## Definition of Done

- **19-A (automated):** for a fixed window and seeded transactions, the category level sums to the window's debit total (credits excluded, boundaries inclusive); scoping to one account never blends another's rows; merchant grouping collapses blank/`null` into one localized-at-VM "unknown" bucket; drilling category→merchant→transactions returns exactly the matching rows; the VM re-queries on a window/account change and the breadcrumb pushes/pops correctly.
- **19-B (automated):** `ShowSpending` activates the page; resource-key parity holds; the VM surfaces categories/merchants/transactions and the breadcrumb through the view.
- **19-C (manual):** open **Wydatki / Spending**, pick "last month" → the category list sums to that month's spend; click *Groceries* → its merchants appear with sums; click a merchant → its transactions appear; switch to "last 12 months" → everything re-aggregates from one query set; every label switches PL↔EN live; money shows "zł".
- **Whole-sprint:** the owner can interactively explore spending by window → category → merchant → transaction, deterministically and server-side, with `Transaction.Merchant` finally surfaced as a first-class analysis dimension.

## Files affected

- `src/Coffer.Core/Spending/ISpendingExplorerQuery.cs` + result records (new)
- `src/Coffer.Infrastructure/Spending/SpendingExplorerQuery.cs` (new) + Infrastructure `ServiceRegistration`
- `src/Coffer.Application/ViewModels/Spending/SpendingExplorerViewModel.cs` (new)
- `src/Coffer.Application/ViewModels/Main/MainViewModel.cs` (nav wiring)
- `src/Coffer.Desktop/Views/SpendingExplorerView.axaml` (+ `.axaml.cs`) (new) + `MainWindow.axaml` (DataTemplate + nav button) + `DesktopServiceRegistration` (VM registration)
- `src/Coffer.Application/Localization/Strings.resx` + `Strings.pl.resx` (new keys, parity)
- `tests/Coffer.Infrastructure.Tests/Spending/**`, `tests/Coffer.Application.Tests/ViewModels/{Spending,Main}/**`

## Open questions

To confirm with the owner before/while building (recorded as decisions in `log.md` once settled):

- **Default window** → proposed **last month** (a complete period). Confirm vs "this month" or "last 30 days".
- **Share basis** → proposed each category's share of the *window's total debit* (so shares sum to ~100%). Confirm vs share of income or a fixed budget.

## Deferred to a follow-up (kept out of scope so the PRs stay digestible)

- **Period-over-period delta** — ▲▼ vs the previous equal-length window on each category/merchant line.
- **`GetSpendingByMerchant` chat tool** — the merchant dimension exposed to the assistant (the category one already exists).
- **Charts** — a LiveCharts doughnut/treemap on the explorer page; v1 is a ranked list with share bars.
- **Income exploration** — v1 is debits only (spending); an inflow/source breakdown is a separate cut.
- **Multi-currency** — v1 is PLN, mirroring the rest of the app.
