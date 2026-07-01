#!/usr/bin/env bash
set -euo pipefail

fail() {
  echo "FAIL: $1" >&2
  exit 1
}

require_file() {
  [ -f "$1" ] || fail "missing $1"
}

search() {
  local pattern="$1"
  shift

  if command -v rg >/dev/null 2>&1; then
    rg -n \
      -g '!**/bin/**' \
      -g '!**/obj/**' \
      -g '!**/Migrations/**' \
      "$pattern" "$@" || true
  else
    grep -RInE \
      --exclude-dir=bin \
      --exclude-dir=obj \
      --exclude-dir=Migrations \
      "$pattern" "$@" || true
  fi
}

assert_no_matches() {
  local label="$1"
  local pattern="$2"
  local hits
  shift 2

  hits="$(search "$pattern" "$@")"
  if [ -n "$hits" ]; then
    echo "$hits" >&2
    fail "$label"
  fi
}

echo "Checking Clean Architecture boundaries..."

require_file "PaymentHub.slnx"
require_file "src/PaymentHub.Domain/PaymentHub.Domain.csproj"
require_file "src/PaymentHub.Application/PaymentHub.Application.csproj"
require_file "src/PaymentHub.Infrastructure.Postgres/PaymentHub.Infrastructure.Postgres.csproj"
require_file "src/PaymentHub.Infrastructure.Providers/PaymentHub.Infrastructure.Providers.csproj"
require_file "src/PaymentHub.Api/PaymentHub.Api.csproj"
require_file "src/PaymentHub.Worker/PaymentHub.Worker.csproj"

assert_no_matches "Domain must not reference external frameworks or outer layers" \
  'using (Microsoft\.EntityFrameworkCore|Microsoft\.AspNetCore)|PaymentHub\.(Application|Infrastructure|Api|Worker)' \
  "src/PaymentHub.Domain"

assert_no_matches "Application must not reference API, Worker or concrete infrastructure" \
  'using PaymentHub\.(Api|Worker|Infrastructure\.Postgres|Infrastructure\.Providers)' \
  "src/PaymentHub.Application"

assert_no_matches "Worker must not reference API" \
  'using PaymentHub\.Api|ProjectReference Include="\.\./PaymentHub\.Api/' \
  "src/PaymentHub.Worker"

assert_no_matches "Infrastructure projects must not reference API or Worker" \
  'using PaymentHub\.(Api|Worker)|ProjectReference Include="\.\./PaymentHub\.(Api|Worker)/' \
  "src/PaymentHub.Infrastructure.Postgres" \
  "src/PaymentHub.Infrastructure.Providers"

if grep -q "ProjectReference" "src/PaymentHub.Domain/PaymentHub.Domain.csproj"; then
  fail "Domain project must not contain ProjectReference"
fi

if grep -qE "(ProjectReference Include=\".*PaymentHub\.Infrastructure|using PaymentHub\.Infrastructure)" "src/PaymentHub.Application/PaymentHub.Application.csproj"; then
  fail "Application project must not reference Infrastructure projects"
fi

echo "Architecture check passed."
