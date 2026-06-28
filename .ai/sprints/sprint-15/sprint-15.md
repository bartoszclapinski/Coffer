# Sprint 15 — Bilingual UI (PL + EN) i18n

**Phase:** — (cross-cutting; not a roadmap feature phase — an i18n foundation under all desktop phases)
**Status:** Planned
**Depends on:** none (touches every shipped desktop view, so effectively all of sprints 5–14)

## Goal

The owner flips a language toggle in **Ustawienia/Settings** and the **entire** desktop UI — sidebar, every page, engine-origin labels (goal status/type, scenario names, anomaly text), and VM status/error messages — switches between **Polish and English at runtime without a restart**, with the choice persisted across restarts and honored on the pre-login (setup/login) screens.

## Approach — three PRs ("foundation before breadth", Sprint-10/12/13/14 cadence)

- **15-A — i18n foundation + pilot.** Build the whole mechanism end-to-end and prove it on one slice (MainWindow sidebar + Settings page), so the loop — resources → service → markup extension → runtime refresh → persistence → startup wiring → language switch — is validated before the bulk migration. No behavior change to other pages yet.
- **15-B — views + VM strings.** Migrate every remaining `.axaml` view and every VM-formatted string to resource keys: Dashboard, Transactions, Import, Chat, Alerts, Login, the Setup wizard (5 views), Goals/Doradca, plus VM display mappers (`GoalDisplay`, `DateRangeOption`) and status/error messages.
- **15-C — engine/domain display strings + sweep.** Localize the deterministic display text currently rendered raw (goal scenario labels "Current pace"/"Max sustainable"/"On target", risk messages, anomaly templated text, `"Bez kategorii"`), keeping `Coffer.Core` free of presentation strings (hard rule #3). Final sweep for stragglers + manual DoD click-through. Update `docs/conventions.md` to retire the Polish-only rule.

**Conventions established here:** English is the **neutral** resource (`Strings.resx`); Polish is the satellite (`Strings.pl.resx`). Resource keys are dotted by area — `Nav.Dashboard`, `Settings.ApiKey.Save`, `Goal.Status.OnTrack`, `Import.Error.UnknownBank`. The language choice is **non-sensitive** and stored in a plaintext app-data file (not the encrypted DB) so setup/login can read it before the DEK exists. Currency stays formatted with the existing `pl-PL` number format + `" zł"` suffix (money formatting is not language-switched in v1 — Polish złoty regardless of UI language).

## Steps

### 15-A — i18n foundation + pilot

- [ ] 15.1 Resources (`Coffer.Application/Localization/`): `Strings.resx` (neutral = **English**) + `Strings.pl.resx` (Polish), with the keys needed for the pilot (nav + settings). `Strings.Designer.cs` strongly-typed accessor optional — the localizer reads via `ResourceManager`, so raw `.resx` is enough.
- [ ] 15.2 `AppLanguage` enum (`Coffer.Core/Localization/` — pure, no strings): `Polish`, `English`. Maps to culture codes `pl` / `en`.
- [ ] 15.3 `ILocalizer` (`Coffer.Application/Localization/`): `string this[string key]`, `string Format(string key, params object[] args)`, `AppLanguage Current`, `void SetLanguage(AppLanguage lang)`, `event EventHandler? LanguageChanged`. Implementation `Localizer` (singleton) backed by `ResourceManager` + `CultureInfo`; `SetLanguage` sets `CultureInfo.CurrentUICulture`/`DefaultThreadCurrentUICulture` and raises `LanguageChanged`.
- [ ] 15.4 Language persistence (`Coffer.Core/Localization/ILanguageStore.cs` + `Coffer.Infrastructure/Localization/FileLanguageStore.cs`): read/write the chosen `AppLanguage` to a **plaintext** JSON file under the app-data folder (non-secret; readable pre-login). Default = `Polish` when absent. No DB, no migration.
- [ ] 15.5 Avalonia `LocalizeExtension` (`Coffer.Desktop/Localization/`): `{l:Localize Key}` markup extension resolving against the singleton `ILocalizer`; subscribes to `LanguageChanged` and pushes the new value so bound controls refresh live (no restart).
- [ ] 15.6 Startup wiring: register `ILocalizer` (singleton) + `ILanguageStore` in `AddCofferApplication`/`AddCofferInfrastructure`; in `App.OnFrameworkInitializationCompleted` (before resolving the first window) load the saved language and apply it.
- [ ] 15.7 Settings language switch: `LanguageOptions` + `SelectedLanguage` on `SettingsViewModel`; changing it calls `ILocalizer.SetLanguage` and persists via `ILanguageStore`. Wire a `ComboBox` ("Polski" / "English") into `SettingsView.axaml`.
- [ ] 15.8 Pilot migration: convert `MainWindow.axaml` sidebar nav + `SettingsView.axaml` to `{l:Localize ...}` keys; confirm flipping the toggle re-labels both live.
- [ ] 15.9 Tests: `Localizer` returns the right string per language and falls back to the neutral (English) value for a missing satellite key; `SetLanguage` raises `LanguageChanged`; `FileLanguageStore` round-trips and defaults to Polish when the file is absent; `SettingsViewModel` language-change persists + switches.

### 15-B — views + VM strings

- [ ] 15.10 Migrate remaining views to `{l:Localize ...}`: `DashboardView`, `TransactionsView`, `ImportView`, `ChatView`, `AlertsView`, `LoginWindow`, the 5 Setup wizard views, `GoalsView`. Add their keys to both `.resx` files.
- [ ] 15.11 Migrate VM display mappers: `GoalDisplay` (status/type/priority → keys), `DateRangeOption` ("Ostatnie 3/6/12 mies.", "Cały okres" → keys). These now resolve via `ILocalizer`, so they re-evaluate on the next load after a switch.
- [ ] 15.12 Migrate VM status/error messages: `ImportViewModel`, `SettingsViewModel`, `AlertsViewModel`, `ChatViewModel`, `GoalsViewModel`, `LoginViewModel`, `DashboardViewModel` — replace Polish literals with `_localizer["..."]` (inject `ILocalizer`).
- [ ] 15.13 App-level dialogs: localize the hardcoded error/confirmation strings in `App.axaml.cs` (migration confirmation, partial-vault errors).
- [ ] 15.14 Tests: update existing VM/view tests that assert on hardcoded Polish text to assert on the localizer-resolved value (or a fake localizer returning keys); add a fake `ILocalizer` to the test fakes.

### 15-C — engine/domain display strings + sweep

- [ ] 15.15 Goal scenario/risk labels: the engine-origin strings currently rendered raw ("Current pace", "Max sustainable", "On target", risk messages) become resource keys resolved at the VM/display boundary — `Coffer.Core` strategies emit a stable **code/enum**, the presentation string is looked up in `Strings.resx`. No presentation text remains in `Coffer.Core`.
- [ ] 15.16 Anomaly display text: `AnomalyFormatting` (Infrastructure) templated Polish + `"Bez kategorii"` move to keys; templated alert text becomes a localizable format string per `AnomalyType`.
- [ ] 15.17 Final sweep: grep for residual Polish/English literals in views + VMs; ensure every `{l:Localize}` key exists in both `.resx` files (a small test asserting key parity between neutral and `pl` is encouraged).
- [ ] 15.18 Docs: update `docs/conventions.md` to replace the "UI strings are Polish-only" rule with the bilingual + resource-key policy; note the key-naming convention.
- [ ] 15.19 Manual DoD click-through (below).

## Definition of Done

- **15-A (automated + manual):** `Localizer` unit tests pass (per-language lookup, neutral fallback, `LanguageChanged`, persistence round-trip/default). Manual: launch app, open Ustawienia, flip Polski↔English → the **sidebar and Settings page** re-label instantly with no restart; restart the app → the chosen language persists.
- **15-B (automated):** VM/view tests assert localized output via the localizer (no remaining hardcoded-Polish assertions for migrated areas).
- **15-C (automated):** key-parity test holds (every neutral key has a `pl` counterpart); no residual user-facing literals in the swept areas.
- **Whole-sprint (manual):** with the app in English, walk Dashboard → Transactions → Import → Chat → Alerts → Doradca and the **setup/login** screens — every visible label, status message, goal status/type, scenario label, and anomaly text is in English; flip back to Polski and all of it switches live. Money still shows as "1 234,50 zł".

## Files affected

- `src/Coffer.Core/Localization/` (new): `AppLanguage.cs`, `ILanguageStore.cs`
- `src/Coffer.Application/Localization/` (new): `ILocalizer.cs`, `Localizer.cs`, `Strings.resx`, `Strings.pl.resx`
- `src/Coffer.Infrastructure/Localization/FileLanguageStore.cs` (new)
- `src/Coffer.Desktop/Localization/LocalizeExtension.cs` (new); `App.axaml.cs` (startup language load + localized dialogs)
- `src/Coffer.*/DependencyInjection/*ServiceRegistration.cs` (register `ILocalizer`/`ILanguageStore`)
- `src/Coffer.Desktop/Views/*.axaml` (all 16 views → `{l:Localize}`)
- `src/Coffer.Application/ViewModels/**` (`SettingsViewModel`, `GoalDisplay`, `DateRangeOption`, `ImportViewModel`, `AlertsViewModel`, `ChatViewModel`, `GoalsViewModel`, `LoginViewModel`, `DashboardViewModel`)
- `src/Coffer.Infrastructure/Anomalies/AnomalyFormatting.cs`
- `docs/conventions.md` (retire Polish-only rule)
- `tests/Coffer.Application.Tests/**`, `tests/Coffer.Infrastructure.Tests/**` (localizer tests, fake `ILocalizer`, updated assertions)

## Open questions

All resolved by the owner (2026-06-28), recorded as decisions in `log.md`:
- **Pre-login language source** → **plaintext app-data file** (non-sensitive), so setup/login honor the choice before the DEK exists. Not the encrypted DB.
- **Neutral resource culture** → **English** (`Strings.resx`); Polish is the satellite (`Strings.pl.resx`).
- **Setup wizard scope** → **localize now** (in 15-B), despite being a one-time flow.

## Deferred to a follow-up

- **Money/number culture switching** — currency stays `pl-PL` + `" zł"` regardless of UI language in v1 (the app is PLN-first). Locale-aware number/date formatting per UI language is a separate concern.
- **Additional languages** — the architecture supports more satellites (`Strings.de.resx` etc.), but only PL + EN ship here.
- **MAUI/mobile localization** — the `ILocalizer` lives in `Coffer.Application` so the mobile app can reuse it, but mobile views are out of scope until the mobile track starts.
