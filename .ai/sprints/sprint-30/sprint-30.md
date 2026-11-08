# Sprint 30 — Real-data readiness (vault integrity, AI privacy, import robustness)

**Phase:** — (hardening before the owner's first real-data import; touches doc 02 encryption/backup, doc 03 parsers, doc 04 AI privacy, doc 08 backup)
**Status:** In progress
**Depends on:** sprint-23/24 (`BackupSnapshotWriter`, `BackupService`, `ArchiveExporter`, `RestoreService`, `BackupRetention`), sprint-8/9 (`PkoHistoriaCsvParser`, `FingerprintBankDetector`, `ImportStatementUseCase`, `TransactionHash`), sprint-10 (`PromptAnonymizer`, `HybridCategorizer`), sprint-12/13/14/16 (chat tools, `AnomalyCommentator`, `AdvisorReportGenerator`, `CashFlowExplainer`), sprint-17 (`AiAssistedParser`), and the pre-sprint security fix that pins SQLCipher as the only SQLite provider and fails closed without it. No EF schema change.

## Goal

The owner can create a fresh vault with a Release build and import their real PKO statements with confidence: backups actually happen and are consistent, nothing personal leaves the machine in AI prompts or plaintext logs, real-world CSV exports import without silent loss, and dev runs can never touch the real vault.

## Why this sprint exists

A readiness audit (2026-11-08, adversarially verified) run at the end of Sprint 29 found that the feature set is complete but several paths would hurt the moment real data goes in:

- **Backups silently fail.** `BackupSnapshotWriter` opens `coffer.db` with `FileShare.Read`, which conflicts with any pooled SQLite connection — so the daily snapshot fails (logged only) and "Back up now" almost always fails after Settings has loaded. `ArchiveExporter` hits the same lock *after* `FileMode.Create` has already truncated the target, destroying a previous archive at that path. Retention is age-only, and "Back up now" overwrites today's daily snapshot.
- **AI prompts leak personal data (hard rule 7).** `PromptAnonymizer.Anonymize(string)` only redacts IBAN/NIP/account digits. PKO descriptions join every column — counterparty names, `Adres nadawcy/odbiorcy`, phone numbers (BLIK), PESEL in tax transfers — and the categorizer, chat tool results, anomaly commentator, advisor report and cash-flow explainer all send them. Owner identity names are applied only by the AI fallback parser.
- **Identical transactions collapse.** Two genuinely identical rows in one statement (two same coffees, same day, same description) hash identically and the second is dropped as a "duplicate".
- **Encoding is assumed.** The PKO parser always decodes Windows-1250 with BOM detection off: a UTF-8 (BOM) export fails, UTF-8 without BOM silently garbles diacritics. The AI fallback reads CSVs as UTF-8, so owner-name redaction misses Windows-1250 names with diacritics.
- **Dev and real data share one vault.** `CofferVaultPaths` is hardwired to `%LocalAppData%\Coffer` for Debug and Release alike — running a feature branch (with its migrations) from the IDE would operate on the real vault.
- **Small privacy leaks in logs.** A counterparty name (`CashFlowPlanningViewModel`) and the statement file name (`ImportStatementUseCase`) are written to the plaintext log.
- **No ignore rule for real CSV statements** in a public repo (`*.real.pdf` etc. exist; CSV exports don't).

## Design decisions (the shape we commit to)

- **Consistent, lock-free snapshots via SQLite's online backup API.** Snapshots are taken through keyed connections (`source.BackupDatabase(destination)`), the destination keyed with the same DEK, written to a temp name and moved into place. The result is the same encrypted, same-DEK file Sprint 24's restore already swaps in — restore semantics don't change. When no DEK is held (pre-login paths, if any), the existing byte copy remains, opened with a share mode that tolerates other readers.
- **Archives are written atomically.** Build to `target.tmp`, verify the expected entries are present and non-empty, then move over the target; on failure delete the temp and leave any previous archive untouched.
- **Retention never deletes the newest backups.** Keep the newest 7 regardless of age, then apply the age rule. Manual "Back up now" writes a distinct, timestamped file instead of overwriting today's daily; restore listing shows both.
- **Backup failures are visible.** The Settings Backup panel shows the age of the newest snapshot and the last failure (if any), not just the log.
- **One data root, chosen per build.** `COFFER_DATA_DIR` (if set) overrides everything; otherwise Debug uses `%LocalAppData%\Coffer.Dev` and Release uses `%LocalAppData%\Coffer`. Logs follow the same root. The owner's existing dev vault can be kept by renaming its folder to `Coffer.Dev`.
- **Anonymize at one choke point.** Every AI path goes through the anonymizer with owner identity names applied (the anonymizer resolves them itself or every caller passes them — whichever makes it impossible to forget; covered by tests on all six consumers). New redactions: PESEL (11 digits), Polish phone numbers, and PKO labelled sub-fields (`Nazwa/Adres nadawcy/odbiorcy`, `Numer telefonu`, `Nazwa i nr identyfikatora`, `Rachunek …`) — the value after the label up to the next label becomes a placeholder.
- **The categorizer sees only what it needs.** It gets merchant-like fields (merchant / location / transfer title), not account, address, phone or identifier fields. Category quality should not depend on personal data.
- **Within-file duplicates keep the first hash.** The 2nd+ occurrence of an identical (account, date, amount, description) key within one file hashes as `base|#n`; the first keeps today's hash, so already-imported rows stay deduplicated. The import summary reports these separately from cross-import duplicates.
- **Encoding detection is shared.** UTF-8 BOM → UTF-8; else strict UTF-8 decode succeeds → UTF-8; else Windows-1250. Used by the PKO detector + parser and the AI fallback. New golden files cover UTF-8 with and without BOM (hard rule 10).

## Approach — four PRs

- **30-A — vault & backup integrity** (headless + a small Settings status line): online-backup snapshots, atomic archive export, retention floor + manual snapshots, visible failures, per-build data root.
- **30-B — AI privacy**: anonymizer redactions + owner names on every path + categorizer input narrowing; log scrubbing.
- **30-C — import robustness**: within-file duplicate occurrences, shared encoding detection, friendly unsupported-layout message, `.gitignore` for real statements, golden files.
- **30-D — running on real data + close**: `docs/running-on-real-data.md` (publish command, data root, first-run + verification checklist), README pointer, sprint closure.

## Steps

### 30-A — vault & backup integrity
- [x] 30.1 Snapshots via the SQLite online backup API on keyed connections (temp → move); test with a pooled connection held open during the snapshot; the snapshot opens with the same DEK and is not plaintext.
- [x] 30.2 `ArchiveExporter` atomic (`.tmp` → verify entries → move); a failure leaves a previous archive intact; DB content taken from a consistent snapshot.
- [x] 30.3 Retention keeps the newest 7 regardless of age; "Back up now" writes a distinct timestamped snapshot; restore listing + retention understand both names.
- [x] 30.4 Settings Backup panel: newest-snapshot age + last failure.
- [x] 30.5 Data root: `COFFER_DATA_DIR` override, Debug → `Coffer.Dev`, Release → `Coffer`; Serilog under the same root; tests.

### 30-B — AI privacy
- [ ] 30.6 `PromptAnonymizer`: PESEL, phone numbers, PKO labelled sub-fields; owner identity names applied on every AI path (categorizer, chat tool results, anomaly commentator, advisor report, cash-flow explainer, AI fallback).
- [ ] 30.7 Categorizer prompt input narrowed to merchant-like fields.
- [ ] 30.8 Remove counterparty name and statement file name from log events.
- [ ] 30.9 Tests: synthetic PKO transfer / BLIK-to-phone / tax-transfer (PESEL) rows produce prompts with no name, address, phone or PESEL, per AI consumer.

### 30-C — import robustness
- [x] 30.10 Occurrence-indexed hash for identical rows within one file (first occurrence unchanged); summary reports them.
- [x] 30.11 Shared CSV encoding detection (BOM / strict UTF-8 / Windows-1250) in the PKO detector + parser and the AI fallback; golden files for UTF-8 with/without BOM.
- [x] 30.12 Unsupported CSV layout → a clear, localized message (e.g. "re-saved in Excel?") instead of a generic failure.
- [x] 30.13 `.gitignore`: real statement exports (`*.real.*`, PKO export file-name patterns), keeping the golden fixtures.

### 30-D — running on real data + close
- [ ] 30.14 `docs/running-on-real-data.md`: self-contained publish command, data root, first-run checklist (seed written down + one restore-from-seed drill, owner identity names, AI cap/key, first backup + archive to external media), import verification checklist.
- [ ] 30.15 README pointer; sprint closed (plan, log, index).

## Definition of Done

- With a pooled connection open, "Back up now" and the daily snapshot succeed; the snapshot opens with the DEK; a failed archive export leaves the previous archive intact.
- For synthetic PKO rows containing a person's name, street address, phone number and PESEL, no AI prompt (any consumer) contains any of them.
- Two identical rows in one file import as two transactions; re-importing the same file adds nothing.
- UTF-8 (with and without BOM) and Windows-1250 PKO exports import with correct diacritics.
- A Debug run never opens `%LocalAppData%\Coffer`.
- All tests green; golden files unchanged except the added ones; `dotnet format` clean.

## Files affected

- `src/Coffer.Infrastructure/Backup/*`, `src/Coffer.Infrastructure/Security/CofferVaultPaths.cs`, Serilog configuration, Settings backup status (VM + view + resx)
- `src/Coffer.Infrastructure/AI/PromptAnonymizer.cs` + its consumers (`Categorization/HybridCategorizer.cs`, chat service, `AnomalyCommentator`, `AdvisorReportGenerator`, `CashFlowExplainer`), `CashFlowPlanningViewModel` log line
- `src/Coffer.Infrastructure/Import/ImportStatementUseCase.cs`, `Parsing/Pko/*`, `Parsing/Ai/AiAssistedParser.cs`, a shared encoding helper, golden files
- `.gitignore`, `docs/running-on-real-data.md`, `README.md`, `.ai/sprints/*`

## Open questions

- Is the existing dev vault worth keeping (rename to `Coffer.Dev`) or should it simply be deleted once the real vault exists? → owner decides; the doc describes both.

## Deferred (kept out of scope)

- AI fallback chunking for long non-PKO statements + metering failed calls; merchant-centric category cache key; theme-subscription disposal on logout; opening-balance reconciliation against `Saldo po transakcji`; a longer seed check in the wizard.
- Sprint 27 (archive import on a fresh machine, monthly restore self-test, regenerate seed) — resumes next; until then `docs/running-on-real-data.md` documents the manual archive-restore procedure.
- Redesign polish (former "Sprint 32" items) and the `Accounts` screen.
