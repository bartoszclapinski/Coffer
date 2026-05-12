# Coffer

[![build](https://github.com/bartoszclapinski/Coffer/actions/workflows/build.yml/badge.svg)](https://github.com/bartoszclapinski/Coffer/actions/workflows/build.yml)

Personal finance application with AI integration. Cross-platform .NET app — Avalonia desktop + MAUI mobile companion. Local-first, end-to-end encrypted, no third-party data exchange.

## What it does

- Imports PDF bank statements (PKO BP first, multi-bank support via parser registry)
- Auto-categorizes transactions using AI (cheap models for batch labeling, smarter ones for chat)
- Captures receipt photos on mobile, OCRs them with Claude vision, auto-matches to bank transactions
- Anomaly detection — duplicate charges, category spikes, missing recurring payments
- Goal-based financial advisor (savings goals, mortgage prepayment calculator, emergency fund tracking)
- Chat with your data — natural language questions answered via tool-calling against the local DB
- Multi-device sync via Google Drive (encrypted, only the user can decrypt)
- Encrypted backups: daily local + monthly to Drive + manual archive export

## What it doesn't do

- Tax advice or PIT-37 generation
- Specific investment recommendations
- Banking integration (no PSD2/Open Banking)
- Multi-user accounts

See `CLAUDE.md` and `docs/architecture/` for the full design.

## Architecture at a glance

```
Coffer.sln
├── src/
│   ├── Coffer.Core/              # domain, interfaces, no UI
│   ├── Coffer.Infrastructure/    # EF, parsers, AI, file I/O, secure storage
│   ├── Coffer.Application/       # ViewModels, use cases, framework-agnostic
│   ├── Coffer.Desktop/           # Avalonia
│   ├── Coffer.Mobile/            # MAUI (Android + iOS)
│   └── Coffer.Shared/            # DTOs
├── tests/
├── tools/
│   └── Anonymizer/                 # CLI to sanitize bank statements for golden file tests
├── docs/
│   ├── architecture/               # 10 design docs — see CLAUDE.md for routing
│   ├── conventions.md
│   └── commit-conventions.md
├── .editorconfig
├── .gitignore
└── CLAUDE.md                       # Read this first
```

## Stack

- .NET 9, C# 13
- Avalonia 11 (desktop), .NET MAUI (mobile)
- EF Core 9 + SQLite + SQLCipher (encrypted at rest)
- CommunityToolkit.Mvvm
- PdfPig for PDF parsing
- Anthropic.SDK + OpenAI SDK + Microsoft.Extensions.AI abstraction
- LiveChartsCore.SkiaSharpView for charts
- Serilog, FluentValidation, xUnit + FluentAssertions + Bogus + FsCheck

Full list and rationale: `docs/architecture/01-stack-and-projects.md`.

## UI mockups

Visual reference for all planned screens lives in `docs/mockups/`. Open `docs/mockups/index.html` in a browser to browse the gallery. Light and dark themes are supported via toggle (top-right corner).

Each mockup is a working HTML file that demonstrates intended visual language, density, and component behavior. They are visual contracts for implementation, not pixel-perfect specs.

## Working with this repo

This is a personal project. The `CLAUDE.md` at the root is meant to be read by Claude/Cursor before any task — it routes to the right architecture doc for whatever you're working on. Treat it as the single source of truth for cross-cutting decisions.

When you ask AI to help with a task in this repo:

> "Pracujemy nad parserem mBanku. Przeczytaj `CLAUDE.md` i `docs/architecture/03-statement-parsers.md`, potem zacznij implementację `MBankStatementParser`."

This keeps context windows manageable and answers focused.

## Status

Pre-MVP. Phase 0 (foundation) in progress. Roadmap: `docs/architecture/10-roadmap.md`. CI runs on every push and pull request to `main` — status badge at top.

## License

**No license** — all rights reserved.

This repository is public for transparency and portfolio purposes, but the code is not licensed for reuse, redistribution, or modification. You may read the code and study the architecture. You may not fork, copy, or use any portion in your own projects without explicit permission.

If you're interested in collaborating or have questions, open an issue.

A permissive open-source license (MIT or Apache 2.0) may be added later once the project reaches a stable state. Until then, default copyright applies.
