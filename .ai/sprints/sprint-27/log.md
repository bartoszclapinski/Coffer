# Sprint 27 log

## 2026-07-08

- `--:--` decision: Sprint 27 = the doc-08 disaster-recovery tail, all three items (owner: "skończmy restore" → full tail). Scope confirmed with the owner: (1) archive `.zip` restore on a fresh/foreign machine, (2) monthly auto-test restore, (3) a password-gated **regenerate** recovery seed.
- `--:--` decision: "Show recovery seed" reframed as **"Regenerate recovery seed"** — the mnemonic is never persisted (hard rule #6) and BIP39 is one-way, so the original 12 words are unrecoverable; the honest equivalent is minting a fresh seed and re-wrapping the DEK (reusing `EnableSeedRecoveryAsync`), telling the owner to rewrite their paper. (Owner picked this over dropping it and over persisting the mnemonic encrypted-at-rest.)
- `--:--` decision: archive restore does **no crypto** — the `.zip` holds already-encrypted `coffer.db` + `dek.encrypted`; import validates + extracts to the vault paths, then the existing login window (password or "Restore from seed") unlocks. Fresh-install only; refuses to clobber an existing vault.
- `--:--` plan written (`sprint-27.md`), Status: Planned. Four PRs: 27-A archive import (headless) → 27-B fresh-install restore UI → 27-C monthly auto-test restore → 27-D regenerate recovery seed. Awaiting plan-PR review before implementation.
