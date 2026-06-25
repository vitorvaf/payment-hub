---
name: docs-maintenance
description: Use when updating Payment Hub docs, ADRs, specs, AGENTS.md, OpenCode docs, or agent-progress without duplicating source-of-truth content.
---

# Docs Maintenance

## What It Does

Keeps documentation concise, linked, versioned, and aligned with specs/ADRs while avoiding documentation drift.

## When To Use

- Harness, OpenCode, Copilot, Codex, specs, ADRs, runbooks, or README updates.
- Contract changes that require spec updates.
- Architecture decisions that require ADR proposals.
- Multi-step tasks that must update `agent-progress.md`.

## Expected Inputs

- Target doc or change summary.
- Source of truth: spec, ADR, code, audit, or user decision.
- Whether the change is operational guidance or product contract.

## Steps

1. Keep `AGENTS.md` as an index with essential global rules only.
2. Put operational process in `docs/harness/`.
3. Put product contracts in `docs/specs/`.
4. Put durable architecture decisions in `docs/adr/` using `docs/harness/adr-template.md`.
5. Put tool-specific guidance in `.opencode/`, `.github/`, `.codex/`, or relevant tool folder.
6. Run `scripts/agent-docs-check.sh` and `scripts/agent-verify.sh` when docs/harness changes.
7. Update `docs/harness/learnings.md` only for reusable learnings.

## Acceptance Criteria

- No duplicated long-form content across `AGENTS.md`, Copilot, OpenCode, and skills.
- Links point to existing files.
- Specs and ADRs remain the contract sources.
- `agent-progress.md` has plan, evidence, validation, and risks for multi-step work.

## Evidence To Record

- Files changed.
- Source of truth used.
- Checks run and results.
- Any doc drift found or intentionally left for backlog.

## Antipatterns

- Moving product rules into agent prompts instead of specs.
- Creating a giant `AGENTS.md`.
- Editing accepted ADRs to change history instead of creating a new ADR.
- Recording noisy one-off operational notes as learnings.
