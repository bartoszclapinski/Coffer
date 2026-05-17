# Sprint index

Status as of: 2026-05-14

| Sprint | Phase | Goal | Status |
|---|---|---|---|
| [sprint-0](sprint-0/sprint-0.md) | 0 | Repo on GitHub, `Coffer.sln` with 5 production + 3 test projects, `dotnet build` green, CI green | Closed (2026-05-12) |
| [sprint-1](sprint-1/sprint-1.md) | 0 | DI plumbing, Serilog (console + file), Avalonia booted via DI, `MainWindow` with `ILogger<MainWindow>` | Closed (2026-05-13) |
| [sprint-2](sprint-2/sprint-2.md) | 0 | `IKeyVault` in Core, `WindowsDpapiKeyVault` (DPAPI, 7-day TTL), `InMemoryKeyVault` (cross-platform fallback), round-trip tests | Closed (2026-05-13) |
| [sprint-3](sprint-3/sprint-3.md) | 0 | `Argon2KeyDerivation`, `Bip39SeedManager`, `AesGcmCrypto`, `DekFile` format + serializer, 17 tests (including official BIP39 vectors) | Closed (2026-05-14) |
| [sprint-4](sprint-4/sprint-4.md) | 0 | `CofferDbContext` + `_SchemaInfo`, `SqlCipherKeyInterceptor` (PRAGMA key per connection), `MigrationRunner` with pre-migration backup callback, first migration `InitialCreate`, 9 integration tests | Closed (2026-05-17) |
| [sprint-5](sprint-5/sprint-5.md) | 0 | Avalonia setup wizard (welcome → master password with zxcvbn → BIP39 display + verification → confirm), `IDekHolder`, `SetupService` orchestrator, `IScreenCaptureBlocker` (Win32 P/Invoke); first interactive UI | Planned |

## Planned sprints in Phase 0 (full plans drafted at the start of each sprint)

- **Sprint 6** — Login window (DPAPI cache), `LastActivityTracker` + 15-min auto-lock, `MainWindow` placeholder "logged in as", Phase 0 closure

## After Phase 0

- **Phase 1** — PKO BP parser (sprints planned once Phase 0 closes)
- **Phase 2** — Import flow + transaction list
- **Phase 3** — Sync via Google Drive
- **Phase 4** — Auto-categorisation
- **Phase 5+** — Receipts (from this phase, mobile/MAUI is needed)

Mobile (`Coffer.Mobile`, MAUI) — postponed until Phase 5 is approaching or it is needed earlier.

> Note: Sprint plans and logs for Sprints 0-3 are in Polish (historical artefacts from the early phase of the project). All sprints from Sprint 4 onward are in English. See [.ai/sprints/README.md](README.md) for the language policy.
