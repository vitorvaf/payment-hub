# OpenCode Configuration

This directory keeps project-specific OpenCode configuration for the Payment Hub harness.

## Run OpenCode

Start OpenCode from the repository root so `.opencode/opencode.json`, `AGENTS.md`, specs and harness docs resolve correctly.

Before substantial work, run:

```bash
scripts/agent-init.sh
```

After changing `.opencode/opencode.json`, agents or skills, quit and restart OpenCode. The running session keeps the already-loaded config.

## Choose Agent

| Agent | Use for |
| --- | --- |
| `planner` | Planning a slice before edits |
| `implementer` | Executing a clear slice |
| `architect-reviewer` | Independent architecture review |
| `qa-reviewer` | Independent test and validation review |
| `security-reviewer` | Independent security review |

`planner` is the default primary agent. Reviewers are subagents and do not edit files by default.

## Load Skills

Skills live under `.opencode/skills/<name>/SKILL.md` and are registered through `skills.paths` in `opencode.json`.

Use skills only when relevant:

- `payment-slice`: feature, bugfix or harness slice flow.
- `dotnet-validation`: restore, build, test, format and agent scripts.
- `architecture-fitness`: Clean Architecture and dependencies.
- `security-review`: multitenancy, API Key, HMAC, webhooks and secrets.
- `docs-maintenance`: specs, ADRs, docs and progress records.

## Feature Flow

1. Use `planner` with `payment-slice`.
2. Read `AGENTS.md`, `docs/specs/README.md`, the related spec and relevant ADRs.
3. Record objective, out of scope, plan, risks and validations in `agent-progress.md`.
4. Switch to `implementer` for one small slice.
5. Ask `architect-reviewer`, `qa-reviewer` or `security-reviewer` when risk exists.
6. Run validations and record evidence.

## Bugfix Flow

1. Reproduce or delimit the failure.
2. Read the related spec and tests.
3. Apply the smallest safe fix.
4. Add a regression test when practical.
5. Run targeted tests first, then broader validation proportional to the change.
6. Record cause, commands, results and residual risk.

## Audit Flow

1. Use `planner` to define audit scope.
2. Use the reviewer matching the risk area.
3. Report findings first, ordered by severity, with file/line when possible.
4. Do not fix unrelated findings in the same pass.
5. Record follow-ups in `feature_list.md` when they remain out of scope.

## Required Validation

Use the smallest set that fits the change, but do not skip applicable checks:

```bash
scripts/agent-docs-check.sh
scripts/agent-architecture-check.sh
scripts/agent-smoke.sh
scripts/agent-verify.sh
dotnet restore
dotnet build
dotnet test
```

Use `docker compose config` for Docker changes. Use `dotnet format --verify-no-changes` when formatting is part of the risk.

## Security Rules

- Never commit real `.env`, secrets, tokens, provider credentials or API Keys.
- Never store card number or CVV.
- Keep API Keys hashed and provider credentials encrypted or prepared for encryption.
- Authenticated endpoints derive tenant/application from `ITenantContext`, never from request body.
- Webhooks are persisted in Inbox before processing and outgoing events go through Outbox.
- `git push`, destructive shell actions, broad removals, migrations and secret-related edits require approval.

## Config Rules

- Keep only keys supported by `https://opencode.ai/config.json`.
- Use top-level `agent`, not unsupported `agents`.
- Do not add top-level `notes`.
- Keep long guidance in `docs/harness/` or skills, not in `opencode.json`.

See `docs/harness/opencode.md`, `docs/harness/agent-operating-model.md` and `docs/harness/skill-index.md` for the full operating model.
