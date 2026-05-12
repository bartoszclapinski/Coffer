# Index sprintów

Status na: 2026-05-12

| Sprint | Faza | Cel | Status |
|---|---|---|---|
| [sprint-0](sprint-0/sprint-0.md) | 0 | Repo na GitHubie, `Coffer.sln` z 5 projektami + 3 testowymi, `dotnet build` zielono, CI zielony | Planowany |

## Zaplanowane sprinty Fazy 0 (pełne plany powstają na początku każdego sprintu)

- **Sprint 1** — DI plumbing (`ServiceRegistration` extensions), Serilog (plik + konsola), pusty `MainWindow` w Avalonia uruchamiany z DI bootstrap
- **Sprint 2** — `IKeyVault` w Core + `WindowsDpapiKeyVault` (DPAPI cache 7 dni) + testy round-trip
- **Sprint 3** — `Argon2KeyDerivation`, `Bip39SeedManager` (NBitcoin), format pliku `dek.encrypted`, AES-GCM helpers, testy z wektorami BIP39
- **Sprint 4** — `CofferDbContext` + `_SchemaInfo`, `SqlCipherKeyInterceptor`, `IDbContextFactory`, pierwsza migracja, szkielet `MigrationRunner` z hookiem na backup, test integracyjny szyfrowania
- **Sprint 5** — Avalonia setup wizard (welcome → password z zxcvbn → BIP39 display z `SetWindowDisplayAffinity` → verification → confirm); pierwszy run tworzy DEK
- **Sprint 6** — Login window (DPAPI cache), `LastActivityTracker` + auto-lock 15 min, `MainWindow` placeholder "logged in as", zamknięcie Fazy 0

## Po Fazie 0

- **Faza 1** — parser PKO BP (sprinty rozplanowane po zamknięciu Fazy 0)
- **Faza 2** — import flow + lista transakcji
- **Faza 3** — sync via Google Drive
- **Faza 4** — auto-kategoryzacja
- **Faza 5+** — paragony (od tej fazy potrzebne mobile/MAUI)

Mobile (`Coffer.Mobile`, MAUI) — odłożone do momentu zbliżania się do Fazy 5 lub gdy będzie potrzebne wcześniej.
