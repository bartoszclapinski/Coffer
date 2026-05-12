# Coffer

Personal finance application with AI integration. Cross-platform (.NET 9 + Avalonia desktop + MAUI mobile). Mobile-first for receipt capture, desktop-first for analysis and statement imports.

## Quick orientation

- **Owner:** solo developer building for personal use
- **Languages of data:** Polish (transactions, statements, UI labels)
- **Languages of code:** English (variables, methods, comments, docs)
- **Privacy posture:** maximum — local-first, end-to-end encrypted, no telemetry

## Bieżąca praca — najpierw to

Praca jest zorganizowana w sprinty. **Zanim cokolwiek zrobisz**, sprawdź `.ai/sprints/index.md` (który sprint jest w toku) i przeczytaj `.ai/sprints/sprint-N/sprint-N.md` (plan) + `log.md` (postęp). Konwencja sprintów: `.ai/sprints/README.md`.

## Tech stack

- .NET 9, C# 13
- Avalonia 11 (desktop: Windows primary, Linux/macOS supported)
- .NET MAUI (mobile: Android + iOS)
- EF Core 9 + SQLite + SQLCipher for encryption at rest
- CommunityToolkit.Mvvm for MVVM (source generators)
- PdfPig for PDF parsing (Apache 2.0)
- Anthropic.SDK + OpenAI SDK + Microsoft.Extensions.AI abstraction
- LiveChartsCore.SkiaSharpView for charts
- xUnit + FluentAssertions + Bogus + FsCheck for tests
- Serilog for logging

## Project layout

```
Coffer.sln
├── src/
│   ├── Coffer.Core/              # Domain entities, interfaces, business rules. NO framework dependencies.
│   ├── Coffer.Infrastructure/    # EF, parsers, AI providers, file I/O, secure storage. Talks to the world.
│   ├── Coffer.Application/       # Use cases, base ViewModels, shared between Desktop and Mobile.
│   ├── Coffer.Desktop/           # Avalonia app — Windows primary.
│   ├── Coffer.Mobile/            # MAUI app — Android + iOS.
│   └── Coffer.Shared/            # DTOs and primitives used across all layers.
├── tests/
│   ├── Coffer.Core.Tests/
│   ├── Coffer.Infrastructure.Tests/
│   └── Coffer.Application.Tests/
├── tools/
│   └── Anonymizer/                 # CLI tool to anonymize bank statement PDFs for golden file tests.
└── docs/
    └── architecture/               # Detailed design docs — see "Where to look" below.
```

## Where to look — task-to-doc routing

When working on a task, read the relevant architecture doc(s) FIRST before writing code:

| Task involves... | Read |
|---|---|
| Stack decisions, project boundaries, DI setup | `docs/architecture/01-stack-and-projects.md` |
| Database schema, migrations, encryption, queries | `docs/architecture/02-database-and-encryption.md` |
| PDF statement parsing, bank detection, AI fallback | `docs/architecture/03-statement-parsers.md` |
| Categorization, chat, anomaly detection, prompts | `docs/architecture/04-ai-strategy.md` |
| Receipt camera capture, OCR, transaction matching | `docs/architecture/05-receipt-pipeline.md` |
| Multi-device sync, mobile companion specifics | `docs/architecture/06-sync-and-mobile.md` |
| Goals, feasibility engine, advisor logic | `docs/architecture/07-financial-advisor.md` |
| Backups, restore, disaster recovery | `docs/architecture/08-backup-and-recovery.md` |
| Master password, BIP39 seed, key derivation | `docs/architecture/09-security-key-management.md` |
| Roadmap, phasing, what to build next | `docs/architecture/10-roadmap.md` |
| Building any UI screen (Avalonia or MAUI) | `docs/mockups/index.html` — open the relevant mockup, match its design language |
| Code style, naming, formatting | `docs/conventions.md` and `.editorconfig` |
| Commit messages, branch naming | `docs/commit-conventions.md` |

