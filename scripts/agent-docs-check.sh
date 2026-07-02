#!/usr/bin/env bash
set -euo pipefail

fail() {
  echo "FAIL: $1" >&2
  exit 1
}

require_file() {
  [ -f "$1" ] || fail "missing $1"
}

require_text() {
  local file="$1"
  local pattern="$2"
  grep -qE "$pattern" "$file" || fail "$file missing pattern: $pattern"
}

echo "Checking harness and OpenCode docs..."

require_file "AGENTS.md"
require_file ".github/copilot-instructions.md"
require_file ".opencode/README.md"
require_file ".opencode/opencode.json"
require_file "docs/harness/agent-operating-model.md"
require_file "docs/harness/architecture-fitness.md"
require_file "docs/harness/skill-index.md"
require_file "docs/harness/opencode.md"
require_file "docs/specs/README.md"
require_file "docs/adr/000-adr-index.md"
require_file "agent-progress.md"

if [ "$(wc -l < AGENTS.md)" -gt 120 ]; then
  fail "AGENTS.md is too large; keep it as an index"
fi

if [ "$(wc -l < .github/copilot-instructions.md)" -gt 120 ]; then
  fail ".github/copilot-instructions.md is too large; move details to docs/harness or specs"
fi

if command -v python3 >/dev/null 2>&1; then
  python3 -m json.tool ".opencode/opencode.json" >/dev/null || fail ".opencode/opencode.json is invalid JSON"
  python3 - <<'PY'
import json
import sys

with open(".opencode/opencode.json", encoding="utf-8") as file:
    config = json.load(file)

for key in ("agents", "notes"):
    if key in config:
        raise SystemExit(f"FAIL: .opencode/opencode.json must not use unsupported top-level {key} key")

if "agent" in config:
    raise SystemExit("FAIL: .opencode/agents/*.md is the agent source of truth; do not duplicate top-level agent config")

if "prompt" in json.dumps(config):
    raise SystemExit("FAIL: opencode.json must not contain agent prompts")

if config.get("default_agent") != "planner":
    raise SystemExit("FAIL: default_agent must be planner")

skills = config.get("skills", {})
if ".opencode/skills" not in skills.get("paths", []):
    raise SystemExit("FAIL: .opencode/skills must be listed in skills.paths")

permission = config.get("permission", {})
if permission.get("edit", {}).get("*") != "ask":
    raise SystemExit("FAIL: global edit permission must default to ask")
if permission.get("bash", {}).get("git push*") != "ask":
    raise SystemExit("FAIL: git push must ask approval")
if permission.get("bash", {}).get("rm -rf*") != "ask":
    raise SystemExit("FAIL: broad removals must ask approval")
PY
else
  if grep -qE '"agent"[[:space:]]*:' ".opencode/opencode.json"; then
    fail ".opencode/agents/*.md is the agent source of truth; do not duplicate top-level agent config"
  fi
fi

require_text ".opencode/opencode.json" '"skills"[[:space:]]*:'

if grep -qE '"agents"[[:space:]]*:' ".opencode/opencode.json"; then
  fail ".opencode/opencode.json must not use unsupported top-level agents key"
fi

if grep -qE '"notes"[[:space:]]*:' ".opencode/opencode.json"; then
  fail ".opencode/opencode.json must not use unsupported top-level notes key"
fi

echo "Checking OpenCode agents..."
for agent in planner implementer architect-reviewer qa-reviewer security-reviewer; do
  file=".opencode/agents/${agent}.md"
  require_file "$file"
  require_text "$file" '^---$'
  require_text "$file" '^description:'
  require_text "$file" '^mode: (primary|subagent|all)$'
done

for agent in planner implementer; do
  file=".opencode/agents/${agent}.md"
  require_text "$file" '^  task:$'
  require_text "$file" '^    '\''\*'\'': deny$'
  require_text "$file" '^    architect-reviewer: allow$'
  require_text "$file" '^    qa-reviewer: allow$'
  require_text "$file" '^    security-reviewer: allow$'
done

if grep -qE "^[[:space:]]+'\\*':[[:space:]]+allow$" ".opencode/agents/implementer.md"; then
  fail "implementer must not have broad edit/task/bash allow"
fi

require_text ".opencode/agents/implementer.md" '^    '\''\*'\'': ask$'
require_text ".opencode/agents/implementer.md" '^    '\''.env'\'': deny$'
require_text ".opencode/agents/implementer.md" '^    '\''src/PaymentHub.Infrastructure.Postgres/Migrations/\*\*'\'': ask$'

for agent in architect-reviewer qa-reviewer security-reviewer; do
  file=".opencode/agents/${agent}.md"
  require_text "$file" '^mode: subagent$'
  require_text "$file" '^  edit: deny$'
  require_text "$file" '^  task: deny$'
  if grep -qE '^[[:space:]]+edit:[[:space:]]+allow$' "$file"; then
    fail "$agent must not allow edit"
  fi
done

echo "Checking OpenCode skills..."
for skill in payment-slice dotnet-validation architecture-fitness security-review docs-maintenance; do
  file=".opencode/skills/${skill}/SKILL.md"
  require_file "$file"
  require_text "$file" '^---$'
  require_text "$file" "^name: ${skill}$"
  require_text "$file" '^description: .+'
  require_text "$file" '^## What It Does$'
  require_text "$file" '^## When To Use$'
  require_text "$file" '^## Expected Inputs$'
  require_text "$file" '^## Steps$'
  require_text "$file" '^## Acceptance Criteria$'
  require_text "$file" '^## Evidence To Record$'
  require_text "$file" '^## Antipatterns$'
done

echo "Checking observability anti-leak gate (Slice 9-O1)..."
# Slice 9-O1 introduces the rule that production code MUST NOT
# interpolate apiKey / webhookSecret / rawPayload / signature /
# Authorization / body into log invocations. The regex below scans all
# *.cs files under src/ for `Log*(<token>` patterns (case insensitive) and
# fails the build when a hit is found. Allowlist: tests/, docs/, and the
# observability catalogue itself.
#
# Update `docs/specs/011-security-and-compliance.md` and `ForbiddenTokens`
# in `tests/PaymentHub.UnitTests/Observability/NoLeakLogTests.cs` when
# adding a new forbidden category.
LEAK_PATTERN='Log(Warning|Information|Error|Debug|Critical|Trace)\([^)]*\{(apiKey|webhookSecret|rawPayload|signature|Authorization|body)\}'
LEAK_HITS="$(grep -RInE "$LEAK_PATTERN" 'src/' 2>/dev/null | grep -vE '/Observability/(SafeLog\.cs|CorrelationIdGenerator\.cs|PaymentHubLogEvents\.cs|PaymentHubMetrics\.cs)$' || true)"
if [ -n "$LEAK_HITS" ]; then
  echo "FAIL: Observability anti-leak gate triggered. Production code MUST NOT" >&2
  echo "interpolate apiKey/webhookSecret/rawPayload/signature/Authorization/body" >&2
  echo "into Log*( invocations. Use SafeLog helpers from" >&2
  echo "PaymentHub.Application.Observability.SafeLog instead." >&2
  echo "Hits:" >&2
  echo "$LEAK_HITS" >&2
  exit 1
fi

echo "Docs check passed."
