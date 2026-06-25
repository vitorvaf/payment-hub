---
name: architecture-fitness
description: Use when checking Clean Architecture boundaries, project dependencies, ADR fit, and Domain/Application/Infrastructure/API/Worker separation.
---

# Architecture Fitness

## What It Does

Reviews Payment Hub architecture boundaries and dependency direction before or after implementation.

## When To Use

- Changes to Domain, Application, Infrastructure, API, Worker, database, provider adapters, or hosted checkout flow.
- New abstractions, new services, or changes that may require ADRs.
- Before accepting a large or sensitive diff.

## Expected Inputs

- Files changed or planned.
- Related specs and ADRs.
- Intended dependency direction and runtime flow.

## Steps

1. Read `docs/harness/architecture-fitness.md`.
2. Read related specs from `docs/specs/README.md`.
3. Read ADRs in `docs/adr/` when decisions are architectural.
4. Check project references and namespace imports.
5. Verify controllers call Application and Domain stays infrastructure-free.
6. Run `scripts/agent-architecture-check.sh` when local files are available.
7. Report findings before proposing broad refactors.

## Acceptance Criteria

- Dependency direction is preserved.
- MVP constraints are respected.
- New decisions are captured as ADR proposals when needed.
- No overengineering or broker dependency is introduced without explicit decision.

## Evidence To Record

- Boundary checks performed.
- Specs/ADRs consulted.
- Script result or manual finding.
- Any required ADR/spec follow-up.

## Antipatterns

- Putting domain rules in controllers.
- Letting Domain reference EF Core, ASP.NET Core, providers, or logging infrastructure.
- Adding generic abstractions without a concrete need.
- Changing architecture and tests in an unreviewable mega-slice.
