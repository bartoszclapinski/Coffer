# Sprint 30 log

## 2026-11-08

- `--:--` decision (owner): "domknąć projekt i zacząć testować na prawdziwych danych" — finish the redesign, then make the app safe for real data before the first import.
- `--:--` context: a real-data readiness audit (five auditors, each finding adversarially verified) ran alongside the Sprint 29-E review. Its blocker — a fresh NuGet restore silently swapping SQLCipher for plain SQLite (plaintext vault) — was fixed pre-sprint in PR #233 (`Sqlite.Core`, exact package pins, fail-closed `PRAGMA cipher_version` guard, fatal window instead of a silent exit). The remaining must-fix-before-real-data findings form this sprint.
- `--:--` plan written (`sprint-30.md`), Status: Planned. Four PRs: 30-A vault & backup integrity → 30-B AI privacy → 30-C import robustness → 30-D running-on-real-data guide + close. Sprint 27 stays paused until after this sprint.
