# Sprint 27 — Disaster-recovery tail (archive restore + auto-test + regenerate seed)

**Phase:** — (roadmap-adjacent; `docs/architecture/08-backup-and-recovery.md` — the restore/self-test/recovery-seed items still open after Sprints 23–26. Closes doc-08 to 100%.)
**Status:** Planned
**Depends on:** sprint-23 (`IArchiveExporter` + the `.zip` layout, `BackupSnapshotWriter`, `IFilePicker` save), sprint-24 (`IRestoreService` + snapshot listing, `restore-pending.json`, startup-before-DB apply, `RestoreResult`), sprint-25 (`DekFile` v2 dual-wrap, `ISeedRecoveryService.EnableSeedRecoveryAsync`, the generate→display→verify seed flow, `IDekHolder`, `IVaultPaths`), sprint-26 (`IMasterPasswordService`, the re-wrap technique), sprint-5/6 (setup wizard, login, `IScreenCaptureBlocker`), sprint-15 (i18n). No new database schema.

## Goal

The three remaining disaster-recovery items from doc-08's Settings mock all work: (1) a fresh install / **foreign machine** can restore from an **exported archive `.zip`** and then log in normally (master password *or* seed); (2) a **monthly auto-test restore** silently verifies the newest daily snapshot is openable and intact, warning the owner if it is not; (3) a password-gated **"Regenerate recovery seed"** mints a fresh 12-word seed, re-wraps the DEK, and invalidates the old paper seed. Deterministic; no AI, no network, no schema change.

## Why this sprint exists

Sprints 23–26 built backup (local snapshots + archive export), same-vault restore (from a daily snapshot), the seed-recovery channel (DEK dual-wrap + forgot-password), and password rotation. Doc-08's recovery story still has three holes:

- **The archive is write-only.** `ArchiveExporter` produces a portable `.zip` (encrypted DB + `dek.encrypted` + manifest) meant for "a USB drive in a safe" — but nothing *reads* it back. The headline disaster ("SSD dies / new machine") is therefore **not actually survivable through the UI**: a fresh install only offers the setup wizard. This is the biggest remaining gap and the whole point of "finish restore".
- **Backups are never verified.** Doc-08 §"Auto-test restore" promises a monthly silent self-test ("this catches silently corrupted backups before the user needs them"). Sprint 24 deliberately unblocked it (`IRestoreService.ListSnapshotsAsync`) but never wired it. A backup you cannot restore is not a backup.
- **The recovery seed is un-showable and un-rotatable.** Doc-08's Settings mock lists "[Show recovery seed]". By design (hard rule #6) the mnemonic is **never persisted** — only the seed-*wrapped* DEK is, and BIP39 is one-way — so the original words are unrecoverable. The honest, privacy-preserving equivalent is **regeneration**: mint a *new* seed, re-wrap, and tell the owner to rewrite their paper. (Owner decision, 2026-07-08: reframe "Show" as "Regenerate".)

Closing all three brings doc-08 from "backup + partial restore" to a complete, drill-able disaster-recovery story.

## Design decisions (the shape we commit to)

### Archive restore

