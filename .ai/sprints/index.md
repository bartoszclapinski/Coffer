# Index sprintów

Status na: 2026-05-12

| Sprint | Faza | Cel | Status |
|---|---|---|---|
| [sprint-0](sprint-0/sprint-0.md) | 0 | Repo na GitHubie, `Coffer.sln` z 5 projektami + 3 testowymi, `dotnet build` zielono, CI zielony | Zamknięty (2026-05-12) |
| [sprint-1](sprint-1/sprint-1.md) | 0 | DI plumbing, Serilog (konsola+plik), Avalonia uruchamiane z DI bootstrap, `MainWindow` z `ILogger<MainWindow>` | Zamknięty (2026-05-13) |
| [sprint-2](sprint-2/sprint-2.md) | 0 | `IKeyVault` w Core, `WindowsDpapiKeyVault` (DPAPI, 7-day TTL), `InMemoryKeyVault` (cross-platform fallback), testy round-trip | Zamknięty (2026-05-13) |
| [sprint-3](sprint-3/sprint-3.md) | 0 | `Argon2KeyDerivation`, `Bip39SeedManager`, `AesGcmCrypto`, `DekFile` format + serializer, 17 testów (w tym z oficjalnymi BIP39 vectorami) | Planowany |

## Zaplanowane sprinty Fazy 0 (pełne plany powstają na początku każdego sprintu)
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
