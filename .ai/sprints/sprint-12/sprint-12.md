# Sprint 12 — Chat with data

**Phase:** 7 (Chat with data)
**Status:** Planned
**Depends on:** sprint-10 (`IAiProvider` + `AiRequest`/`AiResult`, `IAiBudgetGate`, `IAiUsageLedger`,
`IPromptAnonymizer`, `ISecretStore`, `IAiSettings` + provider/model selection in Settings),
sprint-9 (`Transaction`/`Category`/`Account` + the read-side query pattern and category resolution),
sprint-11 (server-side aggregation query shapes — category breakdown / monthly trend — to reuse)

## Goal

The owner opens a Chat page, asks a natural-language question about their finances in Polish
(e.g. "ile wydałem na paliwo w maju?"), and the assistant answers with real numbers — obtained by
the model calling read-only data tools (never by inventing figures or generating SQL) — with a
collapsible panel showing exactly which tools ran and with what arguments, every turn metered into
the existing cost ledger and gated by the monthly budget.

## Background

Phases 0–4 and 6 are closed: encrypted vault, PKO CSV import, filterable transaction grid, hybrid
auto-categorisation, and a dashboard. The AI plumbing already exists from Sprint 10 — a
provider-neutral `IAiProvider` (Claude today), an anonymiser (hard rule #7), a token-pricing cost
ledger, a budget gate, and Settings for provider/model/cap. What is missing is the *conversational*
surface from `10-roadmap.md` Phase 7: let the user ask questions and have the model answer by calling
a fixed menu of **read-only** tools over their data.

Per `docs/architecture/04-ai-strategy.md` ("Chat with data — tool calling"):
- **Tool calling, not SQL generation** — a fixed, safe menu of read-only operations; the chat model
  can never mutate state (only the user can, through dedicated UI).
- Chat uses the **reasoning** tier (Claude Sonnet 4.6 / GPT-4o), not the cheap categorisation tier.
- A **system prompt** sets language (Polish default), "never invent numbers", today's date, "no
  legal/tax/licensed-investment advice" (reinforces the "What this app is NOT" guardrails).
- **Tool transparency**: show the user which tools were called (collapsible trace) to build trust.

### Tool scope for this sprint

Doc 04 lists seven tools, but four depend on data layers that do not exist yet. This sprint ships
**only the tools whose backing data already exists**:

- `GetTotalSpent(from, to, category?)`
- `GetTransactions(from, to, merchantPattern?, category?, limit)`
- `GetSpendingByCategory(from, to)`
- `GetMonthlyTrend(category, months)`

**Explicitly deferred to their phases (not in this sprint):** `FindAnomalies` (Phase 8),
`GetGoals` (Phase 9), `GetReceiptItems` (Phase 5). The tool registry is shaped so these slot in
later without touching the orchestrator.

**Out of scope (later phases / explicitly deferred):**
- Anomaly/goals/receipt tools (above).
- Multi-currency reasoning — tools answer in the single display currency (PLN) like the dashboard;
  other-currency rows are out of scope for v1 (note in Open questions).
- Persisting chat history across app restarts — conversation lives in memory for the session;
  durable history is a later nicety.
- A mobile chat surface (MAUI not stood up).

## Layering note

Same pattern as Sprints 9–11. The chat **orchestrator interface lives in `Coffer.Core`**
(`Chat/IChatService.cs` + message/turn DTOs), the **implementation in `Coffer.Infrastructure`**
(runs the tool-call loop over `IAiProvider`, executes EF-backed tools, meters the ledger, enforces
the budget gate, anonymises tool output). The **read-only financial tools** are EF-backed read
queries in `Coffer.Infrastructure/Chat/` over `IDbContextFactory<CofferDbContext>` (server-side
aggregation, `AsNoTracking`), exposed to the model through the provider's tool mechanism. The
**`ChatViewModel` lives in `Coffer.Application`**, the **`ChatView` in `Coffer.Desktop`**.
`Coffer.Core` stays free of any vendor SDK / EF dependency (hard rule #3). Tools are **read-only**:
they never write to the DB (hard guarantee from doc 04).

## Delivery

**Two PRs** (Sprint-10 cadence — plumbing first, UI second; chat is a larger surface than the
single-PR dashboard):

- **12-A — tool-calling plumbing + tools + chat service (no UI).** Extend the AI abstraction with
  tool calling, implement the four read-only tools, build the orchestrator (`ChatService`) that runs
  the tool-call loop, meters the ledger per turn, enforces the budget gate (chat is **non-critical**
  priority — blocked when over cap), and anonymises tool output. Fully unit-tested with a fake
  provider that scripts tool-call turns (no real API calls).
- **12-B — chat UI + shell wiring.** `ChatViewModel` + `ChatView` (message list, input box, suggested
  prompts, per-response collapsible tool-trace), a "Asystent" sidebar entry, DI registration. Manual
  DoD (a live model call is not headlessly verifiable).

Standard flow per PR: `gh issue create` (labels `feat` + `sprint-12`) → branch → commits → push →
`gh pr create` (`Closes #<issue>`) → CI green → `gh pr merge --squash --delete-branch`. If `main`
moved, `gh pr update-branch` + re-run CI first. Close out with `chore/close-sprint-12` (log + index).

## Steps

### 12-A — plumbing, tools, service

- [ ] 12.1 Extend the AI abstraction for tool calling:
  - 12.1.a `AiTool` descriptor in `Coffer.Core/Ai/` (name, description, JSON parameter schema) and an
    `AiToolCall` / `AiToolResult` pair for the loop; add `IReadOnlyList<AiTool>? Tools` to `AiRequest`.
  - 12.1.b Add a tool-calling completion path to `IAiProvider` (either a `CompleteWithToolsAsync`
    returning either a final message or a list of requested tool calls, or surface tool calls through
    the existing result type). Keep `CompleteJsonAsync` (categorisation) untouched.
  - 12.1.c Implement it in `ClaudeProvider` via the SDK's tool-use API; still return token usage in
    `AiResult` for the ledger. `OpenAiProvider` (if present as a stub) gets the same shape or a clear
    `NotSupported` until wired.
- [ ] 12.2 Read-only financial tools in `Coffer.Infrastructure/Chat/` over `IDbContextFactory`,
  `AsNoTracking`, **server-side aggregation** (no client-side summation):
  - 12.2.a `GetTotalSpent(from, to, category?)` — `SUM` of `Amount < 0` (returned as positive
    magnitude), optional category filter.
  - 12.2.b `GetTransactions(from, to, merchantPattern?, category?, limit)` — filtered projection
    (reuse the `TransactionListItem` shape); cap `limit` (e.g. ≤ 50).
  - 12.2.c `GetSpendingByCategory(from, to)` — `GROUP BY CategoryId` → name→total map (null → "Bez
    kategorii"), PLN-scoped.
  - 12.2.d `GetMonthlyTrend(category, months)` — `GROUP BY` year-month over the window for one
    category.
  - 12.2.e Category arguments arrive as **names** (the model speaks Polish category names); resolve
    name → `CategoryId` in the tool layer (case-insensitive; unknown name → empty result, not error).
  - 12.2.f Validate tool inputs (doc 04 "Hallucinated tool args"): reject impossible dates, clamp
    `limit`, treat unknown category as empty.
- [ ] 12.3 `IChatService` (Core) + `ChatService` (Infra) orchestrator:
  - 12.3.a Run the tool-call loop: send conversation + tool menu → if the model requests tool calls,
    execute them, append results, re-send → repeat until a final text answer (bounded max iterations,
    e.g. 5, to stop runaway loops).
  - 12.3.b System prompt per doc 04 (Polish default, "never invent numbers", `today_iso`, "no
    legal/tax/licensed-investment advice", concise 2–4 sentences).
  - 12.3.c **Budget gate**: chat is `AiPriority` non-critical → estimate cost, call `IAiBudgetGate`;
    when over cap, do not call the API — surface a friendly "budżet AI wyczerpany" turn instead.
  - 12.3.d **Ledger**: write an `AiUsageEntry` per model turn (`Purpose = "chat"`) with token counts
    and PLN cost from `IAiPricing`.
  - 12.3.e **Anonymise** tool output before it returns to the model (hard rule #7): run serialized
    tool results through `IPromptAnonymizer` (account/IBAN/NIP redaction; merchant names kept per
    doc 04). Master password / BIP39 never enter any prompt (hard rule #6).
  - 12.3.f Surface the executed tool calls (name + args) to the caller so the UI can render the trace.
  - 12.3.g Use the chat **reasoning** model from `IAiSettings` (Sonnet 4.6 / GPT-4o), not the
    categorisation model.
- [ ] 12.4 Failure handling per doc 04: network retry/backoff, honour `Retry-After` on 429, bad-JSON
  one-retry, tool errors propagate back to the model to self-correct; never swallow silently — log to
  Serilog with anonymised context.
- [ ] 12.5 Tests (12-A): a `FakeAiProvider` that scripts "request tool X → then final answer" turns;
  assert the loop executes the right tool with parsed args, feeds results back, terminates, meters the
  ledger, and blocks when the budget gate denies; tool queries tested over **real SQLCipher** with
  seeded multi-month/multi-category data (sums, category map, monthly trend, name resolution, unknown
  category, date validation, currency scoping). No real API calls anywhere.

### 12-B — chat UI + shell

- [ ] 12.6 `ChatViewModel` (Application): `ObservableCollection` of message VMs (user / assistant,
  each assistant message carrying its tool-trace list), `SendCommand` (disabled while a turn is in
  flight), input text, busy flag, error/empty state, suggested-prompt seeds (e.g. "Ile wydałem na
  paliwo w tym miesiącu?"). Calls `IChatService`.
- [ ] 12.7 `ChatView.axaml` (+ minimal code-behind) in `Coffer.Desktop`: scrolling message list
  (user vs assistant bubbles), an input box + send button, suggested-prompt chips on an empty
  conversation, and a per-assistant-message **collapsible tool-trace** (`🔧 GetSpendingByCategory(from=…, to=…)`).
  Match the light design language of the existing views and the chat mockup in
  `docs/mockups/index.html`.
- [ ] 12.8 Wire into the shell (`MainViewModel` + `MainWindow.axaml`): "Asystent" sidebar entry with
  `ShowChatCommand` / `IsChatActive`; register `ChatViewModel` (Application/Desktop DI) and
  `IChatService` (Infrastructure DI). Dashboard stays the default landing page.
- [ ] 12.9 Empty-budget / no-API-key states: if no chat API key is configured, the page explains how
  to add one in Settings rather than erroring; over-cap shows the budget message from 12.3.c.
- [ ] 12.10 `dotnet build` + `dotnet test` + `dotnet format --verify-no-changes` green locally and on
  CI for each PR; full suite stays green; 0 build warnings.

## Definition of Done

1. A new **"Asystent"** sidebar page hosts a chat; Dashboard remains the post-login landing page.
2. Asking **"ile wydałem na paliwo w maju?"** returns an answer containing the **real** number for the
   user's data, and the response's tool-trace shows `GetSpendingByCategory` / `GetTotalSpent` (or
   similar) with the concrete arguments the model used.
3. The model **never invents numbers** — answers are derived from tool calls; tools are **read-only**
   (no DB writes).
4. Every model turn is recorded in the **cost ledger** (`Purpose = "chat"`); when the monthly cap is
   exceeded, chat is blocked with a clear message and **no API call is made** (budget gate honoured).
5. Tool output sent back to the model is **anonymised** (account/IBAN/NIP redacted); master password
   and BIP39 never appear in any prompt.
6. No chat API key configured → the page guides the user to Settings instead of crashing.
7. Tests green locally and on CI; **no real API calls in tests**; `dotnet format` clean; 0 warnings.

## Files affected

**New (Core):**
- `src/Coffer.Core/Ai/AiTool.cs` (+ `AiToolCall` / `AiToolResult`); edit `AiRequest.cs`, `IAiProvider.cs`
- `src/Coffer.Core/Chat/IChatService.cs` + chat message/turn/tool-trace DTOs

**New (Infrastructure):**
- `src/Coffer.Infrastructure/Chat/ChatService.cs`
- `src/Coffer.Infrastructure/Chat/` financial tool queries (the four read-only tools + a registry)
- edits to `src/Coffer.Infrastructure/AI/ClaudeProvider.cs` (tool-use path)

**New (Application/Desktop):**
- `src/Coffer.Application/ViewModels/Chat/ChatViewModel.cs` (+ message VM)
- `src/Coffer.Desktop/Views/ChatView.axaml` (+ `.axaml.cs`)

**Modified:**
- `src/Coffer.Application/ViewModels/Main/MainViewModel.cs` (Chat nav)
- `src/Coffer.Desktop/MainWindow.axaml` (sidebar entry, DataTemplate)
- `ServiceRegistration.cs` (Infra: `IChatService` + tools) and `DesktopServiceRegistration.cs`
  (`ChatViewModel`)
- `docs/architecture/04-ai-strategy.md` / `10-roadmap.md` if the realised tool/loop shape diverges

## Open questions

- **Streaming vs whole-message:** stream the final answer token-by-token (`StreamAsync`, currently a
  stub) or render the whole message when complete? **Lean:** whole-message for v1 (simpler, the
  tool-call loop already implies a wait); streaming is a follow-up. Resolve before 12.7.
- **Tool-calling via `Microsoft.Extensions.AI` vs the Anthropic SDK directly:** doc 04 favours
  M.E.AI for provider-swap. **Lean:** use whatever the existing `ClaudeProvider` already builds on;
  keep the tool surface provider-neutral in `Coffer.Core` so OpenAI slots in later. Resolve in 12.1.
- **Conversation memory:** in-session only vs persisted to the encrypted DB. **Lean:** in-session for
  v1 (out of scope above); persistence later.
- **Max tool-loop iterations / cost ceiling per question:** what bound stops a runaway multi-tool
  conversation? **Lean:** cap iterations (~5) and rely on the budget gate; tune during 12.3.
- **Category-name matching:** exact (case-insensitive) vs fuzzy when the model passes a near-miss
  Polish name. **Lean:** exact case-insensitive for v1; unknown → empty result, let the model retry.
- New questions that surface during the sprint are logged in `log.md`.
