# Matriz de Aderencia do Roadmap — 2026-06-17

## Objetivo

Comparar o estado atual do repositorio com as fases do roadmap, identificando para cada area se existe spec, codigo e testes cobrindo o item.

Fontes:
- `docs/audits/spec-adherence-audit-2026-06-17.md` — auditoria detalhada
- `docs/roadmap/000-payment-hub-roadmap.md` — definicao das fases

Legenda de status:
- `OK` — existe e esta aderente
- `PARTIAL` — existe, mas com gaps
- `MISSING` — nao existe
- `SKELETON` — estrutura presente, sem implementacao funcional
- `N/A` — nao aplicavel para o item/fase

---

## Phase 0 — Produto, Arquitetura e Fronteiras

| Item | Spec existe? | Codigo existe? | Testes existem? | Status | Prioridade | Esforco | Risco | Notas |
|------|-------------|---------------|----------------|--------|-----------|---------|-------|-------|
| Escopo MVP definido | OK (`000-mvp-scope.md`) | N/A | N/A | `OK` | P0 | XS | LOW | Spec aceita |
| Glossario e limites | OK (`001-glossary-and-boundaries.md`) | N/A | N/A | `OK` | P0 | XS | LOW | Spec aceita |
| Visao de produto | OK (`001-product-vision-and-boundaries.md`) | N/A | N/A | `OK` | P0 | XS | LOW | Criado neste bootstrap |
| ADR .NET 10 + EF Core | OK (`ADR-0001`) | OK | N/A | `OK` | P0 | XS | LOW | Aceito |
| ADR Postgres Inbox/Outbox | OK (`ADR-0002`) | OK | N/A | `OK` | P0 | XS | LOW | Aceito |
| ADR Hosted Checkout | OK (`ADR-0003`) | OK (sem cartao/CVV) | OK (busca estatica) | `OK` | P0 | XS | LOW | Aceito |
| ADR API Key S2S | OK (`ADR-0004`) | OK | OK (middleware tests) | `OK` | P0 | XS | LOW | Aceito |
| ADR canonicalizacao de status | OK (`ADR-0005`) | OK (`PaymentStatusMapper`) | OK (mapper tests) | `OK` | P0 | XS | LOW | Aceito |
| Overview de arquitetura | OK (`overview.md`) | N/A | N/A | `PARTIAL` | P3 | XS | LOW | Doc HMAC desatualizada (P2-5 da auditoria) |
| Decisoes do MVP | OK (`mvp-decisions.md`) | N/A | N/A | `OK` | P0 | XS | LOW | Atualizado |

---

## Phase 1 — Core Domain MVP e API

