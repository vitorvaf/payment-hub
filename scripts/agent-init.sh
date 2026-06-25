#!/usr/bin/env bash
set -euo pipefail

echo "Payment Hub agent init"
echo
echo "Read first:"
echo "- AGENTS.md"
echo "- docs/harness/project-context.md"
echo "- docs/harness/workflow.md"
echo "- docs/harness/validation.md"
echo "- docs/harness/security.md"
echo "- docs/harness/learnings.md"
echo "- docs/harness/agent-operating-model.md"
echo "- docs/harness/opencode.md"
echo "- .github/copilot-instructions.md"
echo "- docs/specs/README.md"
echo
echo "OpenCode agents:"
echo "- planner"
echo "- implementer"
echo "- architect-reviewer"
echo "- qa-reviewer"
echo "- security-reviewer"
echo
echo "OpenCode skills:"
echo "- payment-slice"
echo "- dotnet-validation"
echo "- architecture-fitness"
echo "- security-review"
echo "- docs-maintenance"
echo
echo "Known validation commands:"
echo "- scripts/agent-docs-check.sh"
echo "- scripts/agent-architecture-check.sh"
echo "- scripts/agent-smoke.sh"
echo "- scripts/agent-verify.sh"
echo "- dotnet restore"
echo "- dotnet build"
echo "- dotnet test"
echo "- docker compose config"
echo
echo "Current git status:"
git status --short
echo
echo "Project files:"
printf "Solutions: "
find . -maxdepth 1 -name '*.slnx' -o -name '*.sln' | sort | tr '\n' ' '
echo
printf "Projects: "
find src tests -name '*.csproj' | sort | wc -l
