#!/usr/bin/env bash
set -euo pipefail

fail() {
  echo "FAIL: $1" >&2
  exit 1
}

require_file() {
  [ -f "$1" ] || fail "missing $1"
}

echo "Checking harness files..."
require_file "AGENTS.md"
require_file ".github/copilot-instructions.md"
require_file "docs/harness/project-context.md"
require_file "docs/harness/workflow.md"
require_file "docs/harness/validation.md"
require_file "docs/harness/security.md"
require_file "docs/harness/learnings.md"
require_file "docs/specs/README.md"
require_file "docs/ai/agent-readiness-audit.md"
require_file "docs/ai/copilot-surface-map.md"
require_file "docs/ai/model-routing.md"
require_file "docs/ai/agent-workflow.md"
require_file "docs/ai/review-governance.md"
require_file "docs/ai/validation-checklist.md"

echo "Checking Copilot path instructions..."
for file in .github/instructions/*.instructions.md; do
  grep -q '^---$' "$file" || fail "$file has no frontmatter"
  grep -q '^applyTo:' "$file" || fail "$file has no applyTo"
done

echo "Checking prompts, agents and skills..."
require_file ".github/prompts/plan-feature.prompt.md"
require_file ".github/prompts/implement-feature.prompt.md"
require_file ".github/prompts/review-pr.prompt.md"
require_file ".github/prompts/generate-tests.prompt.md"
require_file ".github/prompts/debug-bug.prompt.md"
require_file ".github/prompts/document-adr.prompt.md"
require_file ".github/prompts/test-evidence.prompt.md"
require_file ".github/agents/planner.agent.md"
require_file ".github/agents/implementer.agent.md"
require_file ".github/agents/reviewer.agent.md"
require_file ".github/agents/tester.agent.md"
require_file ".github/agents/debugger.agent.md"
require_file ".github/agents/architect.agent.md"
require_file ".github/skills/feature-planning/SKILL.md"
require_file ".github/skills/test-evidence/SKILL.md"
require_file ".github/skills/adr-generation/SKILL.md"
require_file ".github/skills/bug-investigation/SKILL.md"

echo "Checking state files..."
require_file "feature_list.md"
require_file "agent-progress.md"

echo "Checking for obvious secret file names..."
if find . -path './.git' -prune -o -name '.env' -print | grep -q .; then
  fail "real .env file found"
fi

echo "Checking Docker Compose syntax..."
docker compose config >/dev/null

if [ "${RUN_DOTNET_VALIDATION:-0}" = "1" ]; then
  echo "Running dotnet validation..."
  dotnet restore
  dotnet build
  dotnet test
else
  echo "Skipping dotnet restore/build/test. Set RUN_DOTNET_VALIDATION=1 to run them."
fi

echo "Agent verification passed."
