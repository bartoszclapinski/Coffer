# Sprint 18 — Real-balance anchoring + "can I afford this?" affordability

**Phase:** — (beyond-roadmap; extends Sprint 16 cash-flow planning and realises the "free cash / can I spend today" idea behind the app — see `docs/architecture/07-financial-advisor.md`; the schema change follows `docs/architecture/02-database-and-encryption.md`)
**Status:** Planned
**Depends on:** sprint-9 (`Account`, `IAccountService`, import + `ImportSession` periods), sprint-16 (`CashFlowProjectionEngine`, `IRunningBalanceQuery`, `IStatementContinuityChecker`, `RecurringFlow`, the cash-flow planning page + `GetCashFlowProjection` tool + `CashFlowExplainer`), sprint-12 (chat tool-calling: `ChatTool` base, budget gate, usage ledger), sprint-15 (i18n — every new string is a resource key)

## Goal

The owner can ask **"can I spend 2000 zł today?"** and get a grounded yes/no that is based on the *real* account balance (not a relative running sum), projects the known recurring outflows and inflows forward — leasing instalment, taxes, next salary — subtracts a rough allowance for ordinary day-to-day spending, and respects a personal safety floor. The answer is computed deterministically by an `AffordabilityEngine`; the assistant (and a panel on the planning page) only narrates it. When the imported history has a gap between the balance anchor and today, the answer is explicitly flagged **uncertain** rather than being silently wrong.

## Why this sprint exists

Sprint 16 built the deterministic cash-flow spine — active `RecurringFlow`s expanded over a horizon into a dated running-balance timeline with a lowest point and tight-window warnings. But two things stop it from answering the app's *core* question:

1. **The balance is relative, not real.** `RunningBalanceQuery` (doc-commented "the running sum of every PLN transaction up to and including the as-of date") sums imported transactions with no anchor and **across all accounts**. Unless the entire history from a zero start is imported, the absolute level is offset by whatever the account held before the first import — so "you have X, you can spend Y" is not trustworthy. Per-account reconciled-balance anchoring was explicitly deferred in Sprint 16.
2. **There is no affordability primitive.** The engine answers "where is my lowest point"; nothing answers "if I take 2000 out today, does the lowest point before my next inflow stay above my safety floor?". The `tightFloor` parameter exists on the engine but every caller passes the default `0`, and ordinary variable spending between now and payday is not modelled at all (deferred "variable-spend daily-burn overlay"), which makes the projection optimistic in exactly the window the question is about.

This sprint closes both gaps and ties them together into the one question the software exists to answer.

## Design decisions (the shape we commit to)

