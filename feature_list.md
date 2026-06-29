# Feature List

Backlog leve para agentes. Use para registrar itens que nao serao implementados no slice atual.

| ID | Tipo | Titulo | Status | Spec/ADR | Observacoes |
|----|------|--------|--------|----------|-------------|
| AI-001 | Harness | Criar CI com restore/build/test | Concluido | `docs/ai/validation-checklist.md` | Workflow `.github/workflows/ci.yml` valida harness, restore, build e test. |
| AI-002 | Testes | Evoluir testes de integracao | Aberto | `docs/specs/013-testing-strategy.md` | Projeto existe, mas precisa cenarios reais. |
| AI-003 | Testes | Avaliar smoke/E2E local | Concluido | `docs/specs/009-api-contracts.md` | Slice 3-IT (2026-06-29): 4 testes E2E cobrindo checkout + webhook valido + idempotencia + fail-fast 401 com API real + Postgres (Testcontainers) + adapter AbacatePay + fakes de transporte outbound. |
| AI-004 | Harness | Aprimorar CI com cache, resultados e gate de secrets | Concluido | `docs/ai/validation-checklist.md` | CI usa cache NuGet, publica `.trx` e roda `scripts/agent-verify.sh` com scan simples de secrets. |
| AI-005 | Harness | Alinhar agentes OpenCode ao harness Copilot/Codex | Concluido | `AGENTS.md` | `.opencode/README.md` e agentes apontam para Copilot instructions, specs, agents equivalentes e validações. |
| AI-006 | Docs | Documentar uso diario do harness | Concluido | `docs/ai/harness-engineering.md` | README e docs IA indicam `agent-init`, prompts, agents, skills, progresso e validacao. |
| AI-007 | Auditoria | Rodar proxima auditoria specs versus codigo | Concluido | `docs/audits/spec-adherence-refresh-2026-06-24.md` | Auditoria curta executada sem corrigir produto; gaps viraram backlog. |
| AI-008 | Segurança | Avaliar secret scanning dedicado no CI | Aberto | `docs/specs/011-security-and-compliance.md` | Considerar Gitleaks ou ferramenta equivalente alem do scan simples atual. |
| AI-009 | Harness | Evoluir OpenCode com agentes, skills e checks locais | Concluido | `docs/harness/opencode.md` | `opencode.json` usa `agent`, skills locais, permissao segura e scripts docs/architecture/smoke. |
| PH-SEC-001 | Segurança | Proteger `ApplicationClient.WebhookSecret` em repouso | Concluido | `docs/specs/011-security-and-compliance.md` / `docs/adr/ADR-0007-webhook-secret-protection.md` | Slices 6-C (2026-06-25) + 7-A.6 (2026-06-26) + 7-A.9 (2026-06-26). |
| PH-WORKER-001 | Worker | Substituir dispatcher no-op do Outbox worker | Concluido | `docs/specs/007-inbox-outbox-workers.md` / `docs/adr/ADR-0010-real-outbox-dispatcher-location.md` | Slice 7-A (2026-06-26, sub-slices 7-A.1 a 7-A.9). |
| PH-SEC-002 | Segurança | Validar assinatura de webhooks externos reais | Concluido | `docs/specs/006-provider-webhooks.md` | Slice 2-B CONCLUIDO 2026-06-29 — HMAC-SHA256 Base64 + fail-fast 401 + metadata routing para AbacatePay. Stripe/MercadoPago dependem de Phase 4. |
| PH-AUD-001 | Auditoria | Gravar `AuditLog` em acoes administrativas | Aberto | `docs/specs/012-observability-and-audit.md` | Criacao/alteracao de tenants, applications e provider accounts. |
| PH-PROVIDER-WEBHOOK-ABACATEPAY | Provider | Webhook externo AbacatePay com HMAC + normalizacao | Concluido | `docs/specs/006-provider-webhooks.md`, `docs/specs/008-provider-adapters.md`, `docs/specs/011-security-and-compliance.md` | Slice 2-B CONCLUIDO 2026-06-29. 78 testes novos distribuidos entre verifier, normalizer, adapter, handler e controller. Phase 2 passa a `IMPLEMENTED`. |
| PH-PROVIDER-WEBHOOK-E2E | Testes | Testes E2E do fluxo AbacatePay (API + Postgres + adapter + webhook) | Concluido | `docs/specs/013-testing-strategy.md`, `docs/specs/006-provider-webhooks.md` | Slice 3-IT CONCLUIDO 2026-06-29 — 4 testes P1 em `tests/PaymentHub.IntegrationTests/EndToEnd/AbacatePayCheckoutE2ETests.cs`. |
| PH-PROVIDER-WEBHOOK-RAWPAYLOAD-TEXT | Banco | Migrar `webhook_events.raw_payload` de `jsonb` para `text` (HMAC byte preservation) | Concluido | `docs/specs/010-database-contract.md` | **⚠️ ANTI-REGRESSION (BLOCKER for Phase 7-IT).** Slice 3-IT (2026-06-29) — bug encontrado por E2E testing. Postgres `jsonb` reformata JSON no insert (espacos, normaliza chaves), o que quebra HMAC sobre o body bruto. Migracao `20260629205545_ChangeRawPayloadToText` aplicada. **NUNCA** voltar a coluna para `jsonb`. Cobertura: `ProviderWebhook_ValidSignature_UpdatesPaymentAndEnqueuesOutbox`. Detalhes completos em `docs/audits/slice-3-it-e2e-api-postgres-outbox-provider-report-2026-06-29.md` (seção "Anti-Regression Notes" Rule 1). |
| PH-PROVIDER-WEBHOOK-ATTEMPT-TRACKING | Provider | Forcar `Add` explicito do novo `PaymentAttempt` no `ProcessWebhookEventHandler` | Concluido | `docs/specs/006-provider-webhooks.md` | **⚠️ ANTI-REGRESSION (BLOCKER for Phase 7-IT).** Slice 3-IT (2026-06-29) — bug encontrado por E2E testing. Collection navigation tracking do EF Core 10 nao detecta confiavelmente itens adicionados via `entity.Navigation.Add(item)`; o item aparecia como `Modified` em vez de `Added`, levantando `DbUpdateConcurrencyException`. Solucao: chamar `_payments.AddAttemptAsync(attempt, ct)` apos `payment.RegisterAttempt(...)`. **NUNCA** remover a chamada explicita. Cobertura: `ProviderWebhook_ValidSignature_UpdatesPaymentAndEnqueuesOutbox`. Detalhes completos em `docs/audits/slice-3-it-e2e-api-postgres-outbox-provider-report-2026-06-29.md` (seção "Anti-Regression Notes" Rule 2). |
| PH-DB-001 | Banco | Decidir FKs obrigatorias versus referencias logicas | Aberto | `docs/specs/010-database-contract.md` | Atualizar spec/ADR antes de migrations. |
| DOC-001 | Docs | Alinhar overview ao HMAC interno atual | Aberto | `docs/specs/011-security-and-compliance.md` | Usar contrato `{timestamp}.{rawBody}` com headers atuais. |

## Convenções

- Status sugeridos: `Aberto`, `Planejado`, `Em progresso`, `Bloqueado`, `Concluido`.
- Cada item deve apontar spec, ADR ou doc de harness quando existir.
- Nao use este arquivo como substituto de issue tracker quando houver GitHub Issues.