Always read multiple docs if the task spans them. A task like "import a statement and categorize transactions" spans 03, 04, and 02.

## Hard rules — never violate

These exist because violating them silently corrupts user data or breaks security:

1. **Money is `decimal`. Never `double` or `float`.** EF Core mapping: `decimal(18,2)`. This applies to every property, parameter, calculation, and column.
2. **Dates of transactions are `DateOnly`. System timestamps are `DateTime` in UTC.** Do not mix.
3. **`Coffer.Core` has zero references to UI frameworks.** No `using Avalonia.*`, no `using Microsoft.Maui.*`, no `using System.Windows.*`.
4. **`Coffer.Infrastructure` accesses platform APIs only behind interfaces.** DPAPI sits behind `IKeyVault`. File picker behind `IFilePicker`. Camera behind `ICamera`.
5. **No real bank statements committed to the repo.** Only anonymized samples produced by `tools/Anonymizer`. Add `*.real.pdf` to `.gitignore`.
6. **Master password and BIP39 seed are never logged, never sent to AI, never written to disk in plaintext.**
7. **AI prompts are anonymized before sending.** Account numbers, IBAN, full names, addresses replaced with placeholders. Implementation in `Infrastructure/AI/PromptAnonymizer.cs`.
8. **Every database migration runs `pre-migration-backup` first.** No exceptions, even for trivial migrations.
9. **Currency must be on every monetary entity.** Even if 99% is PLN, `Currency` is a non-null field. Multi-currency accounts will exist.
10. **Tests for parsers must use golden files.** New parser features require new golden files. Breaking existing golden files is a regression that blocks merge.
11. **This repo is PUBLIC.** Before every commit, mentally check: would I be comfortable if this exact change was on the front page of HN tomorrow? No API keys, no real account numbers, no real merchant lists tied to a person, no real receipt photos, no `.env` files, no `appsettings.Development.json` with real secrets. The `.gitignore` covers most cases but the responsibility is yours, not the tool's. When in doubt, `git diff --cached` before pushing.

## AI cost discipline

Three rules for keeping API costs predictable:

- **Categorization uses Haiku/gpt-4o-mini.** Cheap, batch 20–50 transactions per call, cache results by normalized description.
- **Chat and complex analysis use Sonnet 4.6 / gpt-4o.** Reasoning matters more than cost.
- **Vision (receipts) uses Sonnet 4.6 vision.** Quality matters; ~5 grosze per receipt is acceptable.
- **User sets a monthly cap in PLN.** When 80% of cap is reached: warning. When 100%: AI features pause, user notified, manual override available.

## What this app is NOT

To prevent scope creep and unsafe behaviors:

- **Not a tax advisor.** Does not generate PIT-37, does not advise on tax optimization. Polish tax law is complex and dynamic.
- **Not an investment advisor.** Does not recommend specific stocks/bonds/funds. Provides facts (free cash, links to research tools), never picks instruments. KNF licensing concern even for personal use because LLM hallucinations on rates are real.
- **Not a budgeting straitjacket.** Suggestions are suggestions. Hard limits/blocks are user-initiated.
- **Not a multi-user product.** No user accounts beyond the device owner. Sync is between owner's devices only.
- **Not a banking integration.** No PSD2/Open Banking. Polish banks rarely expose APIs and the value is not worth the regulatory friction.

## Conversation context

This document was produced after a long planning conversation. Key context the docs assume:

- The owner is a .NET developer in Toruń, Poland, building this for personal use first
- Primary bank is PKO BP, but architecture supports many banks via `IStatementParser` registry
- Owner refinances mortgage between banks, so multi-bank support is a real requirement
- Receipt capture is mobile-first; statement import is desktop-only
- Owner is comfortable with cost of ~10–30 PLN/month on AI APIs

When in doubt, optimize for: correctness of financial data > user privacy > UX > performance > developer convenience.
