# Commit conventions

We follow [Conventional Commits](https://www.conventionalcommits.org/) with a few project-specific additions. The format helps when scanning history, building changelogs, and asking AI to summarize what changed.

## Format

```
<type>(<scope>): <short summary>

<optional body>

<optional footer>
```

- **type:** required, lowercase, see list below
- **scope:** optional, lowercase, names a part of the codebase (e.g., `parser`, `sync`, `advisor`, `mobile`)
- **summary:** imperative mood, no trailing period, max ~72 chars
- **body:** explain *why* the change was made, not *what* (the diff shows what)
- **footer:** breaking changes, issue references

## Types

| Type | When to use |
|---|---|
| `feat` | New user-visible feature |
| `fix` | Bug fix |
| `refactor` | Code restructuring without behavior change |
| `perf` | Performance improvement |
| `test` | Adding or fixing tests |
| `docs` | Documentation only (including CLAUDE.md, architecture docs) |
| `chore` | Tooling, deps, CI, build config |
| `style` | Formatting, whitespace; no logic change |
| `wip` | Work in progress (use sparingly; squash before merging) |
| `security` | Security-relevant change worth surfacing in changelog |

## Scopes (typical)

Use scopes that map to the architecture docs:

- `parser` — anything in `Infrastructure/Parsers/`
- `parser-pko`, `parser-mbank`, etc. — bank-specific
- `ai` — AI providers, prompts, categorization
- `vision` — receipt OCR
- `sync` — Drive sync, event sourcing
- `mobile` — MAUI app specifically
- `desktop` — Avalonia app specifically
- `advisor` — financial advisor / goals
- `db` — schema, migrations, EF
- `crypto` — encryption, key management, BIP39
- `backup` — backup and recovery
- `import` — statement import flow
- `ui` — generic UI changes

## Examples

```
feat(parser-pko): handle credit card statement layout

Credit card PDFs use a different column layout than checking accounts.
Detect via header keyword "Wyciąg z karty kredytowej" and dispatch to
a dedicated layout table.

Adds 2 new golden file samples.
```

```
fix(sync): apply older field updates instead of dropping them

Previously, two devices updating the same transaction's category
within the same second could lose one update due to entity-level
last-write-wins. Switched to field-level clocks per the design
in docs/architecture/06-sync-and-mobile.md.
```

```
refactor(advisor): extract MortgagePrepaymentCalculator to Core

Was sitting in Application; doesn't depend on anything Application-y.
Moves into Core so it can be unit tested without DI.
```

```
docs: clarify migration backup retention is 90 days, not 30

CLAUDE.md and 02-database-and-encryption.md disagreed. 90 is correct.
```

```
chore(deps): bump PdfPig to 0.1.13

Includes fix for letters-with-rotation in some banks' headers.
```

```
security(crypto): increase Argon2 memory from 32MB to 64MB

Recommended minimum has shifted as consumer hardware improved.
DEK files store the params used, so older DEKs still decrypt with old params.
```

```
feat(advisor)!: rename GoalStatus.Pending to GoalStatus.Paused

BREAKING CHANGE: existing Goal rows with Status=Pending need migration.
Migration script Migrations/20260510_RenamePending.cs handles it.
```

The `!` after the type indicates a breaking change. The footer must include `BREAKING CHANGE:` with details.

## Body guidelines

- Wrap at ~72 chars
- Explain why, motivation, what didn't work
- Reference architecture docs when relevant: `Per docs/architecture/03-statement-parsers.md`
- Don't paste full stack traces; reference issue numbers if you have them

## When in doubt

- Small changes: just a one-line summary is fine
- Large changes: write a real body, future you will thank you
- Breaking changes: always note them; this is a personal project but breaking changes still need to migrate user data

## What NOT to do

- ❌ `update`
- ❌ `fix bug`
- ❌ `wip`
- ❌ `more changes`
- ❌ Multiple unrelated changes squeezed into one commit (split them)
- ❌ Commits that mix formatting changes with logic changes (do them separately)

## Branch naming and PR workflow

**Rule: never push directly to `main`.** Every change — feature, fix, doc, chore — goes through feature branch + PR + squash-merge. Branch protection on `main` enforces this mechanically (`enforce_admins=true`, so even the owner cannot bypass).

Branch naming:

- `feature/<scope>-<short-name>` — new functionality, e.g. `feature/parser-mbank`
- `fix/<scope>-<short-name>` — bug fix, e.g. `fix/sync-clock-drift`
- `chore/<scope>-<short-name>` — tooling, deps, CI, operational docs
- `experiment/<short-name>` — exploratory work that may not merge to `main`

Workflow per sprint (or smaller change):

1. `git checkout -b <type>/<scope>-<short-name>`
2. Commit normally on the branch; multiple pushes are fine (CI re-runs per push)
3. When DoD is met: `gh pr create` with a title + body describing the change
4. Wait for CI green (`build-and-test` and `format-check` are required status checks)
5. `gh pr merge --squash --delete-branch`
6. `git checkout main && git pull`

Squash-merge only — no merge commits, no rebase-merge. One PR equals one commit on `main`. Linear history is also enforced by branch protection (`required_linear_history=true`).

Exception: critical hotfix (e.g. exposed secret accidentally pushed). Direct push is acceptable with an explicit "hotfix, branch protection bypassed, follow-up PR adds safeguard" note in the commit body. This should be very rare.
