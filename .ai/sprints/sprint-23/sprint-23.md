# Sprint 23 — Local backup: daily snapshots, status, archive export

**Phase:** — (roadmap-adjacent; `docs/architecture/08-backup-and-recovery.md`, Layer 1 "daily local snapshot" + Layer 3 "manual archive export" + the Settings "Backup & Recovery" panel. Layer 2 Google Drive and the whole restore side are explicitly out of scope — see Deferred.)
**Status:** In progress
**Depends on:** sprint-4 (`MigrationRunner` + `IPreMigrationBackup` + `SqlCipherKeyInterceptor`), sprint-2/3 (`IVaultPaths`, `dek.encrypted`, `IDekHolder`), sprint-9 (`IFilePicker`), sprint-15 (i18n). No new schema.

## Goal

Coffer keeps an automatic, rolling local safety net of the encrypted database so a disk failure, an accidental delete, or a corrupt write does not lose financial history. Concretely: a **daily local snapshot** of `coffer.db` (30-day rolling retention), a **Settings "Backup & Recovery" panel** showing backup status with a "Backup now" action, and a **manual "Export full archive"** that writes a portable `.zip` (encrypted DB + encrypted DEK + a small manifest) the owner can stash on a USB drive. All deterministic; no AI, no schema change, no network.

## Why this sprint exists

Rule #8 already forces a **pre-migration** snapshot, and `PreMigrationBackup` does a real file copy — but that is the *only* backup in the app. There is no daily snapshot, no retention/rotation, no user-visible backup status, and no way to export a portable archive. All the owner's data sits in a single `%LocalAppData%\Coffer\coffer.db`; one SSD failure loses everything. This is the largest single data-loss risk in the app and the cheapest to close: the file is already SQLCipher-encrypted, so a backup is a copy — no new crypto, no new schema, no network. It reuses the exact path abstraction (`IVaultPaths`) and copy technique the pre-migration backup already uses.

## Design decisions (the shape we commit to)

- **Backup only this sprint; restore is a separate sprint.** Creating the safety net (snapshots + export) is self-contained and low-risk. Guided *restore* (restore-from-snapshot, restore-from-BIP39-seed) touches the setup/login flow and key re-encryption — riskier and larger, so it is its own sprint. Until then a snapshot is still recoverable by hand (copy the dated `.db` back over `coffer.db` while logged out), and the archive `.zip` is the offline disaster-recovery package. This is an explicit, stated limitation, surfaced in the Settings panel text.
- **A snapshot is a file copy of the already-encrypted DB + its WAL side-files.** No re-encryption, no `VACUUM INTO`. Copy `coffer.db` **together with** `coffer.db-wal` / `coffer.db-shm` so the set is internally consistent even without a forced checkpoint — the same technique `PreMigrationBackup` uses. A shared `BackupSnapshotWriter` (Infrastructure) owns the copy so the daily service and the pre-migration hook share one tested code path; `PreMigrationBackup` is refactored to call it with its behaviour unchanged (its byte-for-byte tests guard that).
- **Atomic write, daily naming, rolling retention.** The daily snapshot writes to `backups/coffer-{YYYY-MM-DD}.db.tmp` and only `File.Move`s to the final `backups/coffer-{YYYY-MM-DD}.db` on full success (an interrupted copy never leaves a half file mistaken for a good one). Retention is computed by a **pure** `BackupRetention` (given the existing filenames + today + a keep-days window → the set to delete), run at the start of each daily pass: **30 days for daily**, **90 days for pre-migration** (the pre-migration folder gains the rotation it lacks today). Retention is by date parsed from the filename, so it needs no DB and works before login.
- **Idempotent within a day; disk-derived status.** `CreateDailySnapshotAsync` skips if today's file already exists (like `GoalSnapshotJob` is idempotent within a day). "Last snapshot" and the rest of the status are read **from the filenames on disk**, not the DB — so the panel and the job work before the DEK is available and never depend on the encrypted store.
- **Triggered as a delayed startup task, not a hosted service.** The app has no `IHostedService`; the established pattern is a fire-and-forget `Task.Run` dispatched at startup (`StartDailyAdvisorRefresh`). The daily snapshot runs the same way — a short delay after the main window opens, failures logged and swallowed (a backup miss must never crash or block the app).
- **The archive is a plain `.zip` of already-encrypted files.** Layer 3 = `coffer.db` + `dek.encrypted` + a small `manifest.json` (app version + UTC timestamp), zipped via `System.IO.Compression`. No double-encryption (doc 08). The owner picks the destination through a **new save-file method on `IFilePicker`** (today it is open-only); the Avalonia impl uses `StorageProvider.SaveFilePickerAsync`.
- **Direct `System.IO` behind `IVaultPaths`.** There is no `IFileSystem` wrapper and the codebase does not use one; hard rule #4 is satisfied at the path level (`IVaultPaths`) and the dialog level (`IFilePicker`). The new services inject `IVaultPaths` + `ILogger` and do raw file I/O under the folder it hands them, exactly like `WindowsDpapiSecretStore` and `PreMigrationBackup`. Tests swap in `TestVaultPaths` (temp dir).

