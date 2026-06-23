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

## 2026-06-23 — 13-A implemented

- **Domain** (`Coffer.Core/Anomalies`): `AnomalyType`, `AlertStatus`, the detector contracts
  (`TransactionSnapshot`, `AnomalyDetectionContext`, `AnomalyCandidate`, `IAnomalyDetector`),
  the use-case/query/service ports (`IDetectAnomaliesUseCase`, `IAlertsQuery`, `IAlertService`,
  `AlertListItem`), and the persisted `Alert` entity. Money stays `decimal`; transaction-date
  window is `DateOnly`; system timestamps are UTC `DateTime` (hard rules #1/#2).
- **Persistence**: `AlertConfiguration` (enums as strings, unique `Signature`, composite
  `(Status, DetectedAt)` index), `DbSet<Alert>`, migration `AddAnomalyAlerts`.
- **Detectors** (`Infrastructure/Anomalies/Detectors`, pure + synchronous): HighAmountInCategory
  (z>3, ≥8 baseline), NewMerchant, CategorySpike (>2σ monthly, ≥3 months), DuplicatePayment
  (same/adjacent day), MissingRecurrence (≥3 baseline months, absent from recent). Shared
  `AnomalyThresholds`/`AnomalyStatistics` (sample stddev)/`AnomalyFormatting` (Polish templates).
- **Detection service**: `AnomalyDetectionService` anchors the recent window on the latest
  transaction date (last 30 days), baseline = prior 6 months, PLN-scoped; deduplicates candidates
  by `Signature` against existing alerts of **any** status (dismissed never resurrected),
  inserts only new ones. `AlertsQuery`/`AlertService` back the list and ack/dismiss. New
  `AddCofferAnomalies()` registers the five detectors + the three services.
- **UI**: `AlertsViewModel`/`AlertRowViewModel` + `AlertsView`, wired into the shell (Alerty nav
  button between Asystent and Ustawienia, DataTemplate, DI). Per-card Potwierdź/Odrzuć.
- **Deviation from plan step ~13.6**: detection is triggered on **Alerty page load + manual
  rescan**, *not* coupled into `ImportStatementUseCase`'s constructor. Rationale: the use case has
  many test call-sites and the scan is idempotent (signature dedup), so page-load is the simpler,
  equally-correct trigger. User-visible DoD is unchanged: import → open Alerty → alerts appear.
- **Detectors are `public`** (not `internal`): the repo has no `InternalsVisibleTo`, and other
  directly-tested implementation classes (parsers, services) are public — kept consistent.
- **Tests**: per-detector unit tests + an integration test over a real SQLCipher DB
  (persist / idempotent rescan / dismissed-never-resurrected) + `AlertsViewModel` tests;
  `MainViewModelTests` updated for the new shell page. Full suite green (350), format clean.