| Item | Spec existe? | Codigo existe? | Testes existem? | Status | Prioridade | Esforco | Risco | Notas |
|------|-------------|---------------|----------------|--------|-----------|---------|-------|-------|
| Modelo de dominio | OK (`003-domain-model.md`) | OK (entidades, enums, services) | OK (unitarios) | `OK` | P0 | M | MEDIUM | Entidades: Tenant, ApplicationClient, ApiKey, ProviderAccount, Payment, PaymentAttempt, IdempotencyKey, WebhookEvent, OutboxEvent, AuditLog |
| Ciclo de vida do pagamento | OK (`004-payment-lifecycle.md`) | OK (`PaymentStatus`, `PaymentStatusTransitionPolicy`) | OK (transition tests) | `OK` | P0 | S | MEDIUM | Status canonico com 11 valores |
| Multitenancy e autenticacao | OK (`002-multitenancy-and-authentication.md`) | PARTIAL | PARTIAL | `PARTIAL` | P1 | S | HIGH | Isolamento existe; falta enforcement de status ativo (P1-1) |
| API Key hash e autenticacao | OK (`ADR-0004`) | OK (`HmacApiKeyHasher`, middleware) | OK | `PARTIAL` | P1 | S | HIGH | Nao valida status ativo de tenant/application (P1-1) |
| Checkout hospedado | OK (`005-checkout-creation.md`) | OK (`CreateCheckoutHandler`) | OK (unitarios) | `PARTIAL` | P1 | M | MEDIUM | Idempotencia coberta; falta teste API/e2e; falta bloqueio por tenant/app inativo |
| Provider accounts | OK (`009-api-contracts.md`) | PARTIAL | PARTIAL | `PARTIAL` | P1 | S | HIGH | Handler usa tenant/app do body, nao do contexto autenticado (P1-2) |
| Contratos de API | OK (`009-api-contracts.md`) | OK (controllers) | PARTIAL | `PARTIAL` | P1 | S | MEDIUM | Falta teste de integracao de API |
| Schema de banco | OK (`010-database-contract.md`) | OK (migration inicial) | MISSING | `PARTIAL` | P2 | M | MEDIUM | FKs parciais (P2-4); sem testes de integracao de banco |
| Idempotencia de checkout | OK (`005-checkout-creation.md`) | OK | OK (unitarios) | `OK` | P0 | S | MEDIUM | Coberto em `CreateCheckoutHandlerTests` |
| Estrategia de testes | OK (`013-testing-strategy.md`) | PARTIAL | PARTIAL | `PARTIAL` | P1 | L | MEDIUM | 64 unitarios ok; integracao vazia (P2-2) |
| Integracao Job Search | OK (`014-job-search-integration.md`) | N/A (consumidor externo) | MISSING | `PARTIAL` | P2 | M | MEDIUM | Falta teste e2e com Fake |

---

## Phase 2 — Primeiro Adapter de Provider

| Item | Spec existe? | Codigo existe? | Testes existem? | Status | Prioridade | Esforco | Risco | Notas |
|------|-------------|---------------|----------------|--------|-----------|---------|-------|-------|
| Interface de adapter | OK (`008-provider-adapters.md`) | OK (`IPaymentProviderAdapter`) | N/A | `OK` | P0 | XS | LOW | Interface definida |
| Provider Fake funcional | OK (`008-provider-adapters.md`) | OK | OK (adapter tests) | `OK` | P0 | S | LOW | Fake e referencia de implementacao |
| Provider Router | OK (implicitamente) | OK (`PaymentProviderRouter`) | OK | `OK` | P0 | S | LOW | Roteamento por ProviderCode |
| Mapeamento de status Fake | OK (`ADR-0005`) | OK (`PaymentStatusMapper`) | OK | `OK` | P0 | S | LOW | Coberto em testes |
| Adapter AbacatePay funcional | OK (`008-provider-adapters.md`) | SKELETON | MISSING | `SKELETON` | P1 | M | MEDIUM | Skeleton presente; sem implementacao real nem validacao de assinatura (P2-1) |
| Adapter Stripe funcional | OK (`008-provider-adapters.md`) | SKELETON | MISSING | `SKELETON` | P1 | M | MEDIUM | Skeleton presente (Phase 4) |
| Adapter MercadoPago funcional | OK (`008-provider-adapters.md`) | SKELETON | MISSING | `SKELETON` | P1 | M | MEDIUM | Skeleton presente (Phase 4) |
| Validacao assinatura webhook externo | OK (`006-provider-webhooks.md`) | MISSING | MISSING | `MISSING` | P2 | M | HIGH | Nao validado em nenhum adapter real (P2-1) |

---

## Phase 3 — Webhooks Externos e Internos

