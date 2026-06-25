---
name: payment-slice
description: Use when implementing a Payment Hub feature, bugfix, or harness slice that needs planner -> implementer -> reviewer -> validation flow.
---

# Payment Slice

## What It Does

Guides a small, reviewable Payment Hub slice from discovery to evidence without expanding scope.

## When To Use

- Feature, bugfix, audit fix, or harness change with more than one step.
- Changes touching API, Application, Domain, Infrastructure, Worker, tests, docs, or scripts.
- Any task that needs `agent-progress.md` evidence.

## Expected Inputs

- User goal and constraints.
- Related spec from `docs/specs/README.md`.
- Relevant ADRs from `docs/adr/000-adr-index.md`.
- Known acceptance criteria or bug reproduction.

## Steps

1. Read `AGENTS.md` and core docs in `docs/harness/`.
2. Identify scope, out of scope, risks, affected files, and validation commands.
3. Record the slice contract in `agent-progress.md` before broad edits.
4. Implement the smallest useful change.
5. Ask independent review when architecture, QA, or security risk exists.
6. Run proportional validation and update evidence.

## Acceptance Criteria

- Slice is small, coherent, and traceable to a spec or harness doc.
- No business feature is added outside the user request.
- Validation is executed or explicitly justified.
- Files, commands, evidence, and risks are recorded.

## Evidence To Record

- Objective and out of scope.
- Files created or changed.
- Specs/ADRs consulted.
- Commands and results.
- Residual risks and next slice if needed.

## Antipatterns

- Starting implementation before discovery.
- Mixing unrelated refactors with the slice.
- Updating `AGENTS.md` with details that belong in `docs/harness/` or skills.
- Declaring done without validation evidence.
