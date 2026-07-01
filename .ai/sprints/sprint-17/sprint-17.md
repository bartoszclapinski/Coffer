# Sprint 17 — AI-assisted fallback statement parser

**Phase:** 1 (closes the long-deferred Phase-1 item from Sprint 8 — `docs/architecture/03-statement-parsers.md` §"AI-assisted fallback parser")
**Status:** Planned
**Depends on:** sprint-7/8 (`IStatementParser` / `IBankDetector` / `StatementParserRegistry` / `StatementInput` / `ParseResult`), sprint-9 (import flow + `ImportStatementUseCase` + the import page that surfaces `UnsupportedBankException`), sprint-10 (`IAiProvider.CompleteJsonAsync` + `PromptAnonymizer` + `IAiBudgetGate` + `IAiUsageLedger` + `AppSettingsStore`), sprint-15 (i18n — every new string is a resource key)

## Goal

When the owner imports a statement from a bank we have **no deterministic parser for** (e.g. a mortgage refinance to a new bank), the import still works: an `AiAssistedParser` extracts the transactions via a single reasoning-tier LLM call and returns a `ParseResult` with `Confidence = Medium` and a clear warning, instead of the import failing with `UnsupportedBankException`. The numbers come straight from the model's structured JSON over the statement text — this is the one parser that is *not* deterministic, so it is explicitly marked lower-confidence and is **off by default behind an opt-in toggle**, because it sends statement contents to a third-party API.

## Why this sprint exists

Phase 1 (Sprint 7/8) built the parsing spine — registry, detector, `StatementInput`, `ParseResult` — and a deterministic PKO "Historia rachunku" CSV parser, but deliberately left the AI fallback as a follow-up. The registry's `Resolve` still **throws** `UnsupportedBankException` for any unknown bank, and the doc comment in `StatementParserRegistry` and the `CanHandle` doc in `IStatementParser` both point at this exact extension: *"a later sprint swaps that throw for an AI-assisted parser at this same boundary so no callsite has to change."* The owner refinances mortgages between banks (a stated real requirement in `CLAUDE.md`), so an unknown bank is not hypothetical. This sprint fills that slot — and the ledger purpose `parser-fallback` was already reserved in `AiDefaults.AiPurpose` for it.

## Design decisions (the shape we commit to)

