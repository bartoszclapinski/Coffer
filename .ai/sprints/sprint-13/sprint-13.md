# Sprint 13 — Anomalies and alerts

**Phase:** 8
**Status:** Planned
**Depends on:** sprint-9 (transactions), sprint-10 (categories + AI plumbing: ledger/budget gate/anonymizer), sprint-11 (server-side aggregation patterns), sprint-12 (chat tools registry)

## Goal

After an import (or a manual rescan) Coffer surfaces unusual financial activity on a desktop **Alerty** page — duplicate charges, out-of-pattern amounts, category spikes, new merchants, missing subscriptions — each with a clear Polish explanation the owner can **accept** ("to normalne") or **dismiss**. Detection is statistics-first; AI only writes the human-readable commentary.

## Approach — two PRs (Sprint-10/12 cadence, "deterministic before AI")

- **13-A — detection engine + alerts UI (no AI).** Domain `Alert` + the five statistical detectors + the detection use case (persisting alerts with deterministic **templated** Polish descriptions) + the Avalonia Alerty page wired into the shell. Fully unit-testable with synthetic transactions; ships a usable feature with zero AI cost.
- **13-B — AI commentary + chat integration.** An `AnomalyCommentator` replaces the templated text of the top-N candidates with LLM-written title/description (Sonnet, budget-gated, ledgered as `anomaly-comment`, anonymised), and a `FindAnomalies` chat tool slots into the Phase-7 registry so the assistant can answer "pokaż anomalie".

Detectors compute their own per-category stats in-memory from a baseline window (personal-scale data, thousands of rows) — no separate SQL stats query, matching the `IAnomalyDetector(recent, baseline)` shape in doc 04. **Windows:** recent = rolling **last 30 days**, baseline = **prior 6 months**. AI commentary covers the **top 10** candidates per run; the rest keep templated text. z-score detectors require **≥ 8** baseline transactions in a category before firing. `DuplicatePayment` treats same-or-adjacent `Date`/`BookingDate` as "within 24h" (transactions are `DateOnly`).

## Steps

### 13-A — detection engine + alerts UI