| Item | Spec existe? | Codigo existe? | Testes existem? | Status | Prioridade | Esforco | Risco | Notas |
|------|-------------|---------------|----------------|--------|-----------|---------|-------|-------|
| Endpoint webhook externo | OK (`006-provider-webhooks.md`) | OK (`ProviderWebhooksController`) | PARTIAL | `PARTIAL` | P0 | S | MEDIUM | Recebimento e persistencia ok; falta teste e2e |
| Inbox de webhooks | OK (`007-inbox-outbox-workers.md`) | OK (`webhook_events`) | PARTIAL | `PARTIAL` | P0 | S | MEDIUM | Deduplicacao por (provider_code, provider_event_id) existe |
| Processamento assincrono de webhook | OK (`006-provider-webhooks.md`) | OK (`WebhookProcessorWorker`) | OK (unitarios) | `PARTIAL` | P0 | M | MEDIUM | Worker existe; falta teste de integracao |
| `ProcessWebhookEventHandler` | OK (`006-provider-webhooks.md`) | OK | OK (9 testes unitarios) | `OK` | P0 | M | MEDIUM | Deduplicacao, fora de ordem e status canonico cobertos |
| Outbox de eventos internos | OK (`007-inbox-outbox-workers.md`) | OK (`outbox_events`) | PARTIAL | `PARTIAL` | P0 | M | MEDIUM | Criacao ok; dispatch via noop (P1-4) |
| Dispatcher HTTP real | OK (`007-inbox-outbox-workers.md`) | MISSING | MISSING | `MISSING` | P1 | M | HIGH | `NoopApplicationWebhookDispatcher` no Worker host (P1-4) |
| Assinatura HMAC webhook interno | OK (`007-inbox-outbox-workers.md`, `011-security.md`) | OK (signer implementado) | PARTIAL | `PARTIAL` | P1 | S | MEDIUM | Signer existe; dispatcher real ausente |
| Retry policy | OK (`007-inbox-outbox-workers.md`) | OK (`RetryPolicy`) | OK | `OK` | P1 | S | MEDIUM | 5 tentativas com backoff |

---

## Phase 4 — Multi-Provider

| Item | Spec existe? | Codigo existe? | Testes existem? | Status | Prioridade | Esforco | Risco | Notas |
|------|-------------|---------------|----------------|--------|-----------|---------|-------|-------|
| Spec de roteamento multi-provider | MISSING (`016-multi-provider-routing.md` nao existe) | PARTIAL (`PaymentProviderRouter`) | PARTIAL | `PARTIAL` | P1 | M | MEDIUM | Roteamento basico existe; spec formal pendente |
| Stripe funcional | SKELETON (`008-provider-adapters.md`) | SKELETON | MISSING | `NOT_STARTED` | P1 | M | MEDIUM | Aguarda Phase 2 como referencia |
| MercadoPago funcional | SKELETON (`008-provider-adapters.md`) | SKELETON | MISSING | `NOT_STARTED` | P1 | M | MEDIUM | Aguarda Phase 2 como referencia |
| Selecao de provider default | OK (`005-checkout-creation.md`) | PARTIAL | PARTIAL | `PARTIAL` | P1 | S | MEDIUM | Logica existe; falta validacao em producao sem Fake |

---

## Phase 5 — Painel Admin

| Item | Spec existe? | Codigo existe? | Testes existem? | Status | Prioridade | Esforco | Risco | Notas |
|------|-------------|---------------|----------------|--------|-----------|---------|-------|-------|
| Spec de painel admin | MISSING (`017-admin-panel.md` nao existe) | MISSING | MISSING | `NOT_STARTED` | P2 | XL | MEDIUM | Aguarda Phase 6 e decisao de autenticacao admin |
| Autenticacao admin | MISSING (D-03 pendente) | MISSING | MISSING | `NOT_STARTED` | P2 | M | MEDIUM | ADR necessaria |

---

## Phase 6 — Seguranca e Confiabilidade

