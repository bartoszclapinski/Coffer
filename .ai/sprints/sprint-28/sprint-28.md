# Sprint 28 — Redesign foundation: terminal shell + theming (Overview slice)

**Phase:** — (cross-cutting UI redesign, like Sprint 15's i18n — not a roadmap phase. Driven by `.ai/design/Coffer desktop project design/` — the "pro terminal" high-fidelity prototype: `README.md` + `Coffer - Build Spec.md` + `Coffer Terminal.dc.html` + `screenshots/`.)
**Status:** Planned
**Depends on:** sprint-15 (i18n `ILocalizer` + `{l:Localize}` + the `FileLanguageStore` plaintext-persistence pattern to mirror for the theme), sprint-11 (LiveCharts2 charts to restyle), sprint-6 (the `MainWindow` shell + `MainViewModel` nav this replaces). No database schema, no AI, no network.
**Note:** Sprint 27 (disaster-recovery tail) is **paused** mid-plan at the owner's request — this redesign is the priority; we return to 27 after.

## Goal

Prove the entire "pro terminal" design system end-to-end on one screen, so migrating the rest is mechanical. After this sprint the desktop app has: a **token-driven light/dark theme** (dark default, runtime toggle, persisted), the **Hanken Grotesk + Phosphor** type/icon system, a **reusable Avalonia style library** (panels, KPI cells, chips, progress bars, category icon tiles, sortable headers), the new **shell chrome** (60px icon rail + 52px top bar), an **app-wide privacy-blur** toggle, a **⌘K command palette**, and the **Overview** screen (Dashboard remapped onto Coffer's *real* PLN data) fully rebuilt in the system as the reference implementation. Deterministic UI work; no schema, no AI, no network.

## Why this sprint exists

The owner has commissioned a complete, high-fidelity redesign (a dense, data-forward "pro terminal", light + dark) and it is now the priority. The current desktop is the opposite of the target: **pinned to Light** (`App.axaml RequestedThemeVariant="Light"`) and hand-styled with **light-only hardcoded hex across ~20 `.axaml` views**, a 240px text-label sidebar, no theming, no command palette, no privacy blur. Adopting the design is therefore not a facelift — it needs a real token/theming substrate and a new shell before any screen can be faithfully rebuilt.

Owner decisions locked in (2026-07-08):
- **Full shell + remap onto real data**, not a literal rebuild. The mock assumes net worth, investments, debt, APY, credit utilization, and Plaid-style account linking — none of which Coffer has (it is PLN, local PKO-CSV import, no bank linking, and explicitly *"not a banking integration / not an investment advisor"*). We adopt the **visual language and UX** (rail, top bar, ⌘K, light/dark, panels, category colors) and map it onto Coffer's **actual** features and data. **Currency stays PLN "zł"** (`pl-PL`), never `$`. No fabricated investment/net-worth/linking data.
- **Longer rail + ⌘K for the extra features.** The mock shows 6 screens; Coffer has more (Import, Assistant/chat, Alerts, Cash-flow planning, Affordability, Forecast, Settings). The rail carries more than six icons and the command palette reaches everything — nothing is dropped.

Doing this as a **vertical slice** (foundation + shell + one full screen) de-risks the whole program: every hard problem — theming substrate, fonts/icons, the shell, the palette, privacy blur, and one real screen — is solved once and proven before the mechanical per-screen migration in Sprints 29+.

## Design decisions (the shape we commit to)

### Theming substrate

- **Tokens as Avalonia `ThemeDictionaries`, consumed only via `DynamicResource`.** A `Theme/Tokens.axaml` `ResourceDictionary` defines a Light and a Dark `ThemeDictionary` carrying every spec token as a keyed `Color` + `SolidColorBrush` (`Bg`, `Panel`, `Panel2`, `Bd`, `Tx`, `Mut`, `Mut2`, `Pos`, `Neg`, `Track`, `Blue`, `ActiveBg`, `Hover`, `Accent`) plus the **8 category color pairs** (light vs dark-bright). Components read `{DynamicResource Coffer.Panel}` etc. — **never a hardcoded hex** — so the theme toggle and future accent tweaks just work. Exact values are lifted from `Coffer - Build Spec.md`.
- **Dark is the default; the choice persists pre-login.** `App.axaml` stops being pinned to Light. An `IThemeStore` mirrors `FileLanguageStore` (a plaintext `theme.json` in the vault folder, readable before the DEK exists so login/setup honour it), defaulting to **Dark**. Startup applies the stored `ThemeVariant`; a top-bar sun/moon toggle (and a ⌘K command) flips it live and saves.
- **Category icon tiles: precomputed tints, not `color-mix`.** The web mock uses `color-mix(in srgb, <color> 16%, var(--panel))`; Avalonia has no `color-mix`, so each category's tile background is a precomputed tinted brush per theme (a converter or a keyed brush table), with the category color used for the dot/text. The light/dark category pairing table is kept verbatim.
- **Migration guard for un-restyled screens.** Screens not yet migrated (everything except the shell + Overview this sprint) still carry light-only hex. To avoid an unreadable dark-on-light state, the not-yet-migrated section views keep rendering acceptably in the default theme; the theme toggle is fully correct on migrated surfaces (shell + Overview), and the remaining screens are migrated in Sprints 29–31. (Interim handling is an open question below.)

### Type & icons

- **Hanken Grotesk + Phosphor embedded as `avares://` assets** (no CDN — the app is offline-first). A shared `FontFamily` resource; money figures use OpenType tabular figures (`FontFeatures`/`tnum`) at weight 700, mirroring the spec. Phosphor glyphs are referenced through a small helper (a keyed glyph table / markup extension) so views name an icon rather than paste a codepoint; `ph` (regular) vs `ph-fill` (active/emphasis).

### Reusable style library

- **A `Styles/` resource-dictionary set is the component vocabulary the whole redesign reuses:** `Border.panel` (14px radius, `1px var(--bd)`), the KPI cell, filter/segment chips (active = accent fill + white), progress bars (track + colored fill, red when over), the category icon tile, the rail button (active = filled icon + `ActiveBg`), the top-bar buttons, and the sortable table header (active column brightens + ↑/↓). Built and proven here, applied everywhere after.

### Shell & navigation

- **`MainWindow` is rewritten to the terminal chrome:** a 60px icon rail (logo mark; feature icons with 40px hit targets, active = filled + `ActiveBg`; gear pinned bottom) + a 52px top bar (active-screen title, 250px search, theme toggle, privacy eye, accent `New` button) + a scrolling content host. Window grows to **1400×880** (min ~1180).
- **Navigation is refactored from 12 bespoke bool props/commands to a data-driven `NavItem` list.** `MainViewModel` exposes an ordered `IReadOnlyList<NavItem>` (key, title, Phosphor icon, the section VM, an `IsActive`) and a single `Navigate(key)` command; the rail, the top-bar title, and the palette all read this one model. This removes the 12-way duplication and is what makes "add a screen" and "the palette lists every screen" trivial. The existing per-section `Show…Command`s collapse into `Navigate`.

### Command palette & privacy

- **⌘K command palette** — a `CommandPaletteViewModel` (open, query, fuzzy-filtered items, selectedIndex) + a centered overlay view (dimmed/blurred backdrop, autofocused input, ↑/↓ move, ↵ run, esc/backdrop close, hover-select). Items: **Go to <every screen>** (from the `NavItem` list), **Switch theme**, **Show/Hide balances**, and placeholders for the create actions (wired for real in a later sprint). `Ctrl+K` is a global `MainWindow` hotkey.
- **App-wide privacy blur** — a shell `HideBalances` flag; monetary text carries `Classes="money"` and a style applies an Avalonia `BlurEffect` (with a short transition) when hidden. The pattern is defined once here and reused by every screen. Presentation-only — nothing about stored/transmitted values changes.

### Overview screen (the reference build)

- **Overview = the existing Dashboard, remapped to Coffer's real data — no fabricated metrics.** Layout follows the mock (KPI strip → trend chart panel → two-column row: recent activity / allocation donut + budgets mini), but the four KPI cells map to what `IDashboardQuery` actually provides in PLN (proposed: **Balance · Income (this month) · Spending (this month) · Net (this month)** — *not* Net worth/Investments/Debt, which Coffer has no data for; final labels are an open question). The trend area chart, recent-activity rows, spending-by-category donut, and budget mini-bars are rebuilt on the token/style system with LiveCharts restyled to the flat-fill / 2px-stroke / no-gridline look. This screen is the pattern every other screen copies.

## Approach — three PRs (foundation → shell → Overview)

- **28-A — design-system foundation (no screen rebuilt yet).** Tokens + light/dark `ThemeDictionaries`, `IThemeStore`/`theme.json` + startup application + un-pin `App.axaml`, embedded Hanken + Phosphor + tabular-nums, and the `Styles/` component library. Validated on a throwaway harness / the existing shell so both themes render.
- **28-B — terminal shell (rail + top bar + nav refactor + privacy + palette).** Rewrite `MainWindow` to the chrome, refactor `MainViewModel` to the `NavItem` model + `Navigate`, add the theme toggle, the privacy-blur pattern, and the ⌘K command palette. Existing section views still load (un-restyled) inside the new shell.
- **28-C — Overview (Dashboard) rebuilt in the system.** The reference screen end-to-end on tokens + the style library, in both themes, with privacy blur and the restyled charts.

## Steps

### 28-A — design-system foundation

- [ ] 28.1 `Theme/Tokens.axaml` — a `ResourceDictionary` with `ThemeVariant` `Light`/`Dark` `ThemeDictionaries`, every spec token as keyed `Color` + `SolidColorBrush` (`Coffer.Bg` … `Coffer.Accent`) + the 8 category color pairs; merged in `App.axaml`.
- [ ] 28.2 `IThemeStore` (`Coffer.Core`) + `FileThemeStore` (`Coffer.Infrastructure`, `theme.json`, Dark default, mirrors `FileLanguageStore`) + DI.
- [ ] 28.3 `App.axaml` un-pinned from Light; `App.axaml.cs` applies `IThemeStore.Load()` as `RequestedThemeVariant` at startup (pre-login); a shared `IThemeSwitcher`/helper flips + persists at runtime.
- [ ] 28.4 Embed **Hanken Grotesk** (400–800) + **Phosphor** (`ph` + `ph-fill`) as `avares://` fonts; shared `FontFamily` resources; a tabular-nums money style; a Phosphor glyph helper (name → glyph) so views reference icons by name.
- [ ] 28.5 `Styles/Components.axaml` (+ split files as needed): `Border.panel`, KPI cell, chip/segment, progress bar (+ over state), category icon tile (precomputed tints per theme), rail button, top-bar button, sortable header. Merged in `App.axaml`.
- [ ] 28.6 Both themes render on a validation surface (the existing shell or a scratch page); `dotnet format` clean; no CDN references.

### 28-B — terminal shell

- [ ] 28.7 `NavItem` (`Coffer.Application`) + `MainViewModel` refactor: an ordered `NavItem` list (key/title/icon/section VM/`IsActive`) + a single `Navigate(key)` command replacing the 12 `Show…Command`s and bool props; `ActiveTitle` for the top bar. Existing tests updated.
- [ ] 28.8 `MainWindow.axaml` rewritten to the chrome: 60px icon rail (logo mark, feature icons + gear pinned bottom, active = filled + `ActiveBg`) + 52px top bar (title, search, theme toggle, privacy eye, `New`) + content host; window 1400×880 (min 1180); `aria`/tooltip (`ToolTip`) on the icon-only rail.
- [ ] 28.9 Theme toggle (sun/moon) wired to `IThemeSwitcher` (live + persisted); balance-privacy `HideBalances` shell flag + the `money`/`BlurEffect` style pattern.
- [ ] 28.10 `CommandPaletteViewModel` (open/query/fuzzy items/selectedIndex, items from the `NavItem` list + Switch theme + Show/Hide balances + create placeholders) + `CommandPaletteView` overlay (dim/blur backdrop, autofocus, ↑/↓/↵/esc, hover-select, backdrop close); `Ctrl+K` global hotkey; `role="dialog"`-equivalent focus trapping.
- [ ] 28.11 Top-bar search filters/jumps handled minimally (full live-ledger search lands with the Transactions sprint); the field + ⌘K chip render and the palette opens.
- [ ] 28.12 Tests (`Coffer.Application.Tests`): `Navigate` switches the active item + title; the palette filters, moves selection, runs the selected command, and closes; the theme/privacy commands toggle their state.

### 28-C — Overview (Dashboard) rebuilt

- [ ] 28.13 `DashboardView.axaml` rebuilt on tokens + the style library: KPI strip (real PLN metrics), trend area chart, recent activity, allocation donut (spending-by-category), budgets mini — matching the mock layout; charts restyled (flat fill, 2px stroke, no gridlines).
- [ ] 28.14 KPI cells mapped to `IDashboardQuery` data in PLN (labels per the resolved open question); deltas colored `Pos`/`Neg`; `money` class on every figure (privacy blur); tabular-nums.
- [ ] 28.15 Localization: new `Nav.*` / `Overview.*` / `Palette.*` / `Theme.*` keys in both `.resx` (parity); no hardcoded user-facing literals.
- [ ] 28.16 Overview renders correctly in **both** themes with privacy blur; `DashboardViewModel` unchanged or minimally extended (no new query cost).

### Sweep

- [ ] 28.17 resx parity green; `dotnet format --verify-no-changes` clean (only pre-existing CRLF noise); no `RequestedThemeVariant="Light"` pins left; no hardcoded hex in the shell or Overview (tokens only). Accessibility: rail tooltips, visible focus, over-budget not color-only, palette focus trap.
- [ ] 28.18 Manual DoD click-through (below) — deferred to manual (needs a running app).

## Definition of Done

- **28-A (automated + visual):** both `ThemeDictionaries` resolve; `IThemeStore` round-trips (Dark default); fonts/icons load from `avares://` (no CDN); the style library renders on a validation surface in both themes.
- **28-B (automated):** `Navigate` switches active item + top-bar title; the ⌘K palette filters/moves/runs/closes; theme + privacy commands toggle state; the shell hosts the (un-restyled) section views without crashing.
- **28-C (automated + visual):** Overview renders in both themes with privacy blur, KPI figures are real PLN `IDashboardQuery` values (no fabricated net-worth/investment data), charts use the flat token style.
- **Manual:** launch → dark terminal shell; click rail icons to switch screens (title updates); ⌘K opens, arrow-keys + enter navigate, esc closes; sun/moon flips light/dark instantly and survives a restart; the eye blurs every figure; Overview matches the mock's layout adapted to PLN.
- **Whole-sprint:** the design system + shell + one reference screen are shipped; Sprints 29+ migrate the remaining screens mechanically against this foundation.

## Redesign program roadmap (Sprints 29+, indicative — not committed here)

- **Sprint 29 — Transactions + Accounts.** The dense ledger (filter chips, live search, sortable columns, In/Out totals, category dots) + a new **Accounts** screen built from imported accounts/balances (grouped, per-account sparkline; *no* APY/utilization/brokerage — Coffer's real fields only).
- **Sprint 30 — Budgets + Goals.** Budgets (summary + per-category progress, over = red) and Goals (the Advisor's goal cards: % funded, saved/target, target date) rebuilt in the system.
- **Sprint 31 — Insights + the remaining reskins.** Insights (Spending explorer + Forecast + trend/merchant analysis) as the "patterns over time" screen; plus token-migrating the still-light screens (Import, Assistant/chat, Alerts, Cash-flow planning, Affordability) and the pre-login windows (Login, Setup wizard, the recovery/settings dialogs) so the whole app is coherent in both themes.
- **Sprint 32 — interaction depth & polish.** Transaction detail drill-in side panel; wiring the `New`/⌘K create actions to real add/edit forms; empty/skeleton states; full accessibility pass; accent-color picker.

Then **resume Sprint 27** (disaster-recovery tail) on the redesigned surface.

## Files affected

- `src/Coffer.Desktop/App.axaml(.cs)` (un-pin theme, merge token + style dictionaries, apply stored theme); `MainWindow.axaml(.cs)` (rewritten shell)
- `src/Coffer.Desktop/Theme/Tokens.axaml` (new) + `Styles/*.axaml` (new) + `Assets/fonts/*` (Hanken, Phosphor) + a Phosphor glyph helper
- `src/Coffer.Core/Theming/IThemeStore.cs` + `AppTheme.cs` (new); `src/Coffer.Infrastructure/Theming/FileThemeStore.cs` (new) + DI
- `src/Coffer.Application/ViewModels/Main/MainViewModel.cs` (NavItem model + `Navigate`) + `NavItem.cs` (new) + `ViewModels/Shell/CommandPaletteViewModel.cs` (new)
- `src/Coffer.Desktop/Views/CommandPaletteView.axaml(.cs)` (new); `Views/DashboardView.axaml(.cs)` (rebuilt)
- `src/Coffer.Application/Localization/Strings.resx` + `Strings.pl.resx` (new keys, parity)
- `tests/Coffer.Application.Tests/**` (MainViewModel nav refactor, CommandPalette, theme/privacy toggles)

## Open questions

Recorded as decisions in `log.md` once settled:

- **Overview KPI cells — which four real metrics?** → proposed **Balance · Income (this month) · Spending (this month) · Net (this month)** in PLN (the mock's Net worth/Investments/Debt have no Coffer data). Confirm the four + their deltas.
- **Interim look of not-yet-migrated screens while dark is default?** → options: (a) keep the app **Light by default** until Sprints 29–31 finish, flipping the default to Dark at the end; (b) ship **Dark default now** and accept the un-migrated screens look light until their sprint; (c) a quick one-pass "token-ify backgrounds only" of the un-migrated views so they aren't blinding. Proposed **(a)** — least jarring, dark fully correct on migrated surfaces, flip the default when coverage completes. Confirm.
- **Rail order & which icons?** → proposed order Overview · Accounts · Transactions · Budgets · Goals · Insights · (divider) · Import · Assistant · Alerts · Cash-flow · Affordability · (gear) Settings. Confirm the set/order.
- **Phosphor delivery — embedded icon font vs per-icon SVG paths?** → proposed the **embedded font** (matches the prototype's web-font usage, one asset, easy weights). Confirm vs vector paths.
- **Accent color — fixed pine `#2F6B4F` for v1, or expose the swatch picker now?** → proposed **fixed for v1**, picker deferred to Sprint 32. Confirm.

## Deferred to a follow-up (kept out of scope)

- **All screens other than Overview** — Sprints 29–31 (see roadmap).
- **The pre-login windows (Login, Setup, recovery/settings dialogs) restyle** — Sprint 31; they keep working (light) until then.
- **Real create forms behind `New`/⌘K, transaction drill-in, empty/skeleton states, accent picker, keyboard ledger nav** — Sprint 32.
- **Live top-bar search across everything** — the ledger-scoped search lands with Transactions (Sprint 29); cross-entity search is later.
- **Resuming Sprint 27** (disaster-recovery tail) — after the redesign program.
