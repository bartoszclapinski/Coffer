# 10 — Roadmap

## Philosophy

- **Each phase delivers something usable.** No "build for 3 months, see nothing run."
- **Hardest first.** PDF parser is the highest-risk component; build it before depending on it.
- **Deterministic before AI.** Get manual workflows working, then add AI conveniences on top.
- **Test the security primitives early.** Master password, BIP39, encryption — these can't be retrofitted safely.

## Phase 0 — Foundation (1–2 weekends)

**Goal:** A solution that builds, runs, and has all infrastructure abstractions in place.

**First commit (literally before anything else):**
- [ ] `git init`, push to public GitHub repo `Coffer`
- [ ] `.github/workflows/build.yml` (already in this package) — verify it runs green on the empty repo
- [ ] README badge linking to the workflow status

**Then:**
- [ ] Solution: `Coffer.sln` with all projects from `01-stack-and-projects.md`
- [ ] DI configured in both Desktop and Mobile entry points
- [ ] EF Core 9 + SQLite + SQLCipher with `SqlCipherKeyInterceptor`
- [ ] First migration creates empty schema with `_SchemaInfo` table
- [ ] `IKeyVault` with `WindowsDpapiKeyVault` and `MauiSecureStorageKeyVault` stubs
- [ ] Setup wizard: master password creation + Argon2id + BIP39 seed display + verification
- [ ] First `MainWindow` (Avalonia) and first MAUI page that show "logged in as: [user]"
- [ ] `.editorconfig`, `.gitignore`, `README.md`

**Definition of done:** Cold start asks for master password; correct password shows the empty main window; restart within 7 days bypasses password (DPAPI cache); clear cache forces password again. Same on mobile (with `SecureStorage`).

## Phase 1 — Statement parser for PKO BP (2–3 weekendy)

**Goal:** A parser that handles real PKO statements with full test coverage. No UI yet.

- [ ] `IStatementParser`, `IBankDetector`, `StatementParserRegistry`, `ParseResult` interfaces
- [ ] PdfPig helpers: row grouping, column detection, amount/date parsing for Polish format
- [ ] `FingerprintBankDetector` with PKO BP fingerprint
- [ ] `PkoBpStatementParser` for at least the standard checking account layout
- [ ] `tools/Anonymizer/` CLI: takes a real PDF, outputs anonymized PDF + expected JSON
- [ ] 5+ golden file tests covering different PKO layouts
- [ ] Property-based tests for amount/date parsers (FsCheck)
- [ ] `AiAssistedParser` fallback (Claude Sonnet 4.6, JSON mode)
- [ ] `Transaction.Hash` deterministic hashing for dedup

**Definition of done:** Run a unit test that parses an anonymized real PKO PDF and asserts the resulting transactions match an expected JSON file. AI fallback handles any other bank's PDF acceptably.

## Phase 2 — Import flow + transaction list (1–2 weekendy)

**Goal:** Owner imports a real statement and sees the data on desktop.

- [ ] `ImportStatementUseCase` (Application layer): parse → dedup → save with `ImportSession`
- [ ] Avalonia: drag-and-drop area for PDFs on the Import page
- [ ] Progress UI showing the 5-step pipeline (file read, bank detected, parsed, dedup, saved)
- [ ] Avalonia: Transactions list page with default 6-month window, filters (search, category, account)
- [ ] Mobile: Transactions list (read-only on this phase)
- [ ] EF indexes per `02-database-and-encryption.md`

**Definition of done:** Import a real statement, see all transactions in the list, filter by search/category. Import the same statement again — no duplicates added.

## Phase 3 — Sync via Google Drive (1 weekend)

**Goal:** Two devices share data through the user's Drive.

- [ ] OAuth2 flow with `Google.Apis.Drive.v3` (loopback redirect)
- [ ] Refresh token encrypted via `IKeyVault`
- [ ] `SyncEvent` table; every domain change writes a sync event in the same DB transaction
- [ ] `SyncWorker` background service: push, pull, apply
- [ ] Field-level last-write-wins conflict resolution
- [ ] Initial sync flow on a fresh second device

**Definition of done:** Manually add a transaction on desktop. Within 60 seconds, mobile shows it. Edit the transaction's category on mobile. Within 60 seconds, desktop reflects the new category.

## Phase 4 — Auto-categorization (1 weekend)

**Goal:** New transactions get categorized automatically with minimal AI cost.

- [ ] `Category`, `Rule`, `CategoryCache` tables
- [ ] `RuleEngine` regex matcher
- [ ] `HybridCategorizer`: cache → rules → AI batch
- [ ] `IAiProvider` with `ClaudeProvider` and `OpenAiProvider` implementations
- [ ] Prompt anonymization (`PromptAnonymizer`)
- [ ] Provider selection in Settings UI
- [ ] AI usage ledger (`AiUsageEntry`)
- [ ] Cost cap enforcement (`AiBudgetGate`)

