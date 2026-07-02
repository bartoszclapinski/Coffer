# Sprint 20 log

## 2026-07-02

- `--:--` sprint planned — category budgets with mid-month tracking (budget zones). Beyond-roadmap; the owner picked it over what-if runway, spending-explorer polish, and backup/recovery. Sits on Sprint-19's per-category debit-sum aggregation, Sprint-11's current-month anchor, and (optionally) Sprint-13's alert engine. A `CategoryBudget` entity (named to stay distinct from the `AiBudgetGate` cost cap) + a pure `BudgetTrackingEngine` + a page. Two committed PRs: 20-A (entity + backed-up migration + engine + query, headless) → 20-B (Budgets page + nav + i18n); an optional 20-C (over-budget alert detector + `GetBudgetStatus` chat tool) only if the first two land comfortably.
- `--:--` decision: **calendar-month, linear projection, zones at 80% / 100%-or-projected-over**; one recurring monthly limit per (category, currency), no per-month override or rollover in v1; uncategorised month spend shown as an unbudgeted line so overspending can't hide. Engine calculates in `Coffer.Core`; the query only assembles inputs. No AI, no new cost. The `CategoryBudgets` migration runs `pre-migration-backup` (hard rule #8) — the first migration since Sprint 18.
