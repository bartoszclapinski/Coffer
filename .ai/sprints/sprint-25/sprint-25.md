# Sprint 25 — Restore from a BIP39 seed (DEK dual-wrap)

**Phase:** — (roadmap-adjacent; `docs/architecture/08-backup-and-recovery.md` "Restore from BIP39 seed" + `09-security-key-management.md` three-layer key hierarchy. Closes the forgot-password recovery channel and the DEK-format gap that blocked it.)
**Status:** Planned
**Depends on:** sprint-2/3 (`DekFile`, `AesGcmCrypto`, `Argon2KeyDerivation`, `Bip39SeedManager`, `IDekHolder`, `IVaultPaths`), sprint-5/6 (setup wizard, `LoginService`/`ILoginService`, `App` startup routing), sprint-24 (restore mental model), sprint-15 (i18n). No new database schema (the change is to the `dek.encrypted` file format, not EF).

## Goal

If the owner forgets their master password, they can recover the vault with their 12-word BIP39 seed instead of losing everything. Concretely: the `dek.encrypted` file gains a **second, seed-wrapped copy of the DEK** (dual-wrap), so the DEK can be unlocked by *either* the Argon2-derived master key *or* the PBKDF2-derived recovery key (doc 09's three-layer hierarchy, finally implemented). A **"Restore from seed"** entry on the login window takes the 12 words, decrypts the DEK, asks for a **new master password**, re-wraps, and logs in. Because the seed channel only works if the seed blob exists, setup now writes it for new vaults, and a Settings **"Enable seed recovery"** action belatedly adds it to the existing (v1) vault. Deterministic crypto; no AI, no network, no EF schema change.

## Why this sprint exists

Doc 09's whole premise is a **decoupled DEK**: the database key is wrapped by two independent channels (master password *and* BIP39 seed) so a forgotten password is recoverable and a re-key is cheap. Sprint 3 built every primitive — `Argon2KeyDerivation`, `Bip39SeedManager.DeriveRecoveryKeyAsync`, `AesGcmCrypto`, `DekFile` — but Sprint 5 wired only the password channel: `SetupService.CompleteSetupAsync(masterPassword, mnemonic, ct)` **receives the mnemonic and never uses it**, and `DekFile` (v1) stores a single password-wrapped blob. So today a forgotten master password means **permanent, total data loss** — the exact disaster doc 08's seed-recovery flow exists to prevent, and the single biggest gap left in the security model. Sprint 24 shipped snapshot restore (same-vault, no keys); this sprint closes the other, harder branch: cross-credential recovery. It touches key material, so it is scoped tightly, kept behind full rollback, and never logs/سends a seed (hard rule #6).

## Design decisions (the shape we commit to)

- **DEK dual-wrap = `DekFile` v2, backward-compatible with v1.** v2 keeps the entire v1 layout (version, Argon2 params, salt, password-wrapped `{iv, tag, ciphertext}`) and appends a **seed-wrapped** `{iv, tag, ciphertext}` of the same 32-byte DEK. The seed blob needs no salt or KDF params in the file: the recovery key is derived purely from `(mnemonic + a fixed app passphrase)`, both known at restore time. `DekFile.ReadAsync` accepts `version is 1 or 2` (an existing v1 vault still logs in); the seed fields are nullable and null for v1. `CurrentVersion` bumps to 2 so every new write is dual-wrapped. **A v1 file is never silently "upgraded" on read** — upgrading requires the DEK in hand (see Enable), so it is an explicit action, not a side effect of login.
- **The seed derivation passphrase is a fixed app constant `"Coffer"`** (doc 09's `DeriveSeed("Coffer")`). It is domain-separation, not a secret: the same 12 words won't derive a standard BIP39 wallet key and vice-versa. Because no vault has a seed blob yet, there is no back-compat constraint on the choice — but once shipped it is frozen (a v2 vault's seed blob can only be opened with the same passphrase). Centralised as one constant used by both wrap (setup/enable) and unwrap (restore).
- **The DEK never derives from the password or the seed — both only *wrap* it.** So restore-from-seed and a password change both re-encrypt the ~60-byte DEK file only, never the database. `RecoverWithSeedAsync` decrypts the DEK via the seed blob, then re-wraps it under a **new** master password (fresh Argon2 salt, current default params) and rewrites the **same** seed blob — the seed keeps working after the password reset.
- **"Enable seed recovery" for the existing vault mints a *new* functional seed; it does not trust the old one.** The v1 setup showed a seed but never wrapped anything with it, so the owner's original paper seed **protects nothing today** — it is cryptographically inert. Rather than silently trust a re-typed inert seed (a typo would make recovery quietly impossible), the Enable flow **generates a fresh mnemonic, displays it (screen-capture-blocked) and verifies it** — exactly what setup does — then wraps the in-holder DEK with it and rewrites the file as v2. This is "belatedly do what v1 setup should have done"; the old shown-but-unused seed is discarded. New (v2) vaults never need this — their setup seed is wrapped from day one and is the real recovery seed.
- **Recovery lives on the login window (existing-vault, forgot-password), not the setup wizard.** The realistic scenario is the owner's own machine with a live `dek.encrypted` + `coffer.db` — which `App` routes to the **login window**, not setup. So "Restore from seed" is a login affordance ("Forgot password?"). The fresh-install / foreign-machine restore (a seed-only vault with no local files, or an archive-restored `dek.encrypted`) stays deferred with archive restore — it needs the not-yet-built archive-import path. A v1 `dek.encrypted` (no seed blob) shows a clear "this vault predates seed recovery — use your master password, then enable it in Settings" message instead of a dead end.
- **Full rollback, memory hygiene, nothing sensitive logged.** Every new path zeroes master key / recovery key / DEK buffers in `finally` (matching `SetupService`/`LoginService`), writes the new `dek.encrypted` atomically with the old one preserved until success, and logs only outcomes — never the seed, password, or any key (hard rule #6). Wrong-seed and wrong-format are distinct typed exceptions so the UI can explain without leaking which failed.

## Approach — headless crypto first, then the two UI surfaces

- **25-A — DEK dual-wrap + seed-recovery service (headless).** `DekFile` v2 (write v2, read v1+v2), `SetupService` wraps with the mnemonic too, and a new `ISeedRecoveryService` / `SeedRecoveryService`: `RecoverWithSeedAsync(mnemonic, newPassword)` (seed→DEK→re-wrap under new password→holder+cache), `EnableSeedRecoveryAsync(mnemonic)` (wrap in-holder DEK→rewrite v2), `IsSeedRecoveryEnabledAsync()` (is the file v2). New typed exceptions + DI. Exhaustive unit tests including the existing official BIP39 vectors. No pixels.
- **25-B — restore-from-seed UI.** A "Forgot password? Restore from seed" affordance on `LoginWindow` opening a restore-from-seed flow (enter 12 words → validate → enter+confirm a new master password with the strength meter → `RecoverWithSeedAsync` → proceed into the app like a normal login). Distinct errors for wrong seed vs a v1 vault. Fully localized.
- **25-C — enable-seed-recovery in Settings.** A "Recovery seed" card: state (enabled/not, from `IsSeedRecoveryEnabledAsync`) + an "Enable seed recovery" action that generates → displays (capture-blocked) → verifies a fresh seed, then `EnableSeedRecoveryAsync`; optionally a password-gated "Show recovery seed" is left deferred. Fully localized.

## Steps

### 25-A — DEK dual-wrap + seed-recovery service (headless)

- [ ] 25.1 `DekFile` v2: add nullable seed-wrap fields (`byte[]? SeedIv, SeedTag, SeedCiphertext`); `WriteAsync` emits `version=2` + the seed blob after the password blob; `ReadAsync` accepts `version is 1 or 2`, reading the seed blob only for v2 (null for v1). `CurrentVersion = 2`. Round-trip + truncation guards kept.
- [ ] 25.2 Seed-derivation constant: a single `SeedDerivationPassphrase = "Coffer"` used by every wrap/unwrap, so setup, enable, and restore agree.
- [ ] 25.3 `SetupService` writes v2: after generating the DEK, also derive the recovery key from the mnemonic (`ISeedManager.DeriveRecoveryKeyAsync`) and `AesGcmCrypto.Encrypt` the DEK with it; write both blobs in the v2 `DekFile`. Zero the recovery key. Rollback unchanged (DEK file is still the last-written success sentinel).
- [ ] 25.4 `ISeedRecoveryService` (`Coffer.Core/Security/`) + `SeedRecoveryService` (`Coffer.Infrastructure/Security/`): `RecoverWithSeedAsync(string mnemonic, string newMasterPassword, ct)`, `EnableSeedRecoveryAsync(string mnemonic, ct)`, `Task<bool> IsSeedRecoveryEnabledAsync(ct)`. Injects `IMasterKeyDerivation`, `ISeedManager`, `IKeyVault`, `IDekHolder`, `IVaultPaths`, `ILogger`.
  - **Recover:** read `DekFile`; if v1 → `SeedRecoveryUnavailableException`; derive recovery key; `AesGcmCrypto.Decrypt` the seed blob (`CryptographicException` → `InvalidRecoverySeedException`); derive a new master key (fresh salt, `Argon2Parameters.Default`); re-wrap the DEK; rewrite v2 (new password blob + same seed blob) atomically; `IDekHolder.Set`; refresh the DPAPI cache. Zero all key buffers.
  - **Enable:** require `IDekHolder.IsAvailable` (logged in) else throw; read the current `DekFile` (keep its password blob + argon params + salt); wrap the in-holder DEK with the seed; rewrite v2 atomically. Idempotent-safe if already v2 (re-wraps the seed blob).
- [ ] 25.5 Typed exceptions (`Coffer.Core/Security/`): `SeedRecoveryUnavailableException` (vault is v1 / no seed blob), `InvalidRecoverySeedException` (seed does not unlock the DEK). Neither carries seed/key detail.
- [ ] 25.6 Atomic DEK rewrite: because `DekFile.WriteAsync` uses `FileMode.CreateNew` (create-or-fail), a rewrite writes to a temp path then replaces `dek.encrypted` (`File.Replace`/move) so a crash mid-write never destroys the only key file. (Introduce a small internal helper; setup's first-write stays `CreateNew`.)
- [ ] 25.7 DI: register `ISeedRecoveryService` → `SeedRecoveryService` in `AddCofferInfrastructure` (near `AddCofferLogin`). Singleton or transient consistent with `LoginService`.
- [ ] 25.8 Tests (`Coffer.Infrastructure.Tests/Security`): `DekFile` v1 reads back (no seed fields) and v2 round-trips; a v2 DEK decrypts via **both** the password blob and the seed blob; `SetupService` now writes v2 and the seed unlocks the DEK; `RecoverWithSeedAsync` — right seed re-wraps under a new password (new password logs in via `LoginService`, old password now fails, the seed still works), wrong seed → `InvalidRecoverySeedException`, v1 vault → `SeedRecoveryUnavailableException`; `EnableSeedRecoveryAsync` upgrades a v1 vault to v2 (the seed then unlocks it) and requires a logged-in DEK; `IsSeedRecoveryEnabledAsync` reflects v1 vs v2. Reuse the official BIP39 vectors from Sprint 3. `TestVaultPaths` temp dirs.

### 25-B — restore-from-seed UI

- [ ] 25.9 `RestoreFromSeedViewModel` (`Coffer.Application`): 12-word entry (validated via `ISeedManager.IsValid`), new-password + confirm with the shared `IPasswordStrengthChecker`, a `RecoverCommand` calling `ISeedRecoveryService.RecoverWithSeedAsync`, a `RecoveryCompleted` event (mirrors `LoginCompleted`), distinct localized errors for invalid seed vs v1 vault vs weak/mismatched password.
- [ ] 25.10 `RestoreFromSeedWindow` (`Coffer.Desktop`) + a "Forgot password? Restore from seed" link on `LoginWindow`; `App` opens the window and, on `RecoveryCompleted`, routes to `BuildPostUnlockWindow` exactly like a normal login. No seed/copy affordances beyond entry; input cleared on close.
- [ ] 25.11 Localization + DI: keys in both `.resx` (parity), `RestoreFromSeedViewModel`/`RestoreFromSeedWindow` registered.
- [ ] 25.12 Tests (`Coffer.Application.Tests`): the VM validates the seed, rejects a weak/mismatched password, calls the service and raises `RecoveryCompleted`, and surfaces the right message for `InvalidRecoverySeedException` / `SeedRecoveryUnavailableException` (fake `ISeedRecoveryService`); resource-key parity.

### 25-C — enable-seed-recovery in Settings

- [ ] 25.13 Settings "Recovery seed" card: `SettingsViewModel` gains `SeedRecoveryEnabled` (from `IsSeedRecoveryEnabledAsync` in `LoadAsync`) and an `EnableSeedRecoveryCommand` that runs a generate→display→verify seed flow (reusing the setup seed-display/verification VMs + the screen-capture blocker) then calls `EnableSeedRecoveryAsync`; success refreshes the state and reports via `StatusMessage`.
- [ ] 25.14 View + i18n: a card in `SettingsView.axaml` (enabled/disabled state + the enable action), all `{l:Localize}`, keys in both `.resx` (parity). Reuse the seed-display/verification views.
- [ ] 25.15 Tests (`Coffer.Application.Tests`): `SettingsViewModel` reflects enabled/disabled and the enable command calls the service and refreshes; the generated seed is never logged; parity.

### Sweep

- [ ] 25.16 No residual hardcoded user-facing literals; `dotnet format --verify-no-changes` clean. Security self-check (doc 09 checklist): no new plaintext DEK/seed on disk, no new sensitive logging, Argon2 params not weakened.
- [ ] 25.17 Manual DoD click-through (below) — expected to defer to manual (needs a running app + a real vault + the real seed).

## Definition of Done

- **25-A (automated):** `DekFile` reads v1 and round-trips v2; a v2 DEK unlocks via both channels; `SetupService` writes v2; `RecoverWithSeedAsync` resets the password (old fails, new logs in, seed still works), rejects a wrong seed, and refuses a v1 vault; `EnableSeedRecoveryAsync` upgrades v1→v2 and requires a logged-in DEK; no key/seed is logged.
- **25-B (automated):** the restore VM validates the seed + new password, calls the service, raises `RecoveryCompleted`, and maps each failure to a distinct message.
- **25-C (automated):** Settings shows seed-recovery state and enables it through the generate/verify flow.
- **Manual:** on a v2 vault, log out, "Restore from seed", enter the 12 words + a new password, land in the app; the old password no longer works and the new one does; on the (v1) existing vault, log in normally, Settings → enable seed recovery (write down the new seed), log out, and restore with it.
- **Whole-sprint:** a forgotten master password is recoverable from the BIP39 seed; the DEK is genuinely dual-wrapped per doc 09; new vaults get it automatically and the existing vault can opt in. **Total data loss on a forgotten password is no longer possible once seed recovery is enabled.**

## Files affected

- `src/Coffer.Infrastructure/Security/` — `DekFile.cs` (v2), `SetupService.cs` (seed-wrap), `SeedRecoveryService.cs` (new), a small atomic-rewrite helper; `src/Coffer.Core/Security/` — `ISeedRecoveryService.cs`, `SeedRecoveryUnavailableException.cs`, `InvalidRecoverySeedException.cs` (new) + `DependencyInjection/ServiceRegistration.cs`
- `src/Coffer.Application/ViewModels/Recovery/RestoreFromSeedViewModel.cs` (new) + `src/Coffer.Application/ViewModels/Settings/SettingsViewModel.cs`
- `src/Coffer.Desktop/Views/RestoreFromSeedWindow.axaml(.cs)` (new) + `Views/Login/LoginWindow.axaml` (the link) + `Views/SettingsView.axaml` + `App.axaml.cs` + `DependencyInjection/DesktopServiceRegistration.cs`
- `src/Coffer.Application/Localization/Strings.resx` + `Strings.pl.resx` (new keys, parity)
- `tests/Coffer.Infrastructure.Tests/Security/**`, `tests/Coffer.Application.Tests/**`

## Open questions

To confirm with the owner before/while building (recorded as decisions in `log.md` once settled):

- **Enable = mint a NEW seed vs re-enter the existing paper seed?** → proposed **mint a new one** (generate/display/verify), because the v1 setup seed is cryptographically inert and re-typing risks a silent typo. **Consequence to accept: the owner's original paper seed becomes irrelevant; the newly shown seed is the real recovery seed.** Confirm vs a "type your existing seed" flow.
- **Seed derivation passphrase = `"Coffer"`?** → proposed **yes** (doc 09). It is frozen once a v2 vault exists. Confirm vs empty (pure BIP39).
- **Restore entry = login window only this sprint?** → proposed **yes**; the fresh-install / foreign-machine / archive-`dek.encrypted` restore stays deferred (needs archive import). Confirm.

## Deferred to a follow-up (kept out of scope)

- **Fresh-install / foreign-machine restore-from-seed** (seed-only vault, or a `dek.encrypted` unzipped from a Layer-3 archive) via the setup wizard's "Restore" fork — needs the archive-import path (itself deferred from Sprint 24).
- **A password-gated "Show recovery seed"** display in Settings — for a v2 vault this would require re-deriving/holding the mnemonic, which is not stored; realistically it only shows the seed at enable/setup time. A careful separate piece.
- **Master-password change while logged in** (re-wrap the password blob) — a natural sibling of `RecoverWithSeedAsync` but a distinct feature.
- **Migrating the existing vault to v2 automatically** — intentionally manual (Enable), because it requires minting and the owner safely recording a new seed.
- **Argon2 parameter upgrade-on-login** (re-derive with stronger params) — orthogonal, not needed here.
