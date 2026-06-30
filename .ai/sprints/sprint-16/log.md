# Sprint 16 log

## 2026-06-30

- `plan` sprint-16 drafted — timing-aware cash-flow planning + AI explainer, split into 16-A (domain + detection + projection engine + persistence), 16-B (planning UI), 16-C (AI tool + explainer).
- `decision`: `RecurringFlow` is a **persisted, user-editable entity**, not a recomputed read-model — because the accrual offset ("which period a payment belongs to") is owner domain knowledge, not derivable from bank data (`BookingDate` is the posting date, never the accrual period). Detection only proposes; the owner confirms/edits.
- `decision`: flows are **bidirectional** (Inflow/Outflow) — income timing is half the problem.
- `decision`: the projection engine stays **deterministic in `Coffer.Core`**; the AI only explains (Sprint-14 "engine calculates, AI explains" rule).
- `decision` (owner, 2026-06-30): **starting balance = running sum of the account's transactions** (from the statements), not user-entered. Owner accepts responsibility for keeping imports contiguous; to make that safe, add a `StatementContinuityChecker` that detects gaps in `ImportSession` periods and **warns** when the history is non-contiguous (a gap silently corrupts the running-sum balance).
- `decision` (owner): projection **horizon selectable, default 60 days**.
- `decision` (owner): **income modelled as `Inflow` flows**, detected like outflows.
- `decision` (owner): **discrete recurring flows only in v1** — variable-spend daily-burn overlay deferred.
- `decision` (owner): nav label **"Plan przepływów" / "Cash flow"** (`Nav.CashFlow`).
- `decision` (owner): **per-event accrual-period badge in v1**; monthly "true cost" accrual rollup deferred.
