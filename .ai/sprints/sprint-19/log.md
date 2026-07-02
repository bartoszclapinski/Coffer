# Sprint 19 log

## 2026-07-02

- `--:--` sprint planned — spending explorer: selectable-window category breakdown + merchant drill-down. Beyond-roadmap; deepens the Phase-6 dashboard from fixed overview into an interactive analysis surface, and finally surfaces `Transaction.Merchant` as a spending dimension. No migration (data already imported), no AI (pure read-side SQL). Two implementation PRs planned: 19-A (`ISpendingExplorerQuery` + `SpendingExplorerViewModel`, headless) → 19-B (Avalonia "Wydatki / Spending" page + nav + i18n).
- `--:--` scope decision: **core only** for this sprint — selectable window + category→merchant→transaction drill-down. Period-over-period delta, a `GetSpendingByMerchant` chat tool, and charts are explicitly deferred to keep the PRs digestible.