- **The balance anchor is `(AnchorDate, AnchorBalance)` on `Account`, set manually by the owner.** The owner enters the real balance as of a date (e.g. `2025-01-01`, `4 210,55 zł`); from then on `balance_as_of(D) = AnchorBalance + Σ(transactions on that account after AnchorDate up to D)`. Confirmed with the owner: set once, derive forward from statements; import monthly and it self-updates. Both fields are **nullable** (a migration with a mandatory `pre-migration-backup`, rule #8) — when unset, the balance stays the legacy relative running sum and any affordability answer is flagged *relative / set an anchor for an absolute answer*.
- **Balance becomes per-account.** `IRunningBalanceQuery` gains an account scope. The cash-flow planning page and the affordability question both operate on **one chosen account** (the account that actually carries the salary/leasing/taxes), not a cross-account blend. Combined-all-accounts view is out of scope for v1.
- **Continuity gates trust, it does not block.** `IStatementContinuityChecker` already finds per-account gaps. If a gap sits between the anchor date and the as-of date, the derived balance is untrustworthy → the affordability result carries an `IsUncertain` flag with the offending range, and the UI/assistant say "I can't be sure — statement days A–B are missing" instead of a confident number. It never fabricates a balance.
- **The safety floor is the owner's buffer, wired through everywhere.** A configurable floor (default e.g. `0`, owner-set in Ustawienia) is threaded into the engine, the planning VM, and the chat tool — replacing the hard-coded `0`. "Tight" then means "below your buffer", not "below zero".
- **Variable spend is a rough deterministic burn, not a forecast.** Ordinary discretionary spending in the window from today to the next inflow is estimated as an average daily burn from recent history (trailing N months, excluding amounts already modelled as `RecurringFlow`s so nothing is double-counted). It is labelled an *estimate* and shown separately — it makes the answer conservative rather than optimistic. No ML, no per-category modelling in v1.
- **Engine calculates, AI explains (the Sprint-14 rule holds).** Every number — projected low point, headroom, the payment that pushes you under — comes from deterministic C# in `Coffer.Core`. The `CanIAfford` chat tool and the planning-page panel only render/narrate the engine's output. The assistant never invents a figure.
- **No new AI spend by default.** Affordability itself is pure arithmetic — it makes **zero** API calls. Only the optional prose narration (reuse of the existing `CashFlowExplainer` path) costs anything, and only when the owner asks for it, behind the existing budget gate.

## Approach — three PRs (engine/data → affordability → UI)

- **18-A — balance anchor + per-account anchored balance + continuity trust (headless).** The `Account.AnchorDate`/`AnchorBalance` migration (with pre-migration-backup), `IRunningBalanceQuery` made per-account and anchor-aware, and a small "is this balance trustworthy as of D?" signal built on the existing continuity checker. Unit + integration tests; no pixels.
- **18-B — affordability engine + safety floor + variable burn + chat tool (headless).** The deterministic `AffordabilityEngine` in `Coffer.Core`, the owner safety-floor setting threaded through, the variable-burn query, and the `CanIAfford` chat tool so the assistant can answer directly. Unit tests with golden-style fixtures.
- **18-C — UI.** Anchor editing on the account (set/adjust the real balance + date), a **dedicated "Can I afford?" page** (its own nav entry) with an account selector + amount + optional date → verdict, headroom, the payment that pushes you under, uncertainty/relative warnings, and the safety-floor field in Ustawienia. Fully localized (keys in both `.resx`, parity test green).

## Steps

### 18-A — balance anchor + per-account anchored balance + continuity trust

- [x] 18.1 Add `AnchorDate` (`DateOnly?`, rule #2) and `AnchorBalance` (`decimal?`, rule #1) to `Account` + its EF configuration (money `decimal(18,2)`). Currency already lives on `Account` (rule #9).
- [x] 18.2 EF migration adding the two nullable columns. The `pre-migration-backup` callback runs first (rule #8) — verify it fires in a migration integration test.
- [x] 18.3 `IRunningBalanceQuery.GetBalanceAsOfAsync` gains an `accountId` scope and becomes anchor-aware: when the account has an anchor, `AnchorBalance + Σ(amount where AccountId == account && Date > AnchorDate && Date <= asOf)`; when unset, the legacy per-account running sum (so existing behaviour is preserved, just narrowed to one account). Keep it server-side (`SUM`, `AsNoTracking`).
- [x] 18.4 A balance-trust signal: extend/consume `IStatementContinuityChecker` to answer "is the [anchorDate|firstTxn, asOf] window contiguous for this account?" — return the offending `StatementGap`s if not. Small `IBalanceTrust`/method rather than overloading the query result shape; keep `Coffer.Core` presentation-free.
- [x] 18.5 Update the cash-flow planning VM + `GetCashFlowProjection` tool call sites that seed the opening balance to pass the chosen account (the planning page today blends all accounts). Behaviour with a single account is unchanged; the multi-account path now resolves to one account.
- [x] 18.6 Tests (`Coffer.Infrastructure.Tests`): anchored balance = anchor + post-anchor delta for a chosen account; unanchored falls back to the per-account running sum; a second account's transactions never leak into the first's balance; migration integration test proves the backup callback ran and the columns exist; continuity trust flags a window with a seeded gap and passes a contiguous one.

### 18-B — affordability engine + safety floor + variable burn + chat tool

- [x] 18.7 `AffordabilityEngine` in `Coffer.Core/Planning/`: pure/deterministic. Input: proposed spend amount, spend date (default today), opening balance (from 18-A), active `RecurringFlow`s, an estimated daily variable burn, a safety floor, and the balance-trust signal. It projects from `spendDate` to the **next inflow** (and at least to the next big outflow), applies the proposed spend as a same-day outflow, and returns an `AffordabilityVerdict`: `CanAfford` (bool), `LowestBalance` + date, `Headroom` (lowest − floor), the single `RecurringFlow` that drives the low point ("what pushes you under"), `IsUncertain` + gap range, and `IsRelative` (no anchor set). Reuse `CashFlowProjectionEngine` internally rather than re-deriving the timeline.
- [x] 18.8 Owner safety-floor setting: add `GetSafetyFloorPln`/`SetSafetyFloorPln` to `IAiSettings`-style settings (or a dedicated planning-settings surface) backed by `AppSettingsStore` (no migration — the key/value table exists), default via a `*Defaults` constant. Thread it into the `CashFlowProjectionEngine.Project(..., tightFloor)` call sites that currently pass `0` (planning VM + `GetCashFlowProjection` tool) so "tight window" reflects the buffer everywhere.
- [x] 18.9 Variable-burn query (`Coffer.Infrastructure/Planning/`): average daily discretionary spend over a trailing window (e.g. 3 months), **excluding** transactions attributable to an active `RecurringFlow` (by merchant/category match) so recurring outflows are not counted twice. Server-side aggregation, per account. Returns `0` gracefully on thin history.
- [x] 18.10 `CanIAfford` chat tool (`Coffer.Infrastructure/Chat/`, extends `ChatTool`): params `amount` (required), `date?`, `accountId?`. Resolves the anchored balance, active flows, burn, and floor, runs the `AffordabilityEngine`, and returns the verdict as structured JSON (amounts PLN, dates RRRR-MM-DD) with the uncertainty/relative flags surfaced. **No AI call inside the tool** — it is arithmetic the assistant narrates. Register it alongside the other chat tools.
- [x] 18.11 Tests (`Coffer.Core.Tests` + `Coffer.Infrastructure.Tests`): engine says no when the spend drives the low point below the floor before the next salary, yes when headroom remains; the "what pushes you under" flow is identified; `IsUncertain` set when a gap sits in the window; `IsRelative` set when no anchor; burn excludes recurring-flow merchants; the chat tool shapes the verdict and makes no provider call. Deterministic fixtures (fixed dates/amounts), in the "engine calculates" spirit — golden-style expected verdicts.

### 18-C — UI

- [x] 18.12 Account balance anchor editing: a "real balance as of date" set/adjust surface on the account (amount + date), wired through `IAccountService` (`GetAllWithAnchorsAsync` + `SetBalanceAnchorAsync`) to persist `AnchorDate`/`AnchorBalance`. Lives as a per-account card in Ustawienia (owner chose Settings over a page/dialog). Validates the date is not in the future; both fields set/cleared together.
- [x] 18.13 A **dedicated "Can I afford?" page** (own nav entry + `AffordabilityViewModel`): an account selector (incl. all-accounts), amount input + optional date → verdict (afford/not), headroom, the payment that pushes you under, and a clear banner when the balance is *uncertain* (gap) or *relative* (no anchor). Reads the `AffordabilityEngine` via the VM — mirrors `CanIAffordTool`, zero AI calls.
- [x] 18.14 Ustawienia: a safety-floor (buffer) field bound to `IPlanningSettings`. All strings via `{l:Localize}`, keys in **both** `.resx`.
- [x] 18.15 Tests (`Coffer.Application.Tests`): the affordability VM surfaces afford/not + headroom + uncertainty/relative flags + driver from a real engine over fakes; the settings VM round-trips the floor and the anchor (incl. future-date rejection + clear); resource-key parity stays green. Plus `Coffer.Infrastructure.Tests` `AccountServiceTests` round-trips the anchor through real SQLCipher EF.

### Sweep

- [x] 18.16 Resource-key parity holds (parity test green); no residual hardcoded user-facing literals in the new anchor/affordability/settings surfaces (all via `{l:Localize}`). Money still renders `pl-PL` "zł" via `CashFlowDisplay.Money` regardless of UI language.
- [ ] 18.17 Manual DoD click-through (below) — deferred to manual (needs a running desktop app + a real statement).

## Definition of Done

- **18-A (automated):** for an account with an anchor, `GetBalanceAsOfAsync` returns `anchor + post-anchor delta` and never blends other accounts; unanchored falls back to the per-account running sum; the migration ran a pre-migration backup; a seeded gap in the [anchor, asOf] window is reported as untrustworthy.
- **18-B (automated):** the `AffordabilityEngine` returns the correct yes/no + headroom + "what pushes you under" for fixed fixtures; `IsUncertain`/`IsRelative` set in the right conditions; the variable burn excludes recurring-flow merchants; the `CanIAfford` tool shapes the verdict and makes zero provider calls.
- **18-C (automated + manual):** the affordability VM surfaces the verdict and flags; the settings VM round-trips the floor; the account VM round-trips the anchor. **Manual:** set an account's real balance for `1 Jan`, import a statement covering `1–20`, open the "Can I afford?" page for that account, ask "can I afford 2000 today" → get a grounded verdict that accounts for the upcoming leasing/tax outflows and the next salary; leave a statement gap and confirm the answer turns *uncertain*; clear the anchor and confirm it turns *relative*. Ask the assistant the same question in chat and get the same numbers. Every label switches PL↔EN live; money shows "zł".
- **Whole-sprint:** the app answers "can I spend 2000 zł today?" from the real balance, the known recurring flows, a conservative daily-burn allowance, and a personal safety floor — deterministically, with honest uncertainty when the data has holes.

## Files affected

- `src/Coffer.Core/Domain/Account.cs` (+ `Infrastructure/Persistence/Configurations/AccountConfiguration.cs`, new migration)
- `src/Coffer.Core/Planning/IRunningBalanceQuery.cs` + `src/Coffer.Infrastructure/Planning/RunningBalanceQuery.cs` (per-account, anchor-aware)
- `src/Coffer.Core/Planning/IStatementContinuityChecker.cs` / a new balance-trust helper + `src/Coffer.Infrastructure/Planning/StatementContinuityChecker.cs`
- `src/Coffer.Core/Planning/AffordabilityEngine.cs` + `AffordabilityVerdict.cs` (new)
- `src/Coffer.Infrastructure/Planning/` variable-burn query (new)
- `src/Coffer.Infrastructure/Chat/CanIAffordTool.cs` (new) + DI registration
- `src/Coffer.Core/Ai/IAiSettings.cs` (or a planning-settings surface) + `src/Coffer.Infrastructure/AI/AppSettingsStore.cs` (safety-floor key) + `*Defaults`
- `src/Coffer.Application/ViewModels/Planning/CashFlowPlanningViewModel.cs` (per-account scope), a new `AffordabilityViewModel`, an account-anchor VM, `SettingsViewModel` (floor)
- `src/Coffer.Desktop/Views/**` (anchor editing, a new "Can I afford?" page + nav entry, settings floor)
- `src/Coffer.Application/Localization/Strings.resx` + `Strings.pl.resx` (new keys, parity)
- `tests/Coffer.Core.Tests/Planning/**`, `tests/Coffer.Infrastructure.Tests/{Planning,Chat}/**`, `tests/Coffer.Application.Tests/ViewModels/**`

## Open questions

To confirm with the owner before/while building (recorded as decisions in `log.md` once settled):

- **Anchor scope** → proposed `(AnchorDate, AnchorBalance)` as nullable fields on `Account`, set manually, one anchor per account (re-anchoring just overwrites). Confirm we do *not* need a history of anchors in v1.

Settled (see `log.md` 2026-07-01):

- **Variable burn** — single trailing-3-month average daily spend excluding recurring-flow merchants, shown as a labelled *estimate* (owner: OK for v1).
- **Safety floor scope** — one global buffer in Ustawienia, not per-account in v1 (owner: OK).
- **Where affordability lives** — a **dedicated "Can I afford?" page** with its own nav entry, not a panel on the planning page (owner: new page).

## Deferred to a follow-up

- **Combined all-accounts affordability** (blend across accounts / transfers between own accounts). v1 answers per chosen account.
- **Per-category or seasonal variable-spend modelling** and a daily-burn overlay on the projection chart itself.
- **Anchor auto-reconciliation** from a statement's own stated opening/closing balance (PKO CSV does not carry it reliably; would be per-parser).
- **Multi-currency affordability** — v1 is PLN, mirroring the rest of the planner.
