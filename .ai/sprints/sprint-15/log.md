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
