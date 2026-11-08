# Sprint 31 — Local AI (Ollama) and per-role provider routing

**Phase:** — (doc 04 provider abstraction; follows the owner's first real-data setup)
**Status:** Planned
**Depends on:** sprint-10 (`IAiProvider`, `ClaudeProvider`, `AiPricing`, `AiBudgetGate`, `AiUsageLedger`, `AppSettingsStore`, `HybridCategorizer`), sprint-12 (`ChatService` tool loop), sprint-13/14/16 (`AnomalyCommentator`, `AdvisorReportGenerator`, `CashFlowExplainer`), sprint-17 (`AiAssistedParser`), sprint-30 (`IPromptAnonymizer` redaction sessions on every AI path)

## Goal

The owner can run every AI feature on a **local Ollama model** — nothing leaves the machine, nothing is billed — or on Claude, chosen **per role**: one provider + model for categorization, one for analysis (chat, advisor report, anomaly comments, cash-flow explanation, AI fallback parsing). Claude defaults move to the current, cheaper generation, and the Settings screen no longer offers providers that do not work.

## Why this sprint exists

- **The owner runs Ollama locally** (`qwen2.5:3b`, `qwen3.5` 9.7B with tools + thinking) and wants to use it. Local inference is the strongest privacy option the app can offer (doc 04, CLAUDE.md "privacy posture: maximum") and costs nothing.
- **Only one provider is really wired.** `ClaudeProvider` is the sole `IAiProvider` registration. Settings offers "OpenAI" and `gpt-4o` / `gpt-4o-mini`, which have no implementation — picking them fails at call time.
- **The analysis model is hard-coded.** Chat, advisor report, anomaly comments, cash-flow explanation and AI fallback parsing all use `AiDefaults.ChatModel` (`claude-sonnet-4-6`); only the categorization model is selectable.
- **Claude defaults are a generation behind.** `claude-haiku-4-5` ($1 / $5 per MTok) and `claude-sonnet-4-6` ($3 / $15) have successors `claude-haiku-5-5` ($0.10 / $0.50) and `claude-sonnet-5-5` ($2 / $10). The 5.5 models reject non-default `temperature` (400) and think by default, so switching is not a string change.

## Design decisions (the shape we commit to)

### Roles, not one global provider

- **Two roles: `Categorization` and `Analysis`.** Each has its own provider + model in `IAiSettings`. `Analysis` covers chat, advisor report, anomaly commentary, cash-flow explanation and AI fallback parsing. This lets the owner keep e.g. categorization local and analysis on Claude, or everything local.
- **One routing provider.** Consumers keep depending on `IAiProvider`; the registered `IAiProvider` becomes a router that dispatches each request to the concrete provider named on the request (`AiRequest.Provider` / `AiToolRequest.Provider`). Consumers resolve their route (`provider`, `model`) from settings per call through one small resolver, so none of them hard-codes a model or provider again.
- **Settings keys are additive.** The existing `ai.activeProvider` and `ai.categorizationModel` keep meaning "categorization provider/model"; new keys hold the analysis provider/model and the Ollama endpoint. No migration — it is the key/value settings table, and absent keys fall back to defaults.

### Ollama provider

- **Native Ollama HTTP API (`/api/chat`, `/api/tags`, `/api/version`) over `HttpClient`, no new NuGet package.** `stream: false`; `format: "json"` for JSON completions; `tools` for tool-calling turns (tool results go back as `role: "tool"` messages); usage from `prompt_eval_count` / `eval_count`. Tool calls without an id get a generated one.
- **Context window set explicitly.** Ollama's default context is small and truncates long prompts silently, so every request sends `options.num_ctx` (16 384) alongside `num_predict` (max tokens) and `temperature`.
- **Thinking models stay usable.** JSON completions (categorization, fallback parsing) ask a thinking-capable model not to think (speed, clean JSON); the answer is read from `message.content`, never from `message.thinking`. Behaviour against a non-thinking model must not error.
- **Endpoint is a setting**, default `http://localhost:11434`. Long timeout (minutes — local models are slow), cancellation honoured. A connection failure or unknown model surfaces as a typed `AiProviderUnavailableException` with a clear message, handled wherever `MissingAiKeyException` is handled today.
- **Redaction stays on for Ollama.** Every Ollama call goes through the same redaction session as Claude: one code path, and the endpoint may point at another machine. Settings warns when the endpoint is not a loopback address.
- **Local calls cost 0 PLN.** Pricing is provider-aware (`Ollama` → 0); the ledger still records usage (tokens, provider, model) for visibility; the budget gate always allows a zero-cost call, even past the cap.
- **No API key needed for Ollama.** Readiness checks (e.g. `AiAssistedParser`'s key check) become provider-aware.

### Claude refresh

- **Defaults move to `claude-haiku-5-5` (categorization) and `claude-sonnet-5-5` (analysis).** The 4.x IDs stay selectable and priced, so a stored choice keeps working and the owner can fall back if a 5.5 model misbehaves.
- **No `temperature` on models that reject it**; enough `max_tokens` headroom that default thinking cannot truncate a JSON answer (or thinking turned off where the model allows it).
- **OpenAI is removed from the UI and from the defaults**, not implemented: no provider exists and the owner does not need it. Doc 04 records OpenAI as deferred.
- **A live check the owner can run.** A test skipped by default that, given an API key in an environment variable, sends one categorization batch and one tool-calling turn to each default Claude model — the only way to prove the SDK handles 5.5 responses.

## Approach — four PRs

- **31-A — Claude refresh + remove the dead OpenAI option.** New default model IDs and prices, no temperature where rejected, headroom for thinking, OpenAI gone from the model/provider lists and defaults, live check (skipped by default).
- **31-B — per-role routing (headless).** `AiRole`, per-role provider/model settings, route resolver, routing `IAiProvider`, provider-aware pricing / budget gate / readiness, all six consumers on routes. With only Claude registered, behaviour is unchanged.
- **31-C — Ollama provider (headless).** `OllamaProvider` (complete, JSON, tool turn), endpoint setting, model listing + connection test service, `AiProviderUnavailableException` handling in consumers, unit tests over a fake `HttpMessageHandler`, live check against a local Ollama (skipped by default).
- **31-D — Settings UI, docs, close.** Per-role provider + model pickers (Ollama models listed from the endpoint, with refresh), endpoint field + "Test connection", loopback warning, PL + EN strings, both themes; doc 04 + `docs/running-on-real-data.md` (how to use Ollama); sprint closure.

## Steps

### 31-A — Claude refresh

- [x] 31.1 `AiDefaults`: categorization `claude-haiku-5-5`, analysis `claude-sonnet-5-5`; OpenAI constants removed; `AiPricing` gains the 5.5 rates, keeps the 4.x rates, drops GPT.
- [x] 31.2 `ClaudeProvider`: omit `temperature` for models that reject it; JSON calls cannot lose their answer to thinking (max-tokens headroom or thinking off where allowed). Unit tests on the request shape where the SDK allows it.
- [x] 31.3 Settings model/provider lists: Claude models only (5.5 first, 4.x kept); `AiAssistedParser` key check no longer mentions OpenAI.
- [x] 31.4 Live Claude check, skipped unless an API key env var is set.

### 31-B — per-role routing

- [ ] 31.5 `AiRole` (`Categorization`, `Analysis`), `AiRoute(Provider, Model)`; `IAiSettings` per-role provider + model (existing keys = categorization).
- [ ] 31.6 Route resolver; `AiRequest` / `AiToolRequest` carry `Provider`; routing `IAiProvider` dispatching to registered concrete providers (unknown provider → clear exception).
- [ ] 31.7 Provider-aware `IAiPricing.Estimate`; budget gate always allows zero-cost calls; provider-aware readiness (key needed only for Claude).
- [ ] 31.8 `HybridCategorizer`, `ChatService`, `AdvisorReportGenerator`, `AnomalyCommentator`, `CashFlowExplainer`, `AiAssistedParser` resolve their role's route per call; no remaining `AiDefaults.ChatModel` call-site use. Tests per consumer that the routed provider/model is what gets sent and estimated.

### 31-C — Ollama provider

- [ ] 31.9 `OllamaProvider : IAiProvider` over `/api/chat` (text, JSON, tool turn; `num_ctx`, `num_predict`, `temperature`, thinking handling, usage, generated tool-call ids).
- [ ] 31.10 Endpoint setting (default `http://localhost:11434`); `AiProviderUnavailableException` for connection failures / unknown model; consumers degrade the same way as for a missing key.
- [ ] 31.11 Model catalog + connection test (`/api/tags`, `/api/version`) behind an interface for the Settings screen.
- [ ] 31.12 Unit tests over a fake `HttpMessageHandler` (request shape, JSON, tool calls in and out, errors, cancellation); live check against a local Ollama, skipped by default.

### 31-D — Settings UI, docs, close

- [ ] 31.13 Settings AI section: per-role provider + model pickers, Ollama endpoint + "Test connection" + refresh models, loopback warning, API key shown for Claude; PL + EN resources; light + dark.
- [ ] 31.14 Doc 04 provider section (Claude + Ollama, roles, cost, redaction), `docs/running-on-real-data.md` Ollama section, README if needed.
- [ ] 31.15 Sprint closure (plan, log, index).

## Definition of done

- With Ollama running locally, the owner can set both roles to an Ollama model and categorize an import, chat, and generate an advisor report with no network access beyond `localhost` and no API key.
- With Claude selected, the 5.5 defaults work end to end (verified by the live check with the owner's key), and a 4.x model can still be chosen.
- Settings offers only working providers; an unreachable Ollama gives a clear message, never a crash.
- Every AI call, local or remote, still goes through the redaction session (Sprint 30 tests stay green).
- `dotnet build` + `dotnet test` green locally; CI (`build-and-test`, `format-check`) green on every PR.

## Out of scope

- OpenAI or other remote providers (deferred; doc 04).
- Streaming chat responses (`StreamAsync` stays a stub).
- Turning redaction off for local models.
- Downloading or managing Ollama models from the app (the owner uses `ollama pull`).