| Item | Spec existe? | Codigo existe? | Testes existem? | Status | Prioridade | Esforco | Risco | Notas |
|------|-------------|---------------|----------------|--------|-----------|---------|-------|-------|
| Spec de seguranca | OK (`011-security-and-compliance.md`) | PARTIAL | PARTIAL | `PARTIAL` | P1 | M | HIGH | Spec aceita; implementacao incompleta |
| Enforcement de status ativo | OK (`002-multitenancy.md`) | MISSING | MISSING | `MISSING` | P1 | S | HIGH | Gap P1-1 — tenant/application inativo pode criar checkout |
| Provider account via contexto autenticado | OK (`002-multitenancy.md`) | MISSING | MISSING | `MISSING` | P1 | S | HIGH | Gap P1-2 — body substitui contexto autenticado |
| Politica bootstrap/admin | PARTIAL (D-01 pendente) | PARTIAL | MISSING | `MISSING` | P1 | M | HIGH | Gap P1-3 — bootstrap sem caminho claro |
| Protecao de `WebhookSecret` | MISSING (D-02 pendente) | MISSING | MISSING | `MISSING` | P1 | M | HIGH | Gap P1-5 — texto claro no banco |
| `AuditLog` em acoes administrativas | OK (`011-security.md`, `012-observability.md`) | PARTIAL (infra existe) | MISSING | `MISSING` | P2 | M | MEDIUM | Gap P2-3 — entidade existe, nao e gravada |
| API Key hash | OK (`ADR-0004`) | OK (`HmacApiKeyHasher`) | OK | `OK` | P0 | S | LOW | Implementado |
| Credenciais provider criptografadas | OK (`ADR-0001`, `mvp-decisions.md`) | OK (`AesCredentialProtector`) | PARTIAL | `OK` | P0 | S | LOW | AES-256-CBC implementado |
| HTTPS em producao | OK (`011-security.md`) | N/A (config) | N/A | `OK` | P1 | XS | LOW | Regra de config, nao de codigo |
| Logs sem secrets | OK (`011-security.md`) | PARTIAL | N/A | `PARTIAL` | P1 | S | MEDIUM | Auditoria nao encontrou secrets em logs, mas cobertura e parcial |

---

## Phase 7 — Workers, Outbox e Processamento Assincrono

| Item | Spec existe? | Codigo existe? | Testes existem? | Status | Prioridade | Esforco | Risco | Notas |
|------|-------------|---------------|----------------|--------|-----------|---------|-------|-------|
| Spec de workers | OK (`007-inbox-outbox-workers.md`) | OK | PARTIAL | `PARTIAL` | P1 | M | MEDIUM | Spec aceita; dispatcher real ausente |
| `WebhookProcessorWorker` | OK | OK | PARTIAL | `PARTIAL` | P1 | M | MEDIUM | Funcional; falta lock transacional e testes de integracao |
| `OutboxDispatcherWorker` | OK | OK | PARTIAL | `PARTIAL` | P1 | M | HIGH | Funcional; usa noop (P1-4) |
| Dispatcher HTTP real | OK (`007-inbox-outbox-workers.md`) | MISSING | MISSING | `MISSING` | P1 | M | HIGH | Gap P1-4 critico |
| Retry e backoff | OK | OK (`RetryPolicy`) | OK | `OK` | P1 | S | MEDIUM | 5 tentativas implementadas |
| Testes de integracao de workers | OK (`013-testing-strategy.md`) | MISSING | MISSING | `MISSING` | P2 | M | MEDIUM | Gap P2-2 |

---

## Phase 8 — Conciliacao Financeira

| Item | Spec existe? | Codigo existe? | Testes existem? | Status | Prioridade | Esforco | Risco | Notas |
|------|-------------|---------------|----------------|--------|-----------|---------|-------|-------|
| Spec de conciliacao | MISSING (`015-financial-reconciliation.md`) | MISSING | MISSING | `NOT_STARTED` | P2 | XL | HIGH | Aguarda Phase 4 + 7 |
| Job de conciliacao | MISSING | MISSING | MISSING | `NOT_STARTED` | P2 | L | HIGH | Aguarda spec |
| Importacao de extrato | MISSING | MISSING | MISSING | `NOT_STARTED` | P2 | L | HIGH | Depende de API do provider |

