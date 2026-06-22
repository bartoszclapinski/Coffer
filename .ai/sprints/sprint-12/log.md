# Sprint 12 log

## 2026-06-07

- Plan written (`chore/plan-sprint-12`). Sprint 12 = Phase 7 (Chat with data): a Polish-language chat
  page where the assistant answers questions about the owner's finances by calling a fixed menu of
  **read-only** data tools (never inventing numbers, never generating SQL), with a per-response
  tool-trace, every turn metered into the cost ledger and gated by the monthly budget.
- Decisions (planning):
  - **Two PRs** (Sprint-10 cadence): 12-A = tool-calling plumbing + the four read-only tools + the
    `ChatService` orchestrator (no UI, fully unit-tested with a scripted fake provider); 12-B = chat
    UI + shell wiring. Chat is a larger surface than the single-PR dashboard, so plumbing-first.
  - **Tool scope limited to data that exists now**: `GetTotalSpent`, `GetTransactions`,
    `GetSpendingByCategory`, `GetMonthlyTrend`. `FindAnomalies` (Phase 8), `GetGoals` (Phase 9),
    `GetReceiptItems` (Phase 5) are explicitly deferred to their phases; the registry is shaped so
    they slot in later without touching the orchestrator.
  - **Tools are read-only** (doc 04 guarantee) — the chat model can never mutate state.
  - **Reasoning tier** for chat (Sonnet 4.6 / GPT-4o), not the cheap categorisation model.
  - **Budget gate**: chat is non-critical priority → blocked (no API call) when over the monthly cap.
  - **Anonymise tool output** before it returns to the model (hard rule #7); master password / BIP39
    never enter any prompt (hard rule #6).
  - Reuse the Sprint-9/11 layering: `IChatService` + DTOs in `Coffer.Core`, `ChatService` + EF-backed
    tools in `Coffer.Infrastructure`, `ChatViewModel` in `Coffer.Application`, `ChatView` in
    `Coffer.Desktop`. `Coffer.Core` stays free of vendor SDK / EF (hard rule #3).
- Grounding check: the Sprint-10 `IAiProvider` exposes `CompleteAsync` / `CompleteJsonAsync` /
  `StreamAsync` (stub) and `AiRequest` has no `Tools` yet — so 12-A must extend the abstraction with
  an `AiTool` descriptor + a tool-calling completion path before the orchestrator can run.
- Open questions parked for implementation: streaming vs whole-message (lean whole-message v1),
  tool-calling via M.E.AI vs Anthropic SDK directly (lean: whatever `ClaudeProvider` already uses,
  keep the tool surface provider-neutral), conversation memory (lean in-session v1), max tool-loop
  iterations (lean ~5 + budget gate), category-name matching (lean exact case-insensitive). See
  `sprint-12.md`.

## 2026-06-07 — 12-A implemented

Tool-calling plumbing, the four read-only tools, and the `ChatService` orchestrator landed (no UI).
Full suite green: 321 tests (8 Core + 66 Application + 247 Infrastructure), including 13 new
`ChatToolsTests` over a real SQLCipher DB and 9 new `ChatServiceTests` with a scripted fake provider
(zero real API calls).

Two deliberate divergences from the plan:

- **Conversation-shaped `AiToolRequest` instead of `AiRequest.Tools` (plan 12.1.a).** The plan
  sketched adding `IReadOnlyList<AiTool>? Tools` to the existing single-prompt `AiRequest`. But the
  tool loop is inherently multi-turn — it re-sends a growing conversation (user → assistant tool
  calls → tool outputs → …) each iteration. A single-prompt request can't carry that state, so I
  added a dedicated `AiToolRequest` (Messages + Tools) + `IAiProvider.CompleteWithToolsAsync`
  returning `AiToolTurn` (final text *or* tool calls). Categorisation keeps using the unchanged
  `AiRequest`/`CompleteJsonAsync` path. Cleaner and matches M.E.AI's message model.
- **`AiDefaults.ChatModel` constant instead of `IAiSettings.GetChatModelAsync` (plan 12.3.g).** The
  plan had the chat model come from `IAiSettings`. Adding a getter there would ripple into 3 test
  stubs + `SettingsViewModel` for no 12-A benefit, since the Settings UI doesn't exist until 12-B.
  Used a `claude-sonnet-4-6` constant for now (satisfies the "reasoning tier, not categorisation
  tier" intent). User-configurable chat-model selection is deferred to 12-B where the Settings UI
  lives.

## 2026-06-22 — 12-B implemented

Chat UI + shell wiring landed on top of the 12-A plumbing. The "Asystent" sidebar entry now opens a
Polish-language chat page that streams whole messages (token streaming stays deferred) from
`ChatService`, renders user/assistant bubbles, shows a collapsible per-message tool-trace, and
handles the empty, busy, missing-API-key, over-budget, and error states.

- **`ChatMessageViewModel`** (Application): wraps one turn — `Author`/`Text`, the formatted
  `ToolTraceLines`, `IsUser`/`IsAssistant`/`HasToolTraces`, and a `ToggleToolTrace` command driving
  `IsToolTraceExpanded` so each assistant message can reveal which tools it called.
- **`ChatViewModel`** (Application): `Messages` collection, `InputText` + `SendCommand`
  (`CanExecute` = not busy and input non-blank), `IsBusy`, `IsEmpty`, four Polish `SuggestedPrompts`
  with a `UseSuggestionCommand`, and the `MissingApiKey`/`BudgetExceeded`/`ErrorMessage` flags lifted
  straight off the `ChatTurn`. The user message is appended (and kept) before the call so a failed
  turn never loses the question.
- **`ChatView.axaml`** (Desktop): empty-state suggested-prompt chips, message bubbles matching the
  dashboard/transactions design language (no chat mockup existed — only a placeholder card), status
  banners, and an input box with Enter→Send. Wired into the shell via `MainViewModel.ShowChat`, a
  `MainWindow` `DataTemplate` + sidebar button, and a `DesktopServiceRegistration` transient.
- Tests: 8 new `ChatViewModelTests` (fake `IChatService`) + a `ShowChat_SwitchesActivePage` case and
  `StubChatService` added to `MainViewModelTests`. Application suite 75/75 green, full solution build
  0 warnings / 0 errors.

**Manual DoD outstanding:** a live model call (the assistant answering with real numbers in the
running desktop UI) is not headlessly verifiable and has not been exercised here.
