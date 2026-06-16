# OpenCode Configuration

This directory keeps project-specific OpenCode configuration for the agents harness.

## Files

- `opencode.json`: OpenCode JSON configuration. Keep only keys supported by the installed OpenCode schema.
- `agents/*.md`: role prompts used as harness references for OpenCode agents.

OpenCode currently rejects top-level `agents` and `notes` keys in `opencode.json`. If agent registration is needed, use the installed OpenCode version's supported `agent` configuration format or the CLI-managed agent files instead of adding unsupported top-level keys.
