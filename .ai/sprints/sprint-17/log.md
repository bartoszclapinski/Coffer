# Sprint 17 log

## 2026-06-30

- `plan` sprint-17 drafted — the AI-assisted fallback statement parser deferred since Sprint 8, closing the Phase-1 item from `docs/architecture/03-statement-parsers.md`. Split into 17-A (parser + registry fallback + anonymisation extension + gating + tests) and 17-B (Ustawienia opt-in toggle + owner-name field + import warning surface).
- `context`: the registry's `Resolve` still throws `UnsupportedBankException` for unknown banks; both `StatementParserRegistry` and `IStatementParser.CanHandle` doc comments already point at this sprint as the place to swap the throw for an AI fallback "so no callsite has to change". The ledger purpose `parser-fallback` was reserved in `AiDefaults.AiPurpose` for exactly this.
- `decision`: AI fallback is **off by default, behind an explicit Ustawienia opt-in** — sending a whole statement to a third-party model is the most data-exposing AI feature in the app (categorisation sends only descriptions; chat sends aggregated tool output). Maximum-privacy posture ⇒ explicit consent before any statement text leaves the device. When disabled, the registry behaves exactly as today.
- `decision`: the fallback is an **explicit `_fallback` on the registry**, not a normal `(BankCode, Format)` entry — deterministic parsers always win for known banks; the fallback only fills the unknown slot.
- `decision`: the **registry stays dumb**; the `AiAssistedParser` owns the opt-in/key/budget gating and throws `UnsupportedBankException` when blocked, so the import page's existing catch path lights up with no call-site change.
- `decision`: `Confidence = Medium` + a mandatory "parsed by AI fallback — review" warning; `AccountNumber` left empty (the import flow confirms the account), exactly like the PKO CSV parser.
- `decision` (anonymisation option b): extend `PromptAnonymizer` to redact the **owner's name(s)** from the statement header, sourced from an optional `AppSetting` key (no migration — the key/value table exists). When unset, the parser still runs with account/IBAN/NIP redaction only **plus a header-exposure warning** — it does not block.
- `open question` (for owner): confirm opt-in default (off + toggle vs. on + per-import confirm); confirm not blocking when owner name unset (warn only); confirm whether the fallback attempts any `StatementFormat` or PDF-text only in v1.
