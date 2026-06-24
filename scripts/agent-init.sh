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
echo "- .github/copilot-instructions.md"
echo "- docs/specs/README.md"
echo
echo "Known validation commands:"
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
