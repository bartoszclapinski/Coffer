# Sprint 26 — Change master password

**Phase:** — (roadmap-adjacent; `docs/architecture/09-security-key-management.md` — "change master password → re-encrypt DEK only, not the entire DB". The natural sibling of Sprint 25's seed recovery.)
**Status:** Closed
**Depends on:** sprint-25 (`DekFile` v2 + `DekFile.WriteReplaceAsync` + the dual-wrap model), sprint-2/3 (`AesGcmCrypto`, `Argon2KeyDerivation`, `IKeyVault`, `IDekHolder`, `IVaultPaths`), sprint-5/6 (login, `IPasswordStrengthChecker`), sprint-15 (i18n). No new database schema.

## Goal

The owner can change their master password from Settings without losing anything. Concretely: a **"Change master password"** action takes the current password (to prove it's really them), a new password (validated with the same strength rules as setup), re-wraps the **password-wrapped** copy of the DEK under a fresh Argon2 key, and refreshes the DPAPI cache — leaving the database, the DEK itself, and the seed-wrapped copy untouched. Deterministic crypto; no AI, no network, no schema change.

## Why this sprint exists

Doc 09's decoupled-DEK design exists precisely so re-keying is cheap: "change master password → re-encrypt DEK only, not the entire DB." Sprint 25 built the machinery (`DekFile` v2, `WriteReplaceAsync`, re-wrap-under-a-new-password in `RecoverWithSeedAsync`) but only for the forgot-password path. There is still **no way to deliberately rotate the master password** — a routine security hygiene action (suspected shoulder-surf, periodic rotation, a password that turned out weak). It is a small, high-value, self-contained feature that closes the password-lifecycle: set (setup) → use (login) → recover (seed) → **rotate (this sprint)**. It reuses the exact re-wrap technique already shipped and tested.

## Design decisions (the shape we commit to)

- **Change = re-wrap the password blob only.** The DEK is unchanged, so the database is untouched and the seed-wrapped copy is carried over verbatim. Only the password blob (`salt` + Argon2 params + `{iv, tag, ciphertext}`) is replaced with a fresh Argon2 salt + re-encryption under the new master key. The file's **version is preserved** — a v1 (password-only) vault stays v1, a v2 (dual-wrap) vault stays v2 (its seed blob passes through), so the change works regardless of whether seed recovery is enabled.
- **The current password is required — "logged in" is not enough.** Rotation verifies the current password by deriving its master key and AES-GCM-decrypting the password blob (the same check as login). This defends against a walk-up attacker at an unlocked/auto-unlocked session silently locking the owner out, and it yields the DEK to re-wrap without touching the holder. A wrong current password throws `InvalidMasterPasswordException` and changes nothing.
- **The DPAPI cache is refreshed to the new key, not just invalidated.** The old cache holds the old master key, which can no longer unlock the rewritten password blob. Because the owner just re-authenticated, we proactively cache the **new** master key (7-day TTL, as login does) so the next cold start still unlocks silently — better UX than forcing a password prompt, equivalent security. The in-memory DEK holder is left as-is (the DEK never changed).
- **Atomic, rollback-safe, nothing sensitive logged.** The rewrite goes through `DekFile.WriteReplaceAsync` (temp → move) so a crash never destroys the only key file; every key buffer (old master key, new master key, DEK, wrap buffers) is zeroed in `finally`; only the outcome is logged — never the old or new password, or any key (hard rule #6). New-password validation reuses the setup strength rules (len ≥ 12, ≥ 3 char classes, zxcvbn score ≥ 3, ≠ current password).
- **A dedicated service, mirroring `SeedRecoveryService`.** `IMasterPasswordService.ChangeMasterPasswordAsync(current, new)` (Core/Infrastructure) keeps the password-rotation crypto out of `LoginService` (login/logout) and `SeedRecoveryService` (seed channel), each staying single-purpose. Injects `IMasterKeyDerivation`, `IKeyVault`, `IVaultPaths`, `ILogger` — no `ISeedManager`, no `IDekHolder` needed.

## Approach — headless service first, then the Settings UI

- **26-A — master-password change service (headless).** `IMasterPasswordService` / `MasterPasswordService` + DI + exhaustive tests (right current password rotates and the new one logs in while the old fails; wrong current password throws and changes nothing; the seed blob + version survive a v2 rotation; a v1 vault stays v1; the DPAPI cache holds the new key). No pixels.
- **26-B — Settings "Change master password" dialog.** A `ChangeMasterPasswordViewModel` (current + new + confirm, strength-gated) behind a `ChangeMasterPasswordWindow` + an `IChangeMasterPasswordDialog` seam (mirrors `IEnableSeedRecoveryDialog`); a "Master password" action in the Settings security area; distinct localized errors for wrong current password vs weak/mismatched new password. Fully localized.

## Steps

### 26-A — master-password change service (headless)

- [x] 26.1 `IMasterPasswordService` (`Coffer.Core/Security/`): `ChangeMasterPasswordAsync(currentPassword, newPassword, ct)`, documenting `InvalidMasterPasswordException` / `VaultMissingException` / `VaultCorruptedException`.
- [x] 26.2 `MasterPasswordService` (`Coffer.Infrastructure/Security/`, `IMasterKeyDerivation` + `IKeyVault` + `IVaultPaths` + `ILogger`): reads `DekFile` (missing → `VaultMissingException`; malformed → `VaultCorruptedException`); derives the old key + decrypts the password blob (verifies current password + yields the DEK; `CryptographicException` → `InvalidMasterPasswordException`); derives a new key (fresh salt, defaults); re-wraps the DEK; rewrites preserving `file.Version` + seed blob via `DekFile.WriteReplaceAsync`; refreshes the DPAPI cache. All key buffers zeroed in `finally`.
- [x] 26.3 DI: `IMasterPasswordService` → `MasterPasswordService` registered in `AddCofferLogin` (transient).
- [x] 26.4 Tests (`Coffer.Infrastructure.Tests/Security`, 6): correct current password rotates (new logs in via `LoginService`, old throws `InvalidMasterPasswordException`); wrong current password throws + leaves the file **byte-identical**; a v2 rotation preserves the seed blob (the seed still recovers the DEK) and stays v2; a v1 vault stays v1 (new password logs in); no vault → `VaultMissingException`; the refreshed cache lets `TryLoginFromCachedKeyAsync` unlock with the new key. `TestVaultPaths` + inline DEK-file helpers.

### 26-B — Settings "Change master password" dialog

- [x] 26.5 `ChangeMasterPasswordViewModel` (`Coffer.Application`): `CurrentPassword`/`NewPassword`/`ConfirmPassword`, `ChangeCommand` gated on the strength rules (len ≥ 12, ≥ 3 classes, score ≥ 3, match, ≠ current) calling `ChangeMasterPasswordAsync`, `Completed` + `CancelRequested` events, distinct localized errors for `InvalidMasterPasswordException` vs weak/mismatched new password. Clears fields on success (and the current field on a wrong-password error, for a retry).
- [x] 26.6 `ChangeMasterPasswordWindow` (`Coffer.Desktop`) + an `IChangeMasterPasswordDialog` seam (`ChangeMasterPasswordDialogService` shows it modally over the main window, returns whether changed); no screen-capture blocker (no seed shown); close blocked while busy.
- [x] 26.7 Settings wiring: a "Master password" section in `SettingsView.axaml` with a **"Change master password"** button; `SettingsViewModel.ChangeMasterPasswordCommand` opens the dialog and reports `Settings.Password.Changed` via `StatusMessage`. Injects `IChangeMasterPasswordDialog`.
- [x] 26.8 Localization + DI: `Settings.Password.*` / `ChangePassword.*` keys in both `.resx` (parity green); the VM/window/dialog registered in `AddCofferDesktopUi`.
- [x] 26.9 Tests (`Coffer.Application.Tests`, +8): the VM cannot execute with a mismatched / weak / same-as-current new password, calls the service + raises `Completed` + clears sensitive, maps `InvalidMasterPasswordException` to the wrong-current message; `SettingsViewModel` opens the dialog and reports/omits success. Fakes `FakeMasterPasswordService` + `FakeChangeMasterPasswordDialog`. Parity green.

### Sweep

- [x] 26.10 No residual hardcoded user-facing literals; `dotnet format --verify-no-changes` clean (only pre-existing repo-wide CRLF/whitespace noise). Security self-check (doc 09): no new plaintext key on disk, no sensitive logging, Argon2 params unchanged.
- [ ] 26.11 Manual DoD click-through (below) — deferred to manual (needs a running app + a real vault).

## Definition of Done

- **26-A (automated):** `ChangeMasterPasswordAsync` rotates so the new password logs in and the old one fails; a wrong current password throws `InvalidMasterPasswordException` and changes nothing; a v2 vault keeps its seed blob (the seed still recovers) and stays v2; a v1 vault stays v1; the DPAPI cache holds the new key; no key/password logged.
- **26-B (automated):** the change VM validates the new password, calls the service, raises `Completed`, and maps the wrong-current-password case to a distinct message; `SettingsViewModel` opens the dialog and reports success; parity holds.
- **Manual:** log in, Settings → Change master password, enter the current + a new password, confirm; log out and back in with the **new** password (the old one is rejected); if seed recovery was enabled, the seed still recovers.
- **Whole-sprint:** the master password can be rotated at will, re-encrypting only the DEK file (database, DEK, and seed copy untouched) — closing the password lifecycle (set → use → recover → rotate).

## Files affected

- `src/Coffer.Core/Security/IMasterPasswordService.cs` (new) + `src/Coffer.Infrastructure/Security/MasterPasswordService.cs` (new) + `DependencyInjection/ServiceRegistration.cs`
- `src/Coffer.Application/ViewModels/Security/ChangeMasterPasswordViewModel.cs` (new) + `src/Coffer.Application/Dialogs/IChangeMasterPasswordDialog.cs` (new) + `ViewModels/Settings/SettingsViewModel.cs`
- `src/Coffer.Desktop/Views/ChangeMasterPasswordWindow.axaml(.cs)` (new) + `Platform/ChangeMasterPasswordDialogService.cs` (new) + `Views/SettingsView.axaml` + `DependencyInjection/DesktopServiceRegistration.cs`
- `src/Coffer.Application/Localization/Strings.resx` + `Strings.pl.resx` (new keys, parity)
- `tests/Coffer.Infrastructure.Tests/Security/**`, `tests/Coffer.Application.Tests/**`

## Open questions

To confirm with the owner before/while building (recorded as decisions in `log.md` once settled):

- **Require the current password?** → proposed **yes** — verifying it defends an unlocked session and yields the DEK cleanly. Confirm vs relying on the logged-in session alone.
- **Refresh the DPAPI cache to the new key vs invalidate it?** → proposed **refresh** (next cold start stays silent; the owner just re-authenticated). Confirm vs forcing a password prompt next start.
- **Dialog vs inline in Settings?** → proposed a **modal dialog** (mirrors enable-seed-recovery; focused sensitive fields). Confirm.

## Deferred to a follow-up (kept out of scope)

- **Forcing seed re-enrolment on password change** — unrelated; the seed channel is independent by design.
- **Password-strength localization of zxcvbn warnings** — a pre-existing separate chore.
- **A "last changed" timestamp / rotation reminder** — nice-to-have, not needed for the core action.