---

## Phase 9 — Relatorios, Metricas e Observabilidade

| Item | Spec existe? | Codigo existe? | Testes existem? | Status | Prioridade | Esforco | Risco | Notas |
|------|-------------|---------------|----------------|--------|-----------|---------|-------|-------|
| Spec de observabilidade | OK (`012-observability-and-audit.md`) | PARTIAL | MISSING | `PARTIAL` | P2 | L | LOW | Spec aceita; health checks existem; OpenTelemetry ausente |
| Health checks | OK | OK (`HealthController`) | MISSING | `PARTIAL` | P2 | XS | LOW | Endpoint existe; sem testes |
| Entidade AuditLog | OK | OK (infra existe) | MISSING | `PARTIAL` | P2 | S | MEDIUM | Nao e gravada por handlers admin (P2-3) |
| Metricas operacionais | OK (`012-observability.md`) | MISSING | MISSING | `NOT_STARTED` | P2 | M | LOW | OpenTelemetry nao configurado |
| Tracing distribuido | OK (`012-observability.md`) | MISSING | MISSING | `NOT_STARTED` | P2 | M | LOW | |

---

## Phase 10 — Evolucoes Futuras de Produto

| Item | Spec existe? | Codigo existe? | Testes existem? | Status | Prioridade | Esforco | Risco | Notas |
|------|-------------|---------------|----------------|--------|-----------|---------|-------|-------|
| Broker externo | MISSING (`018-external-broker.md`) | N/A | N/A | `NOT_STARTED` | P3 | XL | HIGH | `IApplicationWebhookDispatcher` ja abstrai o mecanismo |
| Recorrencia | MISSING | MISSING | MISSING | `NOT_STARTED` | P3 | XL | HIGH | Fora do MVP |
| Wallet | MISSING | MISSING | MISSING | `NOT_STARTED` | P3 | XL | HIGH | Fora do MVP |
| Split financeiro | MISSING | MISSING | MISSING | `NOT_STARTED` | P3 | XL | HIGH | Fora do MVP |
| Antifraude | MISSING | MISSING | MISSING | `NOT_STARTED` | P3 | XL | HIGH | Fora do MVP |

---

## Resumo de gaps por prioridade

### P0 — Nenhum gap aberto

Nao foram identificados gaps P0 comprovados na auditoria de 2026-06-17.

### P1 — 5 gaps abertos (todos em Phase 1/3/6/7)

| # | Gap | Phase | Slice sugerido |
|---|-----|-------|---------------|
| P1-1 | Tenant/application inativos nao bloqueiam fluxos autenticados | 1, 6 | Slice 6-A |
| P1-2 | `RegisterProviderAccountHandler` usa body ao inves do contexto autenticado | 1, 6 | Slice 6-B |
| P1-3 | Endpoints de bootstrap/admin sem politica explicita | 1, 6 | Slice 6-D |
| P1-4 | Worker outbox usa `NoopApplicationWebhookDispatcher` | 3, 7 | Slice 7-A |
| P1-5 | `ApplicationClient.WebhookSecret` em texto claro | 6 | Slice 6-C |

### P2 — 5 gaps relevantes

| # | Gap | Phase |
|---|-----|-------|
| P2-1 | Assinatura de webhooks externos nao validada nos adapters reais | 2, 4 |
| P2-2 | Projeto de testes de integracao sem testes | 1, 3, 7 |
| P2-3 | `AuditLog` nao e gravado em acoes administrativas | 6, 9 |
| P2-4 | Integridade referencial parcial no banco | 1 |
| P2-5 | Documentacao de arquitetura com formato antigo de HMAC | 0 |

---

## Arquivos relacionados

- `docs/audits/spec-adherence-audit-2026-06-17.md`
- `docs/roadmap/000-payment-hub-roadmap.md`
- `docs/roadmap/001-development-timeline.md`
- `docs/roadmap/002-phase-status-board.md`
