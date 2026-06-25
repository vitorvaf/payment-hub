#!/usr/bin/env bash
set -euo pipefail

fail() {
  echo "FAIL: $1" >&2
  exit 1
}

echo "Running Payment Hub agent smoke checks..."

[ -f "PaymentHub.slnx" ] || fail "missing PaymentHub.slnx"
[ -f "docker-compose.yml" ] || fail "missing docker-compose.yml"

scripts/agent-docs-check.sh
scripts/agent-architecture-check.sh

if ! command -v dotnet >/dev/null 2>&1; then
  fail "dotnet CLI not found"
fi

echo "Restoring .NET dependencies..."
dotnet restore PaymentHub.slnx

echo "Building solution without starting external services..."
dotnet build --no-restore PaymentHub.slnx

if command -v docker >/dev/null 2>&1; then
  echo "Validating Docker Compose syntax..."
  docker compose config >/dev/null
else
  echo "Skipping Docker Compose syntax check because docker was not found."
fi

echo "Agent smoke checks passed."
