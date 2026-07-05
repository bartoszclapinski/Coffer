# Sprint 26 log

## 2026-07-05

- `--:--` sprint planned — change master password (doc 09 "change master password → re-encrypt DEK only"). The sibling of Sprint-25 seed recovery, closing the password lifecycle (set → use → recover → rotate). Plan in `sprint-26.md`.
- `--:--` decision: **change = re-wrap the password blob only** — the DEK is unchanged, so the database is untouched and the seed-wrapped copy passes through verbatim; the file **version is preserved** (v1 stays v1, v2 stays v2). Reuses `DekFile.WriteReplaceAsync` (atomic) from Sprint 25.
- `--:--` decision: **the current password is required** — verified by deriving its master key and decrypting the password blob (same as login), which both authenticates and yields the DEK to re-wrap. Defends a walk-up attacker at an unlocked session; wrong current password throws `InvalidMasterPasswordException` and changes nothing.
- `--:--` decision: **refresh the DPAPI cache to the new master key** (not just invalidate) — the owner just re-authenticated, so the next cold start stays silent; equivalent security, better UX.
- `--:--` decision: a dedicated `IMasterPasswordService` (not on `LoginService`/`SeedRecoveryService`) keeps each key-lifecycle service single-purpose. Injects `IMasterKeyDerivation`/`IKeyVault`/`IVaultPaths`/`ILogger` only.
- `--:--` structure: 26-A `IMasterPasswordService` (headless) → 26-B Settings "Change master password" dialog. See [[project_dek-format-seed-gap]] for the dual-wrap model this builds on.
- `--:--` 26-A complete — `IMasterPasswordService` + `MasterPasswordService` (verify current → re-wrap password blob under a new key → preserve version + seed blob → refresh cache), registered in `AddCofferLogin`. 6 Security tests (rotate, wrong-password-no-op-byte-identical, v2 seed preserved, v1 stays v1, no-vault, cache-refresh); full suite 683 green (was 677). Headless.