## Approach — headless service first, then UI + platform glue

- **23-A — backup service + archive exporter (headless).** `IBackupService` (daily snapshot, "backup now", status) + the pure `BackupRetention` + the shared `BackupSnapshotWriter` (with `PreMigrationBackup` refactored onto it) + `IArchiveExporter` (zip to a given path). New `AddCofferBackup` DI. Tests over `TestVaultPaths` + a real DB fixture. No pixels, no pickers.
- **23-B — Settings panel + export picker + scheduler.** A save-file method added to `IFilePicker` (+ Avalonia impl); a "Backup & Recovery" section in `SettingsViewModel`/`SettingsView` (status text + "Backup now" + "Export full archive" via the picker); the delayed startup task wiring the daily snapshot. Fully localized (keys in both `.resx`, parity green). VM tests over fakes.

## Steps

### 23-A — backup service + archive exporter (headless)

- [x] 23.1 `Coffer.Core/Backup/` contracts + records: `IBackupService` (`CreateDailySnapshotAsync`, `CreateSnapshotNowAsync`, `GetStatusAsync`), `IArchiveExporter` (`ExportAsync(string targetZipPath, CancellationToken)`), and records `BackupStatus(DateOnly? LastDailySnapshot, int DailyCount, DateTime? LastPreMigrationSnapshot)`, `BackupResult(bool Created, string? Path)`.
- [x] 23.2 Pure `BackupRetention` (`Coffer.Core/Backup/`): given a list of snapshot filenames, `today`, and a keep-days window → the filenames to delete. Parses the date from `coffer-{yyyy-MM-dd}.db`; ignores unparsable names. Unit-tested at the boundary (exactly-N-days, older, malformed).
- [x] 23.3 `BackupSnapshotWriter` (`Coffer.Infrastructure/Backup/` or `/Persistence/`): copies `coffer.db` + `-wal`/`-shm` to a destination path (the shared copy technique). Refactor `PreMigrationBackup` to use it — behaviour unchanged (its byte-for-byte + side-file tests must stay green).
- [x] 23.4 `BackupService` (`Coffer.Infrastructure/Backup/`, `IVaultPaths` + `ILogger`): `CreateDailySnapshotAsync` — skip if `backups/coffer-{today}.db` exists; else write `.tmp` via the writer then atomic `File.Move`; then rotate daily (30d) and pre-migration (90d) via `BackupRetention`. `CreateSnapshotNowAsync` — force-refresh today's snapshot. `GetStatusAsync` — read the folder, return last daily date/count + last pre-migration timestamp.
- [x] 23.5 `ArchiveExporter` (`Coffer.Infrastructure/Backup/`, `IVaultPaths`): zip `DatabaseFile` + `EncryptedDekFilePath` + a generated `manifest.json` (app version + UTC timestamp) to the target path via `System.IO.Compression.ZipArchive`. Skips missing side inputs gracefully; the DB is mandatory.
- [x] 23.6 DI: a new `AddCofferBackup` (`IBackupService`/`IArchiveExporter` singletons or transients; the shared writer) chained into `AddCofferInfrastructure` after `AddCofferDatabase`.
- [x] 23.7 Tests (`Coffer.Core.Tests` + `Coffer.Infrastructure.Tests`): `BackupRetention` boundaries; `BackupService` creates a dated snapshot from a seeded DB file, is idempotent within the day, rotates >30-day daily and >90-day pre-migration files, reports status from disk; `ArchiveExporter` produces a zip containing `coffer.db` + `dek.encrypted` + `manifest.json`; `PreMigrationBackupTests` stay green after the refactor. `TestVaultPaths` temp dirs.

### 23-B — Settings panel + export picker + scheduler

- [ ] 23.8 Extend `IFilePicker` with `PickSaveArchiveFileAsync(string suggestedName, CancellationToken)` (returns a target path or null) + Avalonia impl via `StorageProvider.SaveFilePickerAsync`; a fake in tests.
- [ ] 23.9 A "Backup & Recovery" section in `SettingsViewModel`: `LastDailySnapshotText`/`DailyCountText`/`LastPreMigrationText` (from `GetStatusAsync`), `BackupNowCommand` (calls `CreateSnapshotNowAsync`, refreshes status), `ExportArchiveCommand` (picks a save path via `IFilePicker`, calls `IArchiveExporter.ExportAsync`, reports via the shared `StatusMessage`). Injects `IBackupService` + `IArchiveExporter` + `IFilePicker`.
- [ ] 23.10 `SettingsView.axaml`: a "Backup & Recovery" card mirroring the existing card pattern — status lines, a "Backup now" button, an "Export full archive" button, and a one-line note that guided restore is coming (manual copy meanwhile).
- [ ] 23.11 Startup scheduler: a delayed `Task.Run` (mirroring `StartDailyAdvisorRefresh`) dispatched after the main window opens, calling `IBackupService.CreateDailySnapshotAsync`; failures logged and swallowed.
- [ ] 23.12 Localization: every label via `{l:Localize}`, keys in **both** `.resx` (`Settings.Backup.*`), parity test green.
- [ ] 23.13 Tests (`Coffer.Application.Tests`): the settings VM surfaces status, "Backup now" calls the service and refreshes, "Export" picks a path and calls the exporter (and no-ops on a cancelled picker); resource-key parity.