- **Restore = drop the already-encrypted bytes into place, then let the normal login flow unlock.** The archive's `coffer.db` and `dek.encrypted` are already SQLCipher/AES-GCM ciphertext; a foreign machine restore needs **no crypto in the importer** — it validates and extracts the files into the `IVaultPaths` locations, and the *existing* login window (password) or its "Restore from seed" branch (forgotten password) does the unlocking. The DEK is dual-wrapped (v2) or password-only (v1) exactly as it was on the source machine; nothing about the keys changes on import.
- **Both `coffer.db` and `dek.encrypted` are mandatory in the archive.** Without the DEK the database is noise; without the database there is nothing to restore. A missing either → `InvalidArchiveException` (the archive is not a Coffer recovery package). `-wal`/`-shm` side-files are optional (restored if present). The `manifest.json` `app == "Coffer"` marker is checked; a mismatched/absent manifest → `InvalidArchiveException`.
- **Import only entries by known name — no path traversal, no arbitrary extraction.** The importer reads the specific entries `coffer.db`, `coffer.db-wal`, `coffer.db-shm`, `dek.encrypted` by exact name and writes each to its fixed vault path. It never honours an entry's path, so a crafted zip cannot escape the vault folder (zip-slip safe by construction).
- **Fresh-install only, and it refuses to clobber an existing vault.** The restore-from-archive entry appears **only on the "neither DB nor DEK exists" startup branch** (the true foreign-machine / SSD-died case). If either vault file is already present, `ImportAsync` throws `VaultAlreadyExistsException` and touches nothing — restoring *over* a live vault is Sprint 24's same-vault snapshot restore, a different, guarded flow. Extraction is atomic-ish: files are written to temp names beside the targets and moved into place; a mid-extract crash leaves no half-vault that would trip the partial-state startup error.
- **After a successful import, re-enter the normal startup routing.** Import turns a "neither exists" machine into a "both exist" one, so control flows to the login window — the owner logs in with their password, or clicks "Restore from seed" if the password is what they lost. No bespoke post-import unlock path.

### Monthly auto-test restore

- **Verify the newest daily snapshot, in a temp copy, with the in-memory DEK.** Following doc-08 §"Auto-test restore": copy the newest `backups/coffer-*.db` to a temp file, open it as a SQLCipher database keyed with the **in-memory DEK** (`IDekHolder` — so the test only runs in an unlocked session), run `PRAGMA integrity_check` plus a `SELECT COUNT(*)` across the user tables, then delete the temp copy. A raw `SqliteConnection` + the SQLCipher `PRAGMA key` (the same hex-key application the `SqlCipherKeyInterceptor` uses) keeps the check independent of EF and of the live DB.
- **Idempotent per calendar month via a persisted marker.** A small `restore-selftest.json` in `LocalAppDataFolder` records `LastRunMonth` / `LastPassed` / `LastError` / `LastRunUtc`. The startup job runs the test at most once per month (skips if `LastRunMonth == this month`). No timer infrastructure — a fire-and-forget `Task.Run` after a start-up delay, mirroring Sprint 23's `StartDailyBackup`.
- **A failure is surfaced, never thrown.** On integrity failure (or no snapshot to test) the marker records the failure and the Settings Backup & Recovery panel shows "Last self-test restore: <date> ✗ failed" (red); a passing run shows "✓ passed". The panel also gets a **"Test restore"** on-demand button (doc-08 mock) that runs the same service immediately. The self-test never mutates the live DB, the snapshots, or the DEK.

### Regenerate recovery seed

