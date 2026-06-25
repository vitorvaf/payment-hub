---
name: security-review
description: Use when reviewing Payment Hub security around multitenancy, API keys, HMAC, webhooks, idempotency, logs, secrets, and sensitive data.
---

# Security Review

## What It Does

Checks security-sensitive changes against Payment Hub rules for payment data, tenants, credentials, webhooks, logs, and reliable processing.

## When To Use

- Changes to auth, API keys, tenant/application context, provider credentials, webhooks, HMAC, logs, database, CI, Docker, or scripts.
- Before accepting a review finding as resolved.
- When `.env`, tokens, API keys, provider secrets, card data, or CVV appear anywhere.

## Expected Inputs

- Diff or file list.
- Related specs, especially `002`, `006`, `011`, and `012`.
- Validation evidence and logs if available.

## Steps

1. Read `docs/harness/security.md` and `.github/instructions/security.instructions.md`.
2. Compare with `docs/specs/011-security-and-compliance.md` and related specs.
3. Verify authenticated endpoints derive tenant/application from `ITenantContext`.
4. Verify API keys are hashed and provider credentials are encrypted or prepared for encryption.
5. Verify webhooks are persisted in Inbox before processing and signed when supported.
6. Verify logs, responses, tests, docs, and errors do not expose secrets or payment data.
7. Run `scripts/agent-verify.sh` for secret-pattern scan when applicable.

## Acceptance Criteria

- No card number, CVV, real API key, real token, or real provider secret is present.
- Auth and multitenancy boundaries remain explicit.
- Idempotency, Inbox, and Outbox are not weakened.
- Sensitive changes have tests, evidence, and human-review notes when needed.

## Evidence To Record

- Specs reviewed.
- Secret scan result.
- Findings with file and line when possible.
- Residual security risk or required ADR.

## Antipatterns

- Treating webhook processed as payment approved.
- Logging request payloads that may contain secrets.
- Accepting tenant/application IDs from body in authenticated flows.
- Committing `.env` or replacing fake samples with real credentials.
