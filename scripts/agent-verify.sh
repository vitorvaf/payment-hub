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
require_file "docs/harness/agent-operating-model.md"
require_file "docs/harness/architecture-fitness.md"
require_file "docs/harness/skill-index.md"
require_file "docs/harness/opencode.md"
require_file "docs/specs/README.md"
require_file "docs/ai/agent-readiness-audit.md"
require_file "docs/ai/copilot-surface-map.md"
require_file "docs/ai/model-routing.md"
require_file "docs/ai/agent-workflow.md"
require_file "docs/ai/review-governance.md"
require_file "docs/ai/validation-checklist.md"
require_file "docs/ai/harness-engineering.md"

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

echo "Checking OpenCode alignment files..."
require_file ".opencode/README.md"
require_file ".opencode/opencode.json"
require_file ".opencode/agents/planner.md"
require_file ".opencode/agents/implementer.md"
require_file ".opencode/agents/architect-reviewer.md"
require_file ".opencode/agents/qa-reviewer.md"
require_file ".opencode/agents/security-reviewer.md"
require_file ".opencode/skills/payment-slice/SKILL.md"
require_file ".opencode/skills/dotnet-validation/SKILL.md"
require_file ".opencode/skills/architecture-fitness/SKILL.md"
require_file ".opencode/skills/security-review/SKILL.md"
require_file ".opencode/skills/docs-maintenance/SKILL.md"
require_file "scripts/agent-docs-check.sh"
require_file "scripts/agent-architecture-check.sh"
require_file "scripts/agent-smoke.sh"

scripts/agent-docs-check.sh
scripts/agent-architecture-check.sh

echo "Checking state files..."
require_file "feature_list.md"
require_file "agent-progress.md"

echo "Checking for obvious secret file names..."
if find . -path './.git' -prune -o -name '.env' -print | grep -q .; then
  fail "real .env file found"
fi

echo "Scanning for obvious secret patterns..."
secret_pattern="(BEGIN .*PRIVATE KEY|\\b(ghp|github_pat|glpat|sk_live|pk_live|xox[baprs]|AKIA|ASIA)[A-Za-z0-9_=-]{24,}|\\bphk_[A-Za-z0-9_=-]{16,})"
if command -v rg >/dev/null 2>&1; then
  secret_hits="$(rg -n "$secret_pattern" . \
    -g '!**/.git/**' \
    -g '!**/bin/**' \
    -g '!**/obj/**' \
    -g '!**/node_modules/**' \
    -g '!**/TestResults/**' \
    -g '!**/coverage/**' \
    -g '!scripts/agent-verify.sh' || true)"
else
  secret_hits="$(grep -RInE "$secret_pattern" . \
    --exclude-dir=.git \
    --exclude-dir=bin \
    --exclude-dir=obj \
    --exclude-dir=node_modules \
    --exclude-dir=TestResults \
    --exclude-dir=coverage \
    --exclude=agent-verify.sh || true)"
fi

if [ -n "$secret_hits" ]; then
  echo "$secret_hits" >&2
  fail "obvious secret pattern found"
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
