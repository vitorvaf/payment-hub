---
name: dotnet-validation
description: Use when validating Payment Hub changes with dotnet restore, build, test, format, Docker config, and agent scripts.
---

# Dotnet Validation

## What It Does

Selects and runs local validation commands for .NET, Docker, scripts, docs, and harness changes.

## When To Use

- Before finishing any code slice.
- After changing tests, `.csproj`, Docker, scripts, docs, OpenCode config, or CI.
- When a validation failure needs triage.

## Expected Inputs

- Scope of changed files.
- Risk level of the change.
- Whether Docker, database, API, or Worker behavior changed.

## Steps

1. Read `docs/harness/validation.md` and `docs/ai/validation-checklist.md`.
2. For harness/docs/scripts, run `scripts/agent-docs-check.sh` and `scripts/agent-verify.sh`.
3. For architecture boundaries, run `scripts/agent-architecture-check.sh`.
4. For .NET changes, run `dotnet restore`, `dotnet build`, and `dotnet test`.
5. Run `dotnet format --verify-no-changes` when formatting risk exists.
6. For Docker changes, run `docker compose config`.
7. Record skipped commands with reason and residual risk.

## Acceptance Criteria

- Commands match the actual scope.
- Failures are fixed or documented as blockers.
- No external real provider, real secret, or production service is required.

## Evidence To Record

- Command, result, and relevant count or failure summary.
- Any command not run and why.
- Remaining risk after validation.

## Antipatterns

- Running only a narrow test after broad changes.
- Hiding failing tests as environmental without evidence.
- Using production credentials or real provider calls.
- Removing tests to pass validation.
