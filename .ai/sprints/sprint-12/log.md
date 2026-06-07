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