- [ ] 13.1 Domain (`Coffer.Core/Anomalies/`): `AnomalyType` enum (`HighAmountInCategory`, `NewMerchant`, `CategorySpike`, `DuplicatePayment`, `MissingRecurrence`), `AnomalyCandidate` transient record (`Type`, `Score`, `RelatedTransactionId?`, raw-number context), `AlertStatus` enum (`New`, `Acknowledged`, `Dismissed`).
- [ ] 13.2 Domain entity `Alert` (`Coffer.Core/Domain/Alert.cs`): `Guid Id`, `DateTime DetectedAt` (UTC), `AnomalyType Type` (stored as string), `string Title`, `string Description`, `AlertStatus Status`, `decimal? RelatedAmount`, `Guid? RelatedTransactionId`, `DateOnly PeriodFrom`/`PeriodTo`, `string Signature` (dedup key), `DateTime? ResolvedAt`.
- [ ] 13.3 Persistence: `AlertConfiguration : IEntityTypeConfiguration<Alert>` (precision 18,2 on amount, **unique index on `Signature`** for rescan idempotency, index on `Status`+`DetectedAt`), `DbSet<Alert> Alerts` on `CofferDbContext`, `ApplyConfiguration`. Migration `AddAnomalyAlerts` (pre-migration backup runs automatically — hard rule #8).
- [ ] 13.4 `IAnomalyDetector` (`DetectAsync(recent, baseline, ct) → IAsyncEnumerable<AnomalyCandidate>`) + five detectors in `Infrastructure/Anomalies/`:
  - [ ] 13.4.a `HighAmountInCategoryDetector` — z-score > 3 on amount within category; skip categories with < 8 baseline transactions.
  - [ ] 13.4.b `NewMerchantDetector` — merchant absent from the baseline window.
  - [ ] 13.4.c `CategorySpikeDetector` — recent 30-day category total > 2σ above the 6-month monthly average (same ≥ 8 baseline-sample guardrail).
  - [ ] 13.4.d `DuplicatePaymentDetector` — same merchant + same amount on the same or adjacent `Date`/`BookingDate`.
  - [ ] 13.4.e `MissingRecurrenceDetector` — a merchant recurring in prior months absent this month (candidate has no `RelatedTransactionId` — references merchant/category).
- [ ] 13.5 `IDetectAnomaliesUseCase` + `AnomalyDetectionService` (`Infrastructure/Anomalies/`): load recent (last 30 days) + baseline (prior 6 months) windows, run all detectors, rank by score, map to `Alert` rows with **templated Polish descriptions**, compute `Signature`, upsert (skip existing signatures; never resurrect a `Dismissed` alert), save.
- [ ] 13.6 Trigger: invoke detection after a successful import (hook in the import pipeline) and expose a manual rescan command. No background scheduler in v1.
- [ ] 13.7 Read/command side: `IAlertsQuery` (new + history) and `IAlertService` (`AcknowledgeAsync`/`DismissAsync` → status + `ResolvedAt`), implementations in `Infrastructure/Anomalies/`, registered via a new `AddCofferAnomalies()` in `ServiceRegistration`.
- [ ] 13.8 UI: `AlertsViewModel` + `AlertRowViewModel` (`Coffer.Application/ViewModels/Alerts/`) with `LoadAsync`, `RescanCommand`, `AcceptCommand`, `DismissCommand`, empty/loading/error states; `AlertsView.axaml` (type badge, title, description, date, Accept/Dismiss) matching dashboard design language.
- [ ] 13.9 Shell wiring: `Alerty` sidebar entry + `IsAlertsActive`/`ShowAlerts` in `MainViewModel`, `DataTemplate` + nav button in `MainWindow.axaml`, transient DI in `DesktopServiceRegistration`.
- [ ] 13.10 Tests: a focused unit test per detector over synthetic transactions (Bogus/FsCheck), an integration test (import → detection persists the expected alerts, rescan adds none), and `AlertsViewModel` tests with fakes.

### 13-B — AI commentary + chat integration

- [x] 13.11 `IAnomalyCommentator` + `AnomalyCommentator` (`Infrastructure/AI/`): for the top 10 candidates, build an **anonymised** prompt (hard rule #7), call the provider (`AiDefaults` reasoning model), gate via `IAiBudgetGate` (`AiPriority.Normal`), meter via `IAiUsageLedger.RecordAsync(usage, AiPurpose.AnomalyComment)`, parse `{title, description}` JSON. On any failure keep the 13-A templated text (graceful fallback).
- [x] 13.12 Wire the commentator into `AnomalyDetectionService` so persisted top-N alerts carry LLM text; below-cap / over-budget / offline paths fall back to templates.
- [x] 13.13 `FindAnomaliesTool : ChatTool` (`Infrastructure/Chat/`) — params `{from, to}` (no `category`: `Alert` has no category dimension), returns alert rows in range; register `AddTransient<IChatTool, FindAnomaliesTool>()` in `AddCofferChat`. Realises the `FindAnomalies` tool deferred in Sprint 12.
- [x] 13.14 Tests: `AnomalyCommentator` with a scripted fake provider (asserts anonymised prompt, ledger metered as `anomaly-comment`, budget gate blocks over cap, fallback on bad JSON); `FindAnomaliesTool` over a real SQLCipher DB; a DI-registration case proving the new tool is discoverable by `ChatService`.

## Definition of Done

- **13-A (automated):** each detector has a unit test that fires on a planted anomaly and stays silent on clean data; an integration test imports a statement containing a duplicated charge and asserts a `DuplicatePayment` alert is persisted, and that a second rescan adds zero new alerts.
- **13-A (manual):** import a statement with a duplicated charge → an alert appears on the **Alerty** page with a clear templated Polish explanation; **Accept** and **Dismiss** update it and it does not reappear on rescan.
- **13-B (automated):** the commentator meters exactly one `anomaly-comment` ledger entry per call and falls back to templated text when the provider errors; the `FindAnomalies` tool returns alerts and is discoverable by `ChatService`.
- **13-B (manual):** ask the assistant "pokaż anomalie z tego miesiąca" → it invokes `FindAnomalies` (visible in the tool-trace) and the alert descriptions read as LLM-written Polish.

## Files affected

- `src/Coffer.Core/Anomalies/` (new): `AnomalyType.cs`, `AnomalyCandidate.cs`, `AlertStatus.cs`, `IAnomalyDetector.cs`, `IDetectAnomaliesUseCase.cs`, `IAlertsQuery.cs`, `IAlertService.cs`
- `src/Coffer.Core/Domain/Alert.cs` (new)
- `src/Coffer.Infrastructure/Persistence/CofferDbContext.cs`, `Configurations/AlertConfiguration.cs` (new), `Migrations/*_AddAnomalyAlerts.*` (new)
- `src/Coffer.Infrastructure/Anomalies/` (new): the five detectors, `AnomalyDetectionService.cs`, `AlertsQuery.cs`, `AlertService.cs`
- `src/Coffer.Infrastructure/AI/AnomalyCommentator.cs` (new, 13-B)
- `src/Coffer.Infrastructure/Chat/FindAnomaliesTool.cs` (new, 13-B)
- `src/Coffer.Infrastructure/DependencyInjection/ServiceRegistration.cs` (`AddCofferAnomalies`, register tool), import pipeline hook
- `src/Coffer.Application/ViewModels/Alerts/` (new): `AlertsViewModel.cs`, `AlertRowViewModel.cs`
- `src/Coffer.Application/ViewModels/Main/MainViewModel.cs`
- `src/Coffer.Desktop/Views/AlertsView.axaml(.cs)` (new), `MainWindow.axaml`, `DependencyInjection/DesktopServiceRegistration.cs`
- `tests/Coffer.Infrastructure.Tests/Anomalies/`, `tests/Coffer.Application.Tests/ViewModels/Alerts/` (new)

## Open questions

All planning questions resolved 2026-06-23 (recorded as decisions in `log.md`): recent = rolling 30 days; baseline = prior 6 months; dedup by `Signature`, dismissed stays dismissed; trigger = post-import + manual rescan (no scheduler); AI commentary on top 10/run; z-score guardrail = ≥ 8 baseline samples; `DuplicatePayment` = same/adjacent day. None open.
