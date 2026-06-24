# OpenCode Configuration

This directory keeps project-specific OpenCode configuration for the agents harness.

## Harness alignment

OpenCode agents must follow the same project contract used by Copilot and Codex:

- Read `AGENTS.md` first.
- Read `.github/copilot-instructions.md` and the relevant `.github/instructions/*.instructions.md` files for the slice.
- Treat `docs/specs/` as the implementation contract and `docs/adr/` as accepted architecture decisions.
- Register scope, validation and evidence in `agent-progress.md` for multi-step work.
- Do not bypass `scripts/agent-verify.sh`, `dotnet restore`, `dotnet build` or `dotnet test` when they apply.

## Files

- `opencode.json`: OpenCode JSON configuration. Keep only keys supported by the installed OpenCode schema.
- `agents/*.md`: role prompts used as harness references for OpenCode agents.

OpenCode currently rejects top-level `agents` and `notes` keys in `opencode.json`. If agent registration is needed, use the installed OpenCode version's supported `agent` configuration format or the CLI-managed agent files instead of adding unsupported top-level keys.
