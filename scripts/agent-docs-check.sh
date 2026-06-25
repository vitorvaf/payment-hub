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
fi

require_text ".opencode/opencode.json" '"agent"[[:space:]]*:'
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

echo "Docs check passed."