### Sweep

- [ ] 23.14 No residual hardcoded user-facing literals; `dotnet format --verify-no-changes` clean.
- [ ] 23.15 Manual DoD click-through (below) — expected to defer to manual (needs a running desktop app with a real DB).

## Definition of Done

- **23-A (automated):** `BackupService` writes `backups/coffer-{today}.db` from a real DB file, is idempotent within the day, and rotates daily (>30d) / pre-migration (>90d) files via the pure `BackupRetention`; `GetStatusAsync` reports the last daily date and count from disk; `ArchiveExporter` produces a `.zip` with `coffer.db` + `dek.encrypted` + `manifest.json`; the refactored `PreMigrationBackup` still copies byte-for-byte with side-files.
- **23-B (automated):** the settings VM shows backup status, "Backup now" creates/refreshes today's snapshot, "Export full archive" writes to the picked path (and cancels cleanly); `IFilePicker` gained a working save method; resource-key parity holds.
- **Manual:** run the app, open Settings → Backup & Recovery, see today's snapshot listed after startup; click "Backup now" and see the status update; "Export full archive", pick a location, and confirm a `.zip` with the three entries; check `%LocalAppData%\Coffer\backups\` holds a dated `.db`.
- **Whole-sprint:** the encrypted database is automatically snapshotted daily with rolling retention, the owner can see backup status and force a backup or export a portable archive, and the pre-migration folder is now also rotated — closing the single-file data-loss risk. Restore stays a documented next step.

## Files affected

- `src/Coffer.Core/Backup/` — `IBackupService.cs`, `IArchiveExporter.cs`, `BackupStatus.cs`/`BackupResult.cs`, `BackupRetention.cs` (new)
- `src/Coffer.Infrastructure/Backup/` — `BackupService.cs`, `ArchiveExporter.cs`, `BackupSnapshotWriter.cs` (new) + `Persistence/PreMigrationBackup.cs` (refactor onto the writer) + `DependencyInjection/ServiceRegistration.cs` (`AddCofferBackup`)
- `src/Coffer.Core/Import/IFilePicker.cs` (+ `src/Coffer.Desktop/Platform/AvaloniaFilePicker.cs`) — add the save method
- `src/Coffer.Application/ViewModels/Settings/SettingsViewModel.cs` + `src/Coffer.Desktop/Views/SettingsView.axaml` — the panel
- `src/Coffer.Desktop/App.axaml.cs` — the delayed daily-snapshot task
- `src/Coffer.Application/Localization/Strings.resx` + `Strings.pl.resx` (new keys, parity)
- `tests/Coffer.Core.Tests/Backup/**`, `tests/Coffer.Infrastructure.Tests/Backup/**`, `tests/Coffer.Application.Tests/ViewModels/Settings/**`

## Open questions

To confirm with the owner before/while building (recorded as decisions in `log.md` once settled):

- **Scope: backup-only this sprint, restore next?** → proposed **yes** — ship the safety net (snapshots + export) now; guided restore-from-snapshot and restore-from-seed are their own sprint. Confirm vs squeezing a minimal same-machine "restore from snapshot" in here too.
- **"Backup now" semantics** → proposed **create/refresh today's local snapshot immediately** (Drive is deferred, so there is no upload to trigger). Confirm.
- **Snapshot trigger delay** → proposed a short delay after the window opens (doc 08 says ~5 min). Confirm the delay is fine or prefer "immediately on login".

## Deferred to a follow-up (kept out of scope)

- **All restore flows** — restore-from-snapshot, restore-from-BIP39-seed, the setup-wizard "Restore" branch, DEK re-encryption on a new master password (doc 08 "Restore flow" / "Restore from seed"). The next sprint.
- **Layer 2 — Google Drive monthly backup** — depends on the Phase-3 OAuth/sync spine (not started); `drive.file` upload, resumable uploads, folder structure.
- **Auto-test restore** (monthly silent self-test) — depends on being able to open a snapshot with the in-memory DEK; slots in with the restore sprint.
- **Mobile backups** (MAUI) — mobile is deferred project-wide.
- **A "Show recovery seed" action** in the panel — security-gated seed display is a separate, careful piece.
