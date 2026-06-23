# Sprint 13 log

## 2026-06-23

- Plan written (`chore/plan-sprint-13`). Sprint 13 = Phase 8 (Anomalies and alerts): a desktop
  **Alerty** page that surfaces unusual financial activity after import — duplicate charges,
  out-of-pattern amounts, category spikes, new merchants, missing subscriptions — each with a clear
  Polish explanation the owner can accept or dismiss. Statistics detect; AI only writes commentary.
- Decisions (planning):
  - **Two PRs** ("deterministic before AI", Sprint-10/12 cadence): 13-A = domain `Alert` + the five
    statistical detectors + detection use case (templated Polish descriptions) + Alerty UI, no AI;
    13-B = `AnomalyCommentator` (LLM title/description for top-N, budget-gated, ledgered as
    `anomaly-comment`, anonymised) + the `FindAnomalies` chat tool deferred in Sprint 12.
  - **Detectors compute stats in-memory** from a baseline window (personal-scale data), matching the
    `IAnomalyDetector(recent, baseline)` shape in doc 04 — no separate SQL stats query.
  - **`anomaly-comment` ledger purpose already exists** (`AiDefaults`), so 13-B reuses it; no enum
    change. Reasoning-tier model (Sonnet) per doc 04.
  - **Mobile push notifications (roadmap Phase 8 bullet) are out of scope** — desktop-first; alerts
    surface in the Alerty list only.
- Grounding check (read-only survey): `Alert` follows the `AiUsageEntry` entity shape (class, Guid
  Id, UTC `DateTime`, decimal money); persistence uses external `IEntityTypeConfiguration<T>` +
  `dotnet ef migrations add`; new chat tools inherit `ChatTool` (`RunAsync(JsonElement, db, ct)`) and
  register `AddTransient<IChatTool, T>()`; section UI follows the Dashboard/Transactions VM+View+DI
  pattern. Confirmed `AiPurpose.AnomalyComment` and the `ChatTool` base directly in source.
- Planning questions resolved (owner, 2026-06-23):
  - **Recent window = rolling last 30 days** (not calendar-month); baseline = prior 6 months.
  - **Dedup by `Signature`**; rescan never duplicates and a `Dismissed` alert stays dismissed.
  - **Detection trigger = post-import + manual rescan**, no background scheduler in v1.
  - **AI commentary covers the top 10** candidates per run; the rest keep templated text.
  - **z-score detectors require ≥ 8 baseline transactions** in a category before firing.
  - **`DuplicatePayment` window = same or adjacent day** (transactions are `DateOnly`).
