# Sprint index

Status as of: 2026-05-22

| Sprint | Phase | Goal | Status |
|---|---|---|---|
| [sprint-0](sprint-0/sprint-0.md) | 0 | Repo on GitHub, `Coffer.sln` with 5 production + 3 test projects, `dotnet build` green, CI green | Closed (2026-05-12) |
| [sprint-1](sprint-1/sprint-1.md) | 0 | DI plumbing, Serilog (console + file), Avalonia booted via DI, `MainWindow` with `ILogger<MainWindow>` | Closed (2026-05-13) |
| [sprint-2](sprint-2/sprint-2.md) | 0 | `IKeyVault` in Core, `WindowsDpapiKeyVault` (DPAPI, 7-day TTL), `InMemoryKeyVault` (cross-platform fallback), round-trip tests | Closed (2026-05-13) |
| [sprint-3](sprint-3/sprint-3.md) | 0 | `Argon2KeyDerivation`, `Bip39SeedManager`, `AesGcmCrypto`, `DekFile` format + serializer, 17 tests (including official BIP39 vectors) | Closed (2026-05-14) |
| [sprint-4](sprint-4/sprint-4.md) | 0 | `CofferDbContext` + `_SchemaInfo`, `SqlCipherKeyInterceptor` (PRAGMA key per connection), `MigrationRunner` with pre-migration backup callback, first migration `InitialCreate`, 9 integration tests | Closed (2026-05-17) |
| [sprint-5](sprint-5/sprint-5.md) | 0 | Avalonia setup wizard (welcome → master password with zxcvbn → BIP39 display + verification → confirm), `IDekHolder`, `SetupService` orchestrator, `IScreenCaptureBlocker` (Win32 P/Invoke); first interactive UI | Closed (2026-05-18) |
| [sprint-6](sprint-6/sprint-6.md) | 0 | `ILoginService` (DPAPI cache → password fallback), `ILastActivityTracker` + `AutoLockMonitor` (15-min idle), `LoginWindow`, `MainWindow` upgrade ("Zalogowano" + version + "Wyloguj"); closes Phase 0 | Closed (2026-05-20) |
| [sprint-7](sprint-7/sprint-7.md) | 1 | Parsing foundations: `IStatementParser` / `IBankDetector` / `StatementParserRegistry`, Polish-format helpers, `FingerprintBankDetector` (PKO + 6 inert), `PkoBpStatementParser` standard checking, `TransactionHash`, FsCheck property-based tests + synthetic QuestPDF fixtures | Planned |

**Phase 0 closed end-to-end on 2026-05-20.** Cold-start setup → wizard → `MainWindow`; subsequent cold start → DPAPI cache bypass OR login window → `MainWindow`; 15-min idle → auto-lock → login. ~104 automated tests, CI green.

**Phase 1 opens with Sprint 7** — deterministic PKO BP parser for the standard checking layout plus the surrounding infrastructure (interfaces, Polish helpers, registry, dedup hash). Sprint 8 will add the AI fallback, Anonymizer CLI, remaining PKO layouts, and golden-file tests against anonymized samples.

## Beyond Phase 1

- **Phase 2** — Import flow + transaction list
- **Phase 3** — Sync via Google Drive
- **Phase 4** — Auto-categorisation
- **Phase 5+** — Receipts (from this phase, mobile/MAUI is needed)

Mobile (`Coffer.Mobile`, MAUI) — postponed until Phase 5 is approaching or it is needed earlier.

> Note: Sprint plans and logs for Sprints 0-3 are in Polish (historical artefacts from the early phase of the project). All sprints from Sprint 4 onward are in English. See [.ai/sprints/README.md](README.md) for the language policy.