- **Opt-in, off by default.** Sending an entire statement to an external model is the most data-exposing AI feature in the app (categorisation sends only normalised descriptions; chat sends aggregated tool output). The maximum-privacy posture (`CLAUDE.md`) means the owner must explicitly enable the AI fallback in Ustawienia before any statement text leaves the device. When disabled, the registry behaves exactly as today (throws `UnsupportedBankException`), so the existing import-failure UX is unchanged.
- **The fallback is wired into the registry as an explicit `_fallback`, not as a normal `(BankCode, Format)` entry.** Per doc 03: a deterministic parser still wins whenever the detector fingerprints a known bank+format; only when no specific parser matches does `Resolve` hand back the fallback. This keeps the lookup dictionary clean and makes "did we use AI?" unambiguous (`BankCode == "AI_FALLBACK"`).
- **The registry stays dumb; the parser owns the gating.** `Resolve` does not know about budget or the opt-in toggle. The `AiAssistedParser` itself, inside `ParseAsync`, checks the opt-in setting, the API-key presence, and the budget gate, and throws `UnsupportedBankException` when any of them blocks it — so the import page's existing `catch (UnsupportedBankException)` path lights up with no new call-site changes. (A short structural reason rides on the exception; never statement content, per rules #6/#11.)
- **`Confidence = Medium` + a mandatory warning.** The AI parser never claims `High`. The import result carries a warning string ("parsed by AI fallback — review the transactions") that the import UI surfaces, so the owner knows these rows are model-extracted, not deterministically parsed.
- **Account number is not trusted from the model.** Exactly like the PKO CSV parser, `AccountNumber` is left empty (with a warning) and the Phase-2 import flow confirms the target account with the owner. This also means we can fully anonymise the account/IBAN out of the prompt without losing data we'd keep.
- **Anonymisation (option b — owner identity).** `PromptAnonymizer` already redacts IBAN/NIP/account (hard rule #7). For the AI parser the prompt is the *whole statement*, whose header carries the **owner's name and address** — not covered today. We extend the anonymiser to also redact owner-identity names, sourced from an optional `AppSetting` key (the key/value table already exists — **no migration**). When the owner has not set their name, the parser still runs but redaction falls back to account/IBAN/NIP only and the result carries an extra warning that the statement header may have reached the model un-redacted. Merchant names and transaction descriptions stay (they are the data we need and the categorisation signal).
- **Model + metering.** Sonnet 4.6 (`AiDefaults.ChatModel`) via `IAiProvider.CompleteJsonAsync<T>`; one ledger entry per import metered as `AiPurpose.ParserFallback` ("parser-fallback", already defined); ~$0.02/statement (doc 03), acceptable for occasional unknown-bank use.

## Approach — two PRs (the A=engine / B=UI cadence)

- **17-A — parser + registry fallback + anonymisation extension + gating + tests (headless).** Build and prove the whole feature without pixels: the `AiAssistedParser`, the registry `_fallback` wiring, the opt-in/budget/key gating that throws `UnsupportedBankException` when blocked, the `PromptAnonymizer` owner-name extension + its `AppSetting`-backed source, the ledger metering, and the prompt/JSON contract. Covered by unit tests with a mock `IAiProvider` plus a gitignored manual harness against a real statement.
- **17-B — UI.** The opt-in toggle + owner-name field in Ustawienia (so option b is actually usable and consent is explicit), and surfacing the AI-fallback `Medium`-confidence warning in the import result. Fully localized (keys in both `.resx`, parity test green). VM + tests.

## Steps

### 17-A — parser + registry fallback + anonymisation + gating

- [ ] 17.1 `AiAssistedParser : IStatementParser` (`Coffer.Infrastructure/Parsing/Ai/`): `BankCode => "AI_FALLBACK"`, a `Format` that signals format-agnostic handling, `CanHandle(BankFingerprint) => true`. `ParseAsync` reads the `StatementInput` text, anonymises it, sends one `CompleteJsonAsync<T>` request (Sonnet 4.6), maps the structured JSON into a `ParseResult` with `Confidence = Medium`, an empty `AccountNumber` + warning, and a "parsed by AI fallback" warning. Money `decimal` (rule #1), `DateOnly` dates (rule #2), `Currency` non-null (rule #9).
- [ ] 17.2 Gating inside `ParseAsync`: if the AI-fallback opt-in is off, or no API key is configured, or `IAiBudgetGate` denies, throw `UnsupportedBankException` (structural reason only). Order so the cheapest check (opt-in flag) runs first.
- [ ] 17.3 Registry fallback wiring: add an explicit fallback to `StatementParserRegistry` (constructor takes the `AiAssistedParser`); `Resolve` returns the fallback when no `(BankCode, Format)` entry matches **and** the fingerprint path falls through, instead of throwing. Update the class/`Resolve` doc comments (the throw is now the parser's job when AI is unavailable). Keep deterministic parsers winning for known banks.
- [ ] 17.4 DI: register `AiAssistedParser` and inject it into the registry in `ServiceRegistration.cs` (it must *not* be picked up as a normal `IEnumerable<IStatementParser>` registry entry — register it as its own type for the fallback slot).
- [ ] 17.5 `IAiSettings` extension: add `GetAiFallbackParsingEnabledAsync` / `SetAiFallbackParsingEnabledAsync` (bool, default **false**) and `GetOwnerIdentityNamesAsync` / `SetOwnerIdentityNamesAsync` (the owner name(s)/aliases to redact), both backed by new `AppSetting` keys in `AppSettingsStore` (no migration — the table exists). Defaults via `AiDefaults`.
- [ ] 17.6 `PromptAnonymizer` owner-name extension: redact configured owner-identity names (case-insensitive, whole-word) before the IBAN/NIP/account passes. The anonymiser must take the names as input (stay a pure, dependency-light component) — the caller (`AiAssistedParser`) resolves them from `IAiSettings` and passes them in, or a small `IOwnerIdentityProvider` supplies them. Graceful when empty (no-op + the parser adds the header-exposure warning).
- [ ] 17.7 Prompt + JSON contract: a focused extraction prompt (statement text → `{ transactions: [{date, description, merchant?, amount, currency}], periodFrom, periodTo, currency }`), a private DTO for `CompleteJsonAsync<T>`, mapped to `ParsedTransaction` / `ParseResult`. Defensive mapping: drop/flag rows with unparseable dates or amounts rather than throwing.
- [ ] 17.8 Metering: write one `parser-fallback` ledger entry per import via `IAiUsageLedger` (token/cost from the `AiResult`), behind the budget gate already checked in 17.2.
- [ ] 17.9 Unit tests (`Coffer.Infrastructure.Tests`): mock `IAiProvider` returns canned JSON ⇒ `ParseResult` has `Confidence = Medium`, the fallback warning, empty `AccountNumber`; registry returns the fallback for an unknown fingerprint and a known parser for a known one; gating throws `UnsupportedBankException` when opt-in off / no key / budget denied; anonymiser redacts owner names when set and falls back (with warning) when unset; metering writes exactly one `parser-fallback` entry; malformed JSON rows are flagged not fatal.
- [ ] 17.10 Manual harness (gitignored, like the Sprint-8 real-CSV harness): run the AI parser against a real statement from an unknown bank, eyeball the extracted transactions. Document the result in `log.md` (counts only, no statement content — rule #11).

### 17-B — UI

- [x] 17.11 Ustawienia: an "AI fallback parsing" opt-in toggle (off by default) with a short consent explanation that statement text will be sent to the AI provider, and an "Account holder name" field feeding owner-identity redaction. Wired through the settings VM to `IAiSettings`. All strings via `{l:Localize}`, keys in **both** `.resx`.
- [x] 17.12 Import result: surface the `Medium`-confidence + "parsed by AI fallback, review the transactions" warning (and the header-exposure warning when owner name is unset) in the import page after an AI-fallback import. Localized. (`ImportSummary` gained `AiFallbackUsed`/`OwnerNameUnredacted` flags — the parser warning constants live in Infrastructure, so the use case sets flags + suppresses the raw English warnings and the view renders localized banners.)
- [x] 17.13 Tests (`Coffer.Application.Tests`): settings VM round-trips the toggle + name via a fake `IAiSettings`; import VM surfaces the AI-fallback flags; use case sets the flags and suppresses the raw AI warnings; resource-key parity test stays green.

### Sweep

- [x] 17.14 Resource-key parity holds; no residual hardcoded user-facing literals in the new settings/import surfaces.
- [ ] 17.15 Manual DoD click-through (below).

## Definition of Done

- **17-A (automated):** parser maps mock JSON to a `Medium`-confidence `ParseResult` with the fallback warning and empty account; registry returns the fallback only for unknown banks; gating throws `UnsupportedBankException` when the opt-in is off, the key is missing, or the budget gate denies; the anonymiser redacts owner names when configured and degrades gracefully (with warning) when not; exactly one `parser-fallback` ledger entry is written per import.
- **17-A (manual):** the gitignored harness parses a real unknown-bank statement and the transaction count/dates look right (recorded in `log.md`, no content committed).
- **17-B (automated + manual):** settings VM round-trips the toggle and the name; the import VM surfaces the AI-fallback warning. Manual: with the toggle **off**, importing an unknown bank fails exactly as today; flip it **on**, set the account-holder name, re-import — the statement parses via AI, transactions land with a visible "AI fallback / review" warning, and the cost shows up in the AI usage ledger. Every label switches PL↔EN live; money still shows "zł".
- **Whole-sprint:** an unknown bank imports end-to-end without a code change, the owner has explicitly consented, and their name is redacted from the prompt.

## Files affected

- `src/Coffer.Infrastructure/Parsing/Ai/AiAssistedParser.cs` (new) + a private extraction-DTO
- `src/Coffer.Infrastructure/Parsing/StatementParserRegistry.cs` (explicit `_fallback`, `Resolve` no longer throws for unknown when AI available; doc comments)
- `src/Coffer.Infrastructure/AI/PromptAnonymizer.cs` (owner-name redaction) + the owner-identity source (`IAiSettings`/`IOwnerIdentityProvider`)
- `src/Coffer.Core/Ai/IAiSettings.cs` + `src/Coffer.Infrastructure/AI/AppSettingsStore.cs` (opt-in flag + owner-name keys) + `AiDefaults` defaults
- `src/Coffer.Infrastructure/DependencyInjection/ServiceRegistration.cs` (register `AiAssistedParser` + inject into registry)
- `src/Coffer.Application/ViewModels/Settings/**` + `src/Coffer.Application/ViewModels/Import/ImportViewModel.cs` (toggle, name field, AI-fallback warning)
- `src/Coffer.Desktop/Views/**` (Ustawienia toggle+field, import warning surface)
- `src/Coffer.Application/Localization/Strings.resx` + `Strings.pl.resx` (new keys, parity)
- `tests/Coffer.Infrastructure.Tests/Parsing/**`, `tests/Coffer.Infrastructure.Tests/AI/**`, `tests/Coffer.Application.Tests/ViewModels/**`

## Open questions

To confirm with the owner before/while building (recorded as decisions in `log.md` once settled):

- **Opt-in default** → proposed **off by default** behind an explicit Ustawienia toggle with a consent note, given the maximum-privacy posture. Confirm this is the desired friction (vs. on-by-default with a per-import confirm prompt).
- **Owner-identity source (option b)** → proposed an optional "Account holder name" `AppSetting` (encrypted DB, no migration), comma-separated for multiple aliases/spellings; when unset the parser runs with account/IBAN/NIP redaction only **plus a warning**. Confirm we should *not* block AI parsing when the name is unset (just warn).
- **Format handling** → the current registry keys on `(BankCode, Format)`; the AI parser is format-agnostic. Confirm the fallback should attempt **any** `StatementFormat` it is given (PDF text + CSV text), or restrict v1 to PDF-text only (the case a deterministic CSV parser would otherwise cover).

## Deferred to a follow-up

- **"Imported this unknown bank 3+ times → suggest a deterministic parser" nudge** (doc 03 §"Migration path"). Out of scope for v1; the manual dev-side generation flow stays manual.
- **Multi-account / multi-currency statements** in a single AI import — v1 targets a single-account statement, mirroring the deterministic parsers.
- **Re-using the AI parser's JSON output as golden-test seed material** for hand-writing the eventual deterministic parser.