**Definition of done:** First import of 200 transactions categorizes 100% via batch; second import (same merchants) categorizes 95%+ from cache with no API calls.

## Phase 5 — Receipt capture and matching (2 weekendy)

**Goal:** Mobile captures receipts; desktop auto-matches them.

- [ ] `Receipt`, `ReceiptItem`, `TransactionSplit` tables
- [ ] `MauiReceiptCamera` capture flow
- [ ] `ReceiptImagePreprocessor` (resize, JPEG re-encode)
- [ ] `ClaudeVisionReceiptOcr` + per-item categorization
- [ ] `ReceiptMatcher` with weighted scoring (amount, date, merchant fuzzy)
- [ ] Auto-link at score ≥ 0.95; manual queue for 0.6–0.95
- [ ] Encrypted receipt image storage (local) and Drive sync
- [ ] Desktop: receipt drill-down in transaction list
- [ ] Mobile: receipts list with status badges

**Definition of done:** Photograph a Lidl receipt on mobile. Within 60s desktop sees it. Import a statement that includes the matching Lidl charge. The transaction shows the receipt icon and split items with categories.

## Phase 6 — Dashboard and charts (1 weekend)

**Goal:** Owner sees the visual overview.

- [ ] Avalonia: Dashboard page with KPI cards, top categories, recent transactions
- [ ] LiveCharts2 charts: 30-day spending trend, monthly bar chart, category doughnut
- [ ] All aggregations in SQL (`GROUP BY` server-side)
- [ ] Mobile: simplified home with balance + last 5 transactions

**Definition of done:** Open the app, see the current month's KPIs, top categories, and a working spend-over-time chart for the user's actual data.

## Phase 7 — Chat with data (1–2 weekendy)

**Goal:** Owner asks questions in natural language and gets accurate answers.

- [ ] Tool-calling abstractions over `IAiProvider`
- [ ] Tool implementations: `GetTotalSpent`, `GetTransactions`, `GetSpendingByCategory`, `GetMonthlyTrend`, `FindAnomalies`, `GetGoals`, `GetReceiptItems`
- [ ] Avalonia chat UI: message list, input box, suggested prompts, tool-trace panel
- [ ] System prompt + per-conversation context
- [ ] Cost ledger entries per chat turn

**Definition of done:** Ask "ile wydałem na paliwo w listopadzie" — the model invokes `GetSpendingByCategory`, returns an answer with the actual number, and the tool-trace panel shows the call.

## Phase 8 — Anomalies and alerts (1 weekend)

**Goal:** Owner is notified of unusual financial activity.

- [ ] Statistical detectors: high-amount-in-category (z-score), new-merchant, category-spike, duplicate-payment, missing-recurrence
- [ ] LLM commentary for top candidates
- [ ] `Alert` table; UI list of alerts with "accept / it's normal" actions
- [ ] Mobile push notifications (MAUI native APIs) for critical alerts

**Definition of done:** Import a statement with a duplicate Anthropic charge; an alert appears with a clear explanation. Accept it (e.g., "raise dispute") or dismiss as normal.

## Phase 9 — Financial advisor (2–3 weekendy)

**Goal:** Goal-tracking with feasibility and AI-assisted suggestions.

- [ ] `Goal`, `GoalContribution`, `GoalSnapshot` tables
- [ ] `GoalFeasibilityEngine` with strategies for all 6 goal types
- [ ] Daily snapshot job
- [ ] Avalonia: Advisor page with goals list, detail panel, simulator slider, scenarios, 12-month projection chart
- [ ] AI risks and savings-suggestions generator (per `07-financial-advisor.md`)
- [ ] Goal-transaction linking via tags
- [ ] Mortgage prepayment calculator

**Definition of done:** Create a "Vacation Greece — 8000 zł by July 2026" goal. Engine reports realistic projection. AI suggests 2–3 specific cuts grounded in the user's actual category history. Apply a suggestion; goal projection updates immediately.

## Beyond the roadmap

Things to consider after Phase 9:

- Multi-currency display (PLN + EUR for travel/foreign-currency accounts)
- Forecasting next month's expenses based on recurring patterns
- "What-if" scenarios beyond goal slider (e.g., what if I lose my job for 3 months)
- Budget zones per category with mid-month tracking
- Tax-relevant transaction tagging (PIT-37 helper, NOT tax advice)
- Family member account (read-only sub-user with their own categories) — NOT v1, requires deeper privacy model
- Public release as open-source — would require thorough security audit, license decisions, GDPR compliance work

These are explicitly out of scope for the personal-use phase.

## Time estimates

Total: 13–18 weekend days for full v1. Solo developer working evenings + weekends:

- Aggressive pace (2 weekend days/week, 6 hours each): ~8–12 weeks
- Realistic pace (1 weekend day/week, 5 hours): ~14–20 weeks
- First usable MVP (Phases 0–4): 3–6 weeks

Adjust on contact with reality. Each phase ends with a usable app, so it's safe to pause indefinitely between phases.
