# Sprint 15 log

## 2026-06-28

- `plan` Sprint 15 planned — cross-cutting bilingual UI (PL + EN) i18n with a runtime language switch. Three PRs: 15-A i18n foundation + pilot (MainWindow nav + Settings), 15-B remaining views + VM-formatted strings, 15-C engine/domain display strings + sweep + `docs/conventions.md` update.
- `decision` pre-login language source = **plaintext app-data file** (not the encrypted DB), because setup/login render before the DEK exists; language preference is non-sensitive so plaintext is acceptable under the privacy posture.
- `decision` neutral resource culture = **English** (`Strings.resx`), Polish as satellite (`Strings.pl.resx`) — standard .NET satellite-assembly model, simplest fallback.
- `decision` setup wizard is localized now (15-B), not deferred — it is a visible first-run surface.
- `decision` money/number formatting stays `pl-PL` + `" zł"` regardless of UI language in v1 (PLN-first app); locale-aware number/date formatting deferred.
- `context` mapping found ~250 hardcoded strings across 16 Avalonia views + 27 C# files, with **no** existing i18n infrastructure (greenfield migration). Heaviest view: `GoalsView.axaml` (~55 strings).
- `15-A` i18n foundation built end-to-end and proven on a pilot slice. Core: `AppLanguage` enum + `ILanguageStore`. Application: `ILocalizer` + `Localizer` (ResourceManager over `Strings.resx`/`Strings.pl.resx`, runtime switch via `"Item[]"` indexer notification). Infrastructure: `FileLanguageStore` (plaintext `language.json` in app-data, defaults to Polish). Desktop: `{l:Localize Key}` markup extension + apply-saved-language at startup. Pilot migration: `MainWindow` sidebar nav + `SettingsView` (incl. new language picker combo). 434 tests green.
- `15-A` deferred: `MainWindow` version label (`Wersja {0}`) left in Polish — combining `l:Localize` with a bound `StringFormat` needs a converter/MultiBinding; handled in 15-B.

## 2026-06-29

- `15-B` migrated every remaining view and VM-formatted string off hardcoded Polish onto resource keys. Views: Dashboard, Import, Transactions, Chat, Alerts, Goals/Doradca, Login, Setup wizard (4 steps + confirm), plus the `MainWindow` version label and the migration/partial-vault dialogs in `App.axaml.cs`. VMs format through `ILocalizer` (computed properties such as `SubtitleText`/`SummaryAddedText`/`VersionText` for live-bound format strings; error/status messages resolved on demand).
- `15-B` two mechanisms carry resource keys held in data rather than XAML: `LocalizeKeyConverter` (filter sentinels, `DateRangeOption.LabelKey`) and the `{l:Localize}` markup extension (static labels, incl. DataGrid headers — works because it binds with an explicit `Source = localizer`, not the DataContext).
- `15-B` `AvaloniaFilePicker` now resolves its dialog title and file-type label through `ILocalizer` (kept behind the `IFilePicker` interface, hard rule #4).
- `15-B` added ~190 keys to `Strings.resx` (EN neutral) + `Strings.pl.resx` (PL). Money/number formatting stays `pl-PL` + `" zł"` per the v1 decision; only surrounding labels are localized. VM display strings re-evaluate on next page load (no live refresh required).
- `15-B` tests: added `FakeLocalizer` to every VM constructor whose signature gained `ILocalizer`; error-message assertions that checked Polish substrings now assert the resolved resource key (the fake echoes keys). 434 tests green, build clean.
- `15-B` out of scope (→ 15-C): anomaly `AlertRowViewModel` Title/Description text, a key-parity test across the two resx files, and the `docs/conventions.md` i18n note.
