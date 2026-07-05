# Sprint 25 log

## 2026-07-05

- `--:--` sprint planned — restore from a BIP39 seed (doc 08 "Restore from seed" + doc 09 dual-wrap key hierarchy). Plan written to `sprint-25.md`.
- `--:--` decision: **DEK dual-wrap via `DekFile` v2, backward-compatible with v1** — v2 appends a seed-wrapped `{iv, tag, ciphertext}` of the DEK after the existing password blob; `ReadAsync` accepts `version is 1 or 2` (seed fields null for v1); `CurrentVersion` → 2. A v1 file is never auto-upgraded on read (upgrading needs the DEK in hand — see Enable).
- `--:--` decision: **seed derivation passphrase = fixed `"Coffer"`** (doc 09's `DeriveSeed("Coffer")`) — domain separation, frozen once a v2 vault exists.
- `--:--` decision: **DEK stays wrapped by both channels, derived from neither** — restore-from-seed decrypts the DEK via the seed blob then re-wraps under a *new* master password (fresh Argon2 salt), rewriting the same seed blob; only the ~60-byte DEK file changes, never the database.
- `--:--` decision (key UX): **"Enable seed recovery" mints a NEW functional seed, does not trust the old paper one** — v1 setup showed a seed but wrapped nothing with it, so the owner's original paper seed is cryptographically inert. Enable generates/displays/verifies a fresh mnemonic (like setup) and wraps the in-holder DEK with it. Consequence: the original paper seed becomes irrelevant; the newly shown seed is the real recovery seed. Flagged as the top open question for the owner.
- `--:--` decision: **recovery entry lives on the login window** (existing-vault, forgot-password) — the realistic scenario routes to login, not setup; a v1 vault shows "predates seed recovery, use your password then enable it in Settings". Fresh-install/foreign-machine/archive restore deferred (needs archive import).
- `--:--` note: all primitives exist (Sprint 3) — `Bip39SeedManager.DeriveRecoveryKeyAsync` already returns a 32-byte recovery key; the DEK is just AES-GCM-wrapped with it. The gap is purely wiring (`SetupService` ignores the mnemonic today) + the v2 file format. See [[project_dek-format-seed-gap]].
- `--:--` structure: 25-A DEK dual-wrap + `ISeedRecoveryService` (headless) → 25-B restore-from-seed UI (login window) → 25-C enable-seed-recovery in Settings.
