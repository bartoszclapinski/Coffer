# Sprint 29 log

## 2026-07-10

- `--:--` decision (owner): after Sprint 28 (foundation + shell + Overview), migrate **every remaining screen** to the design system so the whole app is cohesive for a demo video ("rób po kolei wszystko, jak skończymy to nagram"). Record in **either** theme → migrate for **both** (default flips to Dark at the end). The new `Accounts` screen is deferred (it's a feature, not a reskin).
- `--:--` plan written (`sprint-29.md`), Status: Planned. Five grouped PRs by screen family: 29-A ledgers (Transactions + Spending) → 29-B budgets & forecast → 29-C advisor & planning → 29-D import/alerts/chat → 29-E Settings + pre-login windows + flip default to Dark. Each screen validated in both themes via env-var preview harnesses (no login/DB). Pure reskin — no VM/data/schema/AI change. Awaiting plan-PR review before implementation.
