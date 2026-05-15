# Sprints

Lightweight work organisation for Coffer. Each sprint = a logical chunk of work that ends with a commit and a working / testable state. Sprints are not time-boxed — they end when DoD is met.

A future LLM (or the same one after a break) starts by:

1. Reading [CLAUDE.md](../../CLAUDE.md)
2. Reading [index.md](index.md) — which sprint is in progress
3. Reading `sprint-N/sprint-N.md` (plan) and `sprint-N/log.md` (what was done)
4. Continuing the work per the plan

## Sprint structure

Each sprint is a directory `sprint-N/` with two files:

- `sprint-N.md` — the sprint plan. While the sprint is running, only checkboxes and the "Open questions" section are edited (closed questions are moved to `log.md` as decisions).
- `log.md` — append-only chronological log.

## `sprint-N.md` format

```markdown
# Sprint N — <short title>

**Phase:** <number from docs/architecture/10-roadmap.md>
**Status:** Planned | In progress | Closed
**Depends on:** sprint-X, sprint-Y (or: none)

## Goal

One sentence — what should work after the sprint.

## Steps

- [ ] N.1 ...
- [ ] N.2 ...

(Sub-steps as N.1.a, N.1.b are allowed.)

## Definition of Done

A specific manual or automated test to tick off.

## Files affected

A list of files / directories expected to be touched.

## Open questions

- question 1
- question 2
```

## `log.md` format

Append-only, newest at the bottom:

```markdown
# Sprint N log

## YYYY-MM-DD

- `HH:MM` step N.X complete — commit `abc1234` — short note
- `HH:MM` decision: <what> because <why>
- `HH:MM` problem: <description> → resolution: <what we did>
```

Commit hash is optional but helpful for navigation.

## Sprint status

- **Planned** — plan written, no step started yet
- **In progress** — at least one step done, not all
- **Closed** — all steps complete, DoD met, last log entry is "sprint closed"

## Updating `index.md`

After any sprint status change, update [index.md](index.md). `index.md` is only a status table — do not copy sprint content there.

## Language

All sprint plans, logs, code, and documentation in this repository are written in English, per [docs/conventions.md](../../docs/conventions.md). The Claude chat session is the only place where Polish is used.

> Note: Sprint plans and logs for Sprint 0 through Sprint 3 were originally written in Polish during the early phase of the project; they remain in Polish as historical artefacts. A separate chore PR can translate them retroactively if desired. All sprints from Sprint 4 onward are in English.