- **"Regenerate", not "Show" — the mnemonic is unrecoverable by design.** The action mints a **fresh** 12-word seed, wraps the in-memory DEK with the new recovery key, and rewrites `dek.encrypted` (v2) — the exact operation `ISeedRecoveryService.EnableSeedRecoveryAsync` already performs and tests. The old paper seed stops working the moment the seed blob is overwritten; the flow tells the owner to rewrite it. This reuses Sprint 25's generate → display (screen-capture-blocked) → verify UI wholesale.
- **Password-gated (doc-08 says so).** Before the new seed is shown, the owner re-enters the **current master password**, verified by a new lightweight `IMasterPasswordService.VerifyMasterPasswordAsync` (derive key, decrypt the password blob — the same check as login, no re-wrap). This defends a walk-up attacker at an unlocked session from silently rotating the seed and pocketing the new words (same rationale as Sprint 26's current-password requirement). A wrong password blocks the flow; nothing is generated or written.
- **Available only when seed recovery is already enabled.** On a v1 (inert-seed) vault the Settings entry is still "Enable seed recovery" (Sprint 25) — which also mints a fresh functional seed. Once v2, the entry becomes "Regenerate recovery seed". Both paths converge on `EnableSeedRecoveryAsync`; the only new surface is the password gate.
- **Nothing sensitive logged or persisted.** The new mnemonic lives only in memory (generate → display → verify → wrap), then is zeroed; only the outcome is logged (hard rule #6). No seed, password, or key on disk in plaintext.

### Cross-cutting

- **Consolidate the backups path onto `IVaultPaths`.** The `backups/` folder is currently a duplicated private constant in `BackupService` and `RestoreService`. Archive safety copies and the self-test both need it, so add `IVaultPaths.BackupsFolder` and point the existing services at it (behaviour unchanged) — a small, test-covered cleanup rather than a fourth private copy.

## Approach — four PRs, headless-first per feature

- **27-A — archive import (headless).** `IArchiveImporter` / `ArchiveImporter` (validate → extract to vault, refuse over an existing vault), `IVaultPaths.BackupsFolder` consolidation, `IFilePicker.PickOpenArchiveFileAsync`, exhaustive tests including the **round-trip** (export a vault → import into an empty vault → the DEK file + DB are byte-identical and a `LoginService` unlock succeeds). No pixels.
- **27-B — fresh-install "Restore from archive" UI.** A "Restore from a backup archive" affordance on the setup wizard's Welcome step raising `RestoreFromArchiveRequested`; the `App` handler picks the `.zip`, calls `ImportAsync`, and re-resolves the startup window (now → login). Localized errors (bad archive / vault already present). Fully localized.
- **27-C — monthly auto-test restore.** `IRestoreSelfTest` / `RestoreSelfTestService` + the `restore-selftest.json` marker, a `StartMonthlyRestoreTest()` startup job in `BuildMainWindow`, and the Settings status line + "Test restore" button. Headless service fully tested; small UI surface.
- **27-D — regenerate recovery seed.** `IMasterPasswordService.VerifyMasterPasswordAsync`; a password-gated `RegenerateRecoverySeedViewModel` reusing the generate→display→verify steps then calling `EnableSeedRecoveryAsync`; the Settings entry flips to "Regenerate recovery seed" once v2, behind an `IRegenerateRecoverySeedDialog` seam (screen-capture-blocked). Fully localized.

## Steps

### 27-A — archive import (headless)

- [ ] 27.1 `IVaultPaths.BackupsFolder` (`Coffer.Core/Security/`) + `CofferVaultPaths` impl (`= {LocalAppDataFolder}\backups`); repoint `BackupService`/`RestoreService` at it (behaviour unchanged, existing tests green).
- [ ] 27.2 `IArchiveImporter` (`Coffer.Core/Backup/`): `Task<ArchiveImportResult> ImportAsync(string zipPath, ct)`; `ArchiveImportResult(string? AppVersion, bool SeedWrapPresent)`; documents `InvalidArchiveException` / `VaultAlreadyExistsException`.
- [ ] 27.3 `InvalidArchiveException` + `VaultAlreadyExistsException` (`Coffer.Core/Backup/`, no sensitive detail).
- [ ] 27.4 `ArchiveImporter` (`Coffer.Infrastructure/Backup/`, `IVaultPaths` + `ILogger`): open zip; require `manifest.json` (`app == "Coffer"`), `coffer.db`, `dek.encrypted` (else `InvalidArchiveException`); refuse if `DatabaseFile` or `EncryptedDekFilePath` already present (`VaultAlreadyExistsException`); extract the four known entries by exact name to their vault paths (temp-name + move); report `AppVersion` from the manifest + whether the extracted DEK is v2 (peek `DekFile.ReadAsync`). Registered singleton.
- [ ] 27.5 `IFilePicker.PickOpenArchiveFileAsync` (`Coffer.Core/Import/`) + Avalonia impl (`.zip` filter); returns null on cancel.
- [ ] 27.6 DI: `IArchiveImporter` → `ArchiveImporter` in `AddCofferBackup`.
- [ ] 27.7 Tests (`Coffer.Infrastructure.Tests/Backup`): **round-trip** (export → import into empty vault → DEK+DB byte-identical, `LoginService` unlocks with the password); missing DB / missing DEK / bad manifest → `InvalidArchiveException`; import over an existing vault → `VaultAlreadyExistsException` (files untouched); side-files restored when present / skipped when absent; a v2 archive reports `SeedWrapPresent`; a path-traversal entry name is ignored (only known names extracted).

### 27-B — fresh-install "Restore from archive" UI

- [ ] 27.8 `WelcomeStepViewModel`: a `RestoreFromArchiveCommand` raising a `RestoreFromArchiveRequested` event, bubbled through `SetupWizardViewModel`.
- [ ] 27.9 `SetupWizardWindow` / Welcome view: a secondary "Restore from a backup archive instead" action under the "Start fresh" primary.
- [ ] 27.10 `App.axaml.cs`: an `OnRestoreFromArchiveRequested` handler — `IFilePicker.PickOpenArchiveFileAsync` → `IArchiveImporter.ImportAsync` → on success re-resolve the startup window (now → login); on `InvalidArchiveException`/`VaultAlreadyExistsException` show a localized message and stay on the wizard.
- [ ] 27.11 Localization: `Setup.Restore.*` (archive button, picker title, bad-archive / vault-present errors) in both `.resx` (parity).
- [ ] 27.12 Tests (`Coffer.Application.Tests`): the Welcome VM raises `RestoreFromArchiveRequested`; the wizard bubbles it. (App-level import wiring is covered by 27-A's importer tests + manual DoD.)

### 27-C — monthly auto-test restore

- [ ] 27.13 `IRestoreSelfTest` (`Coffer.Core/Backup/`): `Task<RestoreSelfTestResult> RunAsync(ct)`; `RestoreSelfTestResult(bool Passed, DateOnly? SnapshotTested, string? Error)`; `Task<RestoreSelfTestStatus?> GetStatusAsync(ct)`; `RestoreSelfTestStatus(DateOnly LastRunDate, bool LastPassed, string? LastError)`.
- [ ] 27.14 `RestoreSelfTestService` (`Coffer.Infrastructure/Backup/`, `IVaultPaths` + `IDekHolder` + `ILogger`): newest `backups/coffer-*.db` → temp copy → raw `SqliteConnection` keyed with the in-memory DEK (`PRAGMA key` hex, reusing the interceptor's key formatting) → `PRAGMA integrity_check` + `SELECT COUNT(*)` per user table → delete temp → write `restore-selftest.json` (`LastRunMonth`/`LastPassed`/`LastError`/`LastRunUtc`, atomic tmp+move). No snapshot / DEK unavailable → a recorded non-fatal skip/fail, never a throw that escapes the job.
- [ ] 27.15 DI: `IRestoreSelfTest` → `RestoreSelfTestService` in `AddCofferBackup`.
- [ ] 27.16 `App.axaml.cs` `StartMonthlyRestoreTest()` in `BuildMainWindow` (fire-and-forget `Task.Run`, start-up delay, run only if `LastRunMonth != this month`, log-and-swallow on error).
- [ ] 27.17 Settings: `LastSelfTestRestoreText` (date + ✓ passed / ✗ failed) in the Backup & Recovery panel + a **"Test restore"** button → `SettingsViewModel.TestRestoreCommand` → `RunAsync`, reporting via `StatusMessage`.
- [ ] 27.18 Localization: `Settings.SelfTest.*` (label, passed/failed, button, running) in both `.resx` (parity).
- [ ] 27.19 Tests (`Coffer.Infrastructure.Tests/Backup`): a healthy snapshot passes and the marker records the month; a truncated/corrupt snapshot fails (`Passed == false`, error recorded, live DB untouched); no snapshot → non-fatal skip; DEK unavailable → non-fatal; the marker round-trips. (`Coffer.Application.Tests`) the VM surfaces status + the on-demand command reports pass/fail.

### 27-D — regenerate recovery seed

- [ ] 27.20 `IMasterPasswordService.VerifyMasterPasswordAsync(password, ct)` + impl (derive key, decrypt the password blob; `true`/`false`, no re-wrap, nothing written) + tests.
- [ ] 27.21 `RegenerateRecoverySeedViewModel` (`Coffer.Application/ViewModels/Recovery/`): a password step (verify via `VerifyMasterPasswordAsync`) → generate a fresh mnemonic → reuse `BipSeedDisplayStepViewModel` + `BipSeedVerificationStepViewModel` → `EnableSeedRecoveryAsync(newMnemonic)`; `Completed`/`CancelRequested`; distinct wrong-password vs verification errors; clears the mnemonic on completion.
- [ ] 27.22 `RegenerateRecoverySeedWindow` (`Coffer.Desktop`) + `IRegenerateRecoverySeedDialog` seam + `RegenerateRecoverySeedDialogService` (screen-capture-blocked on `Opened`, mirroring `EnableSeedRecoveryDialogService`).
- [ ] 27.23 Settings: when `SeedRecoveryEnabled`, the seed-recovery area shows a **"Regenerate recovery seed"** action (replacing the static "enabled ✓" text) → `SettingsViewModel.RegenerateRecoverySeedCommand`; the "Enable" path (v1) is unchanged.
- [ ] 27.24 Localization: `Settings.SeedRecovery.Regenerate*` + `Regenerate.*` (password prompt, warning that the old paper seed stops working, wrong-password error) in both `.resx` (parity).
- [ ] 27.25 DI: the VM / window / dialog registered in `AddCofferDesktopUi`.
- [ ] 27.26 Tests (`Coffer.Application.Tests`): the VM blocks on a wrong current password (nothing generated), requires a correct seed verification before enabling, calls `EnableSeedRecoveryAsync` + raises `Completed` + clears the mnemonic; `SettingsViewModel` shows "Regenerate" only when enabled and reports success. Fakes extend `FakeMasterPasswordService` (verify) + a `FakeRegenerateRecoverySeedDialog`.

### Sweep

- [ ] 27.27 No residual hardcoded user-facing literals; resx parity green; `dotnet format --verify-no-changes` clean (only pre-existing CRLF/whitespace noise). Security self-check (docs 08/09): the archive importer writes only encrypted bytes to known paths; the self-test never mutates live data and needs the in-memory DEK; the new mnemonic never touches disk/logs; no plaintext key on disk.
- [ ] 27.28 Manual DoD click-through (below) — deferred to manual (needs a running app + a real vault + a real archive + a real seed).

## Definition of Done

- **27-A (automated):** round-trip export→import into an empty vault yields byte-identical `coffer.db`/`dek.encrypted` and a successful `LoginService` unlock; missing DB/DEK/manifest → `InvalidArchiveException`; import over an existing vault → `VaultAlreadyExistsException` with files untouched; only known entry names are extracted.
- **27-B (automated):** the Welcome VM raises `RestoreFromArchiveRequested` and the wizard bubbles it.
- **27-C (automated):** a healthy snapshot passes and marks the month; a corrupt snapshot fails without touching the live DB; no-snapshot/DEK-unavailable are non-fatal; the VM surfaces status and the on-demand test reports pass/fail.
- **27-D (automated):** the regenerate VM blocks on a wrong current password, requires seed verification, calls `EnableSeedRecoveryAsync`, raises `Completed`, and clears the mnemonic; Settings shows "Regenerate" only on a v2 vault.
- **Manual (the drill):** on a second machine (or a clean profile) with only the exported `.zip`, start the app → Welcome → "Restore from a backup archive" → pick the zip → log in with the master password (and separately: with the seed via "Restore from seed"); Settings shows a passing self-test after a forced run; "Regenerate recovery seed" (password-gated) shows fresh words and the *new* seed restores while the old one no longer does.
- **Whole-sprint:** doc-08's recovery story is complete — a dead disk is survivable end-to-end from a portable archive, backups are self-verified monthly, and the recovery seed can be rotated. **Doc-08 closed to 100%.**

## Files affected

- `src/Coffer.Core/Security/IVaultPaths.cs` + `src/Coffer.Infrastructure/Security/CofferVaultPaths.cs`; `src/Coffer.Infrastructure/Backup/BackupService.cs` + `RestoreService.cs` (repoint at `BackupsFolder`)
- `src/Coffer.Core/Backup/IArchiveImporter.cs` + `ArchiveImportResult` + `InvalidArchiveException.cs` + `VaultAlreadyExistsException.cs` (new); `src/Coffer.Infrastructure/Backup/ArchiveImporter.cs` (new)
- `src/Coffer.Core/Import/IFilePicker.cs` + the Avalonia file-picker impl (add `PickOpenArchiveFileAsync`)
- `src/Coffer.Core/Backup/IRestoreSelfTest.cs` + `RestoreSelfTestResult`/`RestoreSelfTestStatus` (new); `src/Coffer.Infrastructure/Backup/RestoreSelfTestService.cs` (new)
- `src/Coffer.Core/Security/IMasterPasswordService.cs` + `src/Coffer.Infrastructure/Security/MasterPasswordService.cs` (add `VerifyMasterPasswordAsync`)
- `src/Coffer.Application/ViewModels/Setup/WelcomeStepViewModel.cs` + `SetupWizardViewModel.cs`; `src/Coffer.Application/ViewModels/Recovery/RegenerateRecoverySeedViewModel.cs` (new); `src/Coffer.Application/Dialogs/IRegenerateRecoverySeedDialog.cs` (new); `ViewModels/Settings/SettingsViewModel.cs`
- `src/Coffer.Desktop/App.axaml.cs` (restore-from-archive handler + `StartMonthlyRestoreTest`); `Views/Setup/*` (Welcome restore action); `Views/RegenerateRecoverySeedWindow.axaml(.cs)` (new); `Platform/RegenerateRecoverySeedDialogService.cs` (new); `Views/SettingsView.axaml`; `DependencyInjection/DesktopServiceRegistration.cs`
- `src/Coffer.Infrastructure/DependencyInjection/ServiceRegistration.cs`
- `src/Coffer.Application/Localization/Strings.resx` + `Strings.pl.resx` (new keys, parity)
- `tests/Coffer.Infrastructure.Tests/Backup/**`, `tests/Coffer.Application.Tests/**`

## Open questions

Recorded as decisions in `log.md` once settled:

- **Restore-from-archive entry: Welcome-step affordance vs a dedicated pre-wizard chooser?** → proposed a **Welcome-step secondary action** (fewer new windows; matches the doc-08 "wizard offers choices" mock). Confirm vs a standalone chooser window.
- **Self-test integrity depth: `PRAGMA integrity_check` + per-table `COUNT` vs a full `SELECT *` scan?** → proposed **integrity_check + counts** (doc-08's wording; cheap, catches corruption). Confirm.
- **Regenerate: require the current password (vs relying on the unlocked session)?** → proposed **require it** (doc-08 says password-gated; defends a walk-up at an unlocked session). Confirm.
- **Does regenerating force a re-login / DPAPI refresh?** → proposed **no** — only the seed blob changes; the password key and DEK are untouched, so the session and cache stay valid.

## Deferred to a follow-up (kept out of scope)

- **Layer-2 Google Drive as a backup/restore source** — needs the not-started Phase-3 sync spine (OAuth `drive.file`, resumable upload). Archive `.zip` covers the portable-recovery need for now.
- **Restore *over* an existing vault from an archive** — the guarded same-vault path is Sprint 24's snapshot restore; cross-vault overwrite-in-place is a larger, riskier flow with no current need.
- **Auto-test cadence configuration / self-test of the monthly Drive backup** — the monthly local self-test is the doc-08 requirement; making the cadence user-tunable is a nice-to-have.
- **A rotation reminder / seed-age indicator** — nice-to-have, not needed for the core regenerate action.
