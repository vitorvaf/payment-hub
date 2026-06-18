# Payment Hub — Roadmap Geral

## Visao geral

Este documento cobre as 11 fases do Payment Hub (Phase 0 a Phase 10), desde a fundacao arquitetural ate evolucoes futuras de produto. Cada fase possui objetivo, escopo, dependencias, metricas e status atual baseado no estado do repositorio em 2026-06-17.

Status possivel: `NOT_STARTED` | `DISCOVERY` | `SPEC_DRAFTED` | `SPEC_REVIEW_REQUIRED` | `READY_FOR_IMPLEMENTATION` | `IMPLEMENTING` | `IMPLEMENTED` | `VALIDATED` | `BLOCKED` | `DEFERRED`

### Definicao dos status de phase

| Status | Significado operacional |
| ------ | ----------------------- |
| `IMPLEMENTING` | Codigo em desenvolvimento; testes parciais; pode ter gaps P1 abertos pertencentes a esta phase. |
| `IMPLEMENTED` | Codigo mesclado e build/testes passando. Pode conter gaps P1 **pertencentes a outra phase** (ex.: Phase 1 tem gaps de seguranca que sao escopo da Phase 6). **Nao significa pronto para producao.** |
| `VALIDATED` | `IMPLEMENTED` + todos os criterios de aceite verificados + nenhum gap P1 aberto de responsabilidade desta phase. Unico status que indica que a phase pode ser considerada completa para fins operacionais. |

> **Slice 6-A (2026-06-17):** gap P1-1 (Tenant/application inativos nao bloqueiam fluxos autenticados) foi resolvido. O `ApiKeyAuthenticationMiddleware` agora consulta `Tenant.Status` e `ApplicationClient.Status` apos validar a API Key e aplica `403 Forbidden` para tenant ou application inativos. Phase 6 segue `IMPLEMENTING` porque os gaps P1-2, P1-3 e P1-5 continuam abertos.

> **Regra para o agente:** `IMPLEMENTED` nao equivale a `VALIDATED`. Uma phase marcada como `IMPLEMENTED` pode ainda ter gaps que a impedem de ir para producao. Verificar sempre a secao "Gaps conhecidos" da phase e os registros em `docs/audits/spec-adherence-audit-2026-06-17.md` antes de tratar a phase como finalizada.

---

## Phase 0 — Produto, Arquitetura e Fronteiras

| Campo | Valor |
|-------|-------|
| Status | `IMPLEMENTED` |
| Prioridade | P0 |
| Esforco | M |
| Risco | LOW |

> **Nota:** Phase 0 esta implementada. Ha 1 gap P2 aberto: `docs/architecture/overview.md` descreve formato antigo de assinatura HMAC (Slice documental pendente). Nenhum gap P0 ou P1.

### Objetivo

Estabelecer as fronteiras do produto, decisoes arquiteturais fundamentais e vocabulario compartilhado.

### Escopo

- Definicao de MVP scope (`000-mvp-scope.md`).
- Glossario e limites de responsabilidade (`001-glossary-and-boundaries.md`).
- Decisoes arquiteturais: .NET 10, EF Core 10, Postgres Inbox/Outbox, hosted checkout only, API Key S2S, canonicalizacao de status.
- ADRs 0001 a 0005.
- Overview de arquitetura e camadas.
- Stack tecnologica e regras de dependencia.

### Fora de escopo

- Implementacao de qualquer funcionalidade de negocio.
- Definicao de integracao com provedores reais.

### Entregaveis-chave

- `docs/specs/000-mvp-scope.md`
- `docs/specs/001-glossary-and-boundaries.md`
- `docs/adr/ADR-0001` a `ADR-0005`
- `docs/architecture/overview.md`
- `docs/architecture/mvp-decisions.md`
- `docs/harness/project-context.md`

### Dependencias

Nenhuma.

### Observacoes

Phase 0 esta completa. Existe gap de documentacao P3: `docs/architecture/overview.md` referencia formato antigo de assinatura HMAC interno. Correcao sugerida no proximo slice documental.

---

## Phase 1 — Core Domain MVP e API

| Campo | Valor |
|-------|-------|
| Status | `IMPLEMENTED` |
| Prioridade | P0 |
| Esforco | L |
| Risco | MEDIUM |

> **Atencao — Gaps P1 abertos:** Phase 1 esta implementada no nucleo de dominio, mas possui 2 gaps P1 de autorizacao/seguranca que ainda nao foram corrigidos.
>
> - ~~**P1-1** — Tenant/application inativos nao bloqueiam fluxos autenticados (`ApiKeyAuthenticationMiddleware`).~~ `[RESOLVIDO 2026-06-17 — Slice 6-A]`
> - **P1-2** — `RegisterProviderAccountHandler` usa tenant/application do corpo da requisicao, nao do contexto autenticado.
> - **P1-3** — Endpoints de criacao de tenant/application nao tem politica de autenticacao definida (deadlock de bootstrap).
>
> P1-1 foi resolvido pelo Slice 6-A (enforcement de `TenantStatus.Active` e `ApplicationStatus.Active` no middleware). Os gaps P1-2 e P1-3 serao resolvidos pelos Slices 6-B e 6-D de Phase 6. Phase 1 nao deve ser considerada `VALIDATED` enquanto esses gaps estiverem abertos. Ver `docs/audits/spec-adherence-audit-2026-06-17.md` para detalhes.

### Objetivo

Implementar entidades de dominio, enums, invariantes, repositorios, API autenticada e checkout hospedado com provider Fake.

### Escopo

- Entidades: `Tenant`, `ApplicationClient`, `ApiKey`, `ProviderAccount`, `Payment`, `PaymentAttempt`, `IdempotencyKey`.
- Enums canonicos: `PaymentStatus`, `PaymentAttemptStatus`, `ProviderCode`, `ProviderEnvironment`, `TenantStatus`, `ApplicationStatus`.
- Servicos de dominio: `PaymentStatusMapper`, `PaymentStatusTransitionPolicy`.
- Middleware de autenticacao por API Key.
- Controllers: `CheckoutsController`, `PaymentsController`, `TenantsController`, `ApplicationsController`, `ProviderAccountsController`, `HealthController`.
- Handlers de aplicacao: `CreateCheckoutHandler`.
- Provider Fake funcional.
- Idempotencia por `Idempotency-Key`.
- Schema inicial via EF Core 10 migration.

### Fora de escopo

- Provedores reais.
- Workers asincronos.
- Testes de integracao.

### Entregaveis-chave

- `src/PaymentHub.Domain/`
- `src/PaymentHub.Application/Checkouts/`
- `src/PaymentHub.Api/`
- `src/PaymentHub.Infrastructure.Postgres/`
- `src/PaymentHub.Infrastructure.Providers/Fake/`
- Migration inicial: `20260616232151_InitialSchema.cs`
- 64 testes unitarios passando

### Dependencias

- Phase 0

### Gaps conhecidos (P1)

- ~~Tenant/application inativos nao bloqueiam fluxos autenticados.~~ `[RESOLVIDO 2026-06-17 — Slice 6-A]`
- `RegisterProviderAccountHandler` usa tenant/application do corpo, nao do contexto autenticado.
- Endpoints de criacao de tenant/application divergem entre spec e middleware quanto a autenticacao.

---

## Phase 2 — Primeiro Adapter de Provider

| Campo | Valor |
|-------|-------|
| Status | `IMPLEMENTING` |
| Prioridade | P0 |
| Esforco | M |
| Risco | MEDIUM |

### Objetivo

Tornar pelo menos um provider real (AbacatePay) funcional end-to-end em ambiente sandbox.

### Escopo

- Implementacao completa do adapter AbacatePay: `CreateCheckoutAsync`, `ParseWebhookAsync`.
- Validacao de assinatura de webhook AbacatePay.
- Testes unitarios do adapter.
- Documentacao de endpoints e autenticacao AbacatePay.

### Fora de escopo

- Stripe e MercadoPago na mesma fase.
- Ambiente de producao.

### Entregaveis-chave

- `src/PaymentHub.Infrastructure.Providers/AbacatePay/AbacatePayAdapter.cs` (funcional)
- Testes de adapter e mapeamento de status
- Documentacao de webhook AbacatePay

### Dependencias

- Phase 1

### Gaps conhecidos (P2)

- Adapters AbacatePay, Stripe e MercadoPago sao skeleton; nenhum valida assinatura de webhook externo.

---

## Phase 3 — Webhooks Externos e Internos

| Campo | Valor |
|-------|-------|
| Status | `IMPLEMENTING` |
| Prioridade | P0 |
| Esforco | M |
| Risco | MEDIUM |

### Objetivo

Completar o ciclo de Inbox (webhook externo) e Outbox (webhook interno) com dispatcher HTTP real.

### Escopo

- `WebhookProcessorWorker`: selecao por `next_retry_at`, processamento assincrono, retry policy.
- `OutboxDispatcherWorker`: dispatcher HTTP real substituindo `NoopApplicationWebhookDispatcher`.
- Assinatura HMAC de webhooks internos.
- `ProcessWebhookEventHandler` com deduplicacao, tratamento de fora de ordem.
- Testes unitarios de workers e handler.

### Fora de escopo

- Validacao de assinatura de webhooks de providers reais (tratado na Phase 2 por provider).

### Entregaveis-chave

- `src/PaymentHub.Worker/WebhookProcessorWorker.cs` (funcional)
- `src/PaymentHub.Worker/OutboxDispatcherWorker.cs` com dispatcher HTTP real
- `src/PaymentHub.Application/Webhooks/ProcessWebhookEventHandler.cs`
- Testes do handler

### Dependencias

- Phase 1
- Phase 2 (para validar com provider real)

### Gaps conhecidos (P1)

- Worker dedicado de outbox registra `NoopApplicationWebhookDispatcher`; eventos internos podem ser marcados como enviados sem entrega HTTP real.

---

## Phase 4 — Multi-Provider

| Campo | Valor |
|-------|-------|
| Status | `SPEC_DRAFTED` |
| Prioridade | P1 |
| Esforco | L |
| Risco | MEDIUM |

### Objetivo

Habilitar Stripe e MercadoPago como providers funcionais, com roteamento e selecao de provider por tenant/application.

### Escopo

- Adapter Stripe funcional: checkout, webhook, assinatura.
- Adapter MercadoPago funcional: checkout, webhook, assinatura.
- Roteamento multi-provider por `ProviderAccount` ativa.
- Testes de integracao minimos por adapter.
- Documentacao de webhooks por provider.

### Fora de escopo

- Painel admin de configuracao de provider.
- Split financeiro.

### Entregaveis-chave

- `src/PaymentHub.Infrastructure.Providers/Stripe/StripeAdapter.cs` (funcional)
- `src/PaymentHub.Infrastructure.Providers/MercadoPago/MercadoPagoAdapter.cs` (funcional)
- Testes por adapter
- ADR sobre selecao de provider default

### Dependencias

- Phase 2 (AbacatePay como referencia de implementacao)
- Phase 3 (Outbox funcionando)

---

## Phase 5 — Painel Admin

| Campo | Valor |
|-------|-------|
| Status | `NOT_STARTED` |
| Prioridade | P2 |
| Esforco | XL |
| Risco | MEDIUM |

### Objetivo

Criar interface administrativa para gestao de tenants, applications, API keys, provider accounts e monitoramento de pagamentos.

### Escopo

- UI de gestao de tenants e applications.
- Gestao de API Keys (criacao, rotacao, revogacao).
- Configuracao de provider accounts.
- Visualizacao de pagamentos e status.
- Audit log visivel para operadores.

### Fora de escopo

- Relatorios financeiros completos (Phase 8).
- Painel de metricas em tempo real (Phase 9).

### Entregaveis-chave

- Frontend admin (tecnologia a definir).
- Endpoints admin autenticados (politica admin separada do S2S).
- ADR sobre autenticacao do painel admin.

### Dependencias

- Phase 1 (API base)
- Phase 6 (seguranca endurecida)

---

## Phase 6 — Seguranca e Confiabilidade

| Campo | Valor |
|-------|-------|
| Status | `IMPLEMENTING` |
| Prioridade | P1 |
| Esforco | M |
| Risco | HIGH |

### Objetivo

Corrigir os gaps de seguranca e autorizacao identificados na auditoria e enrijecer o sistema para uso operacional.

### Escopo

- Enforcement de `TenantStatus.Active` e `ApplicationStatus.Active` no middleware.
- Correcao de `RegisterProviderAccountHandler` para derivar tenant/application do contexto autenticado.
- Protecao de `ApplicationClient.WebhookSecret` em repouso (criptografia ou KMS).
- Politica explicita para endpoints de bootstrap/admin.
- Gravacao de `AuditLog` em handlers administrativos (tenant, application, API key, provider account).
- Testes de seguranca: tenant inativo, application inativa, divergencia de escopo.

### Fora de escopo

- Certificacao PCI formal.
- Antifraude.

### Entregaveis-chave

- Middleware atualizado com verificacao de status ativo.
- Handler de provider account corrigido.
- `AuditLog` gravado em acoes administrativas.
- Protecao de `webhook_secret`.
- Documentacao de politica bootstrap/admin.

### Dependencias

- Phase 1

### Gaps conhecidos (P1 criticos)

Esta phase e responsavel por 4 dos 5 gaps P1 da auditoria de 2026-06-17:

- **P1-1** — Tenant/application inativos nao bloqueiam fluxos autenticados → Slice 6-A. `[RESOLVIDO 2026-06-17]`
- **P1-2** — `RegisterProviderAccountHandler` usa tenant/application do body → Slice 6-B.
- **P1-3** — Endpoints de bootstrap/admin sem politica de autenticacao → Slice 6-D + ADR-0006.
- **P1-5** — `ApplicationClient.WebhookSecret` persistido em texto claro → Slice 6-C + ADR-0007.

O gap P1-4 (`NoopApplicationWebhookDispatcher`) e de responsabilidade da Phase 7 (Slice 7-A), nao desta phase. No entanto, o Slice 6-C (protecao de `WebhookSecret`) e prerequisito para que o Slice 7-A possa ser validado de forma segura, pois o dispatcher HTTP usara o secret para assinar os webhooks internos.

Ver `docs/audits/spec-adherence-audit-2026-06-17.md` para detalhes de cada achado.

---

## Phase 7 — Workers, Outbox e Processamento Assincrono

| Campo | Valor |
|-------|-------|
| Status | `IMPLEMENTING` |
| Prioridade | P1 |
| Esforco | M |
| Risco | MEDIUM |

### Objetivo

Garantir que workers de Inbox e Outbox sejam idiomaticos, resilientes e com dispatcher HTTP real.

### Escopo

- `WebhookProcessorWorker`: selecao com `FOR UPDATE SKIP LOCKED` futuro ou lock transacional.
- `OutboxDispatcherWorker` com `IApplicationWebhookDispatcher` HTTP real.
- Retry policy conforme spec (0s, 1m, 5m, 15m, 1h, Failed).
- Mecanismo de lock contra duplo processamento.
- Testes unitarios de workers.
- Testes de integracao com Postgres para workers.

### Fora de escopo

- Broker externo (RabbitMQ, Kafka, Azure Service Bus) no MVP.

### Entregaveis-chave

- Dispatcher HTTP real registrado no Worker host.
- Testes de retry e falha permanente.
- Documentacao de configuracao de workers.

### Dependencias

- Phase 3 (baseline do worker) — obrigatoria para iniciar qualquer slice desta phase.
- Phase 6 Slice 6-C (protecao de `WebhookSecret`) — obrigatoria apenas para VALIDAR esta phase; Slice 7-A pode ser executado antes de Slice 6-C ser concluido.

> **Nota de dependencia parcial:** A dependencia em Phase 6 nao e total. Slice 7-A (substituir `NoopApplicationWebhookDispatcher` por HTTP real) pode comecar antes que Phase 6 esteja concluida. A restricao e: Phase 7 so pode ser marcada como `VALIDATED` depois que o Slice 6-C estiver concluido, pois o dispatcher HTTP real usara `WebhookSecret` para assinar os payloads. Iniciar Phase 7 antes de Phase 6 e intencional — ver `docs/roadmap/001-development-timeline.md`, secao "Timeline Decision".

### Gaps conhecidos (P1)

- **P1-4** — `NoopApplicationWebhookDispatcher` registrado no Worker host (gap P1 compartilhado com Phase 3, de responsabilidade desta phase) → Slice 7-A.

---

## Phase 8 — Conciliacao Financeira

| Campo | Valor |
|-------|-------|
| Status | `NOT_STARTED` |
| Prioridade | P2 |
| Esforco | XL |
| Risco | HIGH |

### Objetivo

Implementar conciliacao financeira entre pagamentos registrados no Payment Hub e extratos de providers.

### Escopo

- Job de conciliacao periodica.
- Importacao de extratos de provider (quando disponivel via API).
- Identificacao de divergencias: pagamento aprovado sem webhook, webhook sem pagamento.
- Relatorio de conciliacao.
- Spec formal de conciliacao.

### Fora de escopo

- Custodia de saldo.
- Split financeiro.
- Contabilidade.

### Entregaveis-chave

- Spec `015-financial-reconciliation.md`.
- Job de conciliacao.
- Relatorio de divergencias.

### Dependencias

- Phase 4 (multi-provider funcional)
- Phase 7 (workers confiaveis)

---

## Phase 9 — Relatorios, Metricas e Observabilidade

| Campo | Valor |
|-------|-------|
| Status | `SPEC_DRAFTED` |
| Prioridade | P2 |
| Esforco | L |
| Risco | LOW |

### Objetivo

Adicionar observabilidade operacional: metricas, tracing distribuido, alertas e relatorios de pagamento.

### Escopo

- Integracao com OpenTelemetry.
- Metricas de checkout (volume, taxa de sucesso, latencia).
- Metricas de workers (pendentes, falhas, latencia de processamento).
- Alertas operacionais (fila crescendo, falhas permanentes).
- Relatorios basicos de pagamento por tenant/application.
- Spec `012-observability-and-audit.md` ja existe; esta phase implementa o que falta.

### Fora de escopo

- BI completo.
- Relatorios financeiros (Phase 8).

### Entregaveis-chave

- Instrumentacao OpenTelemetry no dominio e handlers.
- Dashboard basico (Prometheus/Grafana ou equivalente).
- Relatorio de pagamentos por periodo.

### Dependencias

- Phase 6 (audit log funcional)
- Phase 7 (workers funcionais)

---

## Phase 10 — Evolucoes Futuras de Produto (Backlog)

| Campo | Valor |
|-------|-------|
| Status | `NOT_STARTED` |
| Prioridade | P3 |
| Esforco | XL |
| Risco | HIGH |

### Objetivo

Agrupar evolucoes de produto post-MVP que dependem de validacao de mercado ou decisao de produto.

### Escopo (backlog, sem compromisso de entrega)

- Broker externo: migracao de Outbox para RabbitMQ, Kafka ou Azure Service Bus.
- Recorrencia/assinaturas.
- Wallet interno.
- Split financeiro.
- Antifraude integrado.
- Cartao salvo (tokenizacao).
- Portal de autoatendimento para tenants.
- SDK client para consumidores.
- Internacionalizacao e multiplas moedas.
- Certificacao PCI-DSS nivel mais alto.

### Fora de escopo

- Qualquer item desta lista no MVP.

### Entregaveis-chave

- Specs individuais conforme prioridade de produto.
- ADRs para decisoes arquiteturais de cada evolucao.

### Dependencias

- Phases 0-9 como baseline.

---

## Resumo de status

| Phase | Nome | Status | Prioridade | Esforco | Risco |
|-------|------|--------|-----------|---------|-------|
| 0 | Produto, Arquitetura e Fronteiras | `IMPLEMENTED` | P0 | M | LOW |
| 1 | Core Domain MVP e API | `IMPLEMENTED` | P0 | L | MEDIUM |
| 2 | Primeiro Adapter de Provider | `IMPLEMENTING` | P0 | M | MEDIUM |
| 3 | Webhooks Externos e Internos | `IMPLEMENTING` | P0 | M | MEDIUM |
| 4 | Multi-Provider | `SPEC_DRAFTED` | P1 | L | MEDIUM |
| 5 | Painel Admin | `NOT_STARTED` | P2 | XL | MEDIUM |
| 6 | Seguranca e Confiabilidade | `IMPLEMENTING` | P1 | M | HIGH |
| 7 | Workers, Outbox e Processamento Assincrono | `IMPLEMENTING` | P1 | M | MEDIUM |
| 8 | Conciliacao Financeira | `NOT_STARTED` | P2 | XL | HIGH |
| 9 | Relatorios, Metricas e Observabilidade | `SPEC_DRAFTED` | P2 | L | LOW |
| 10 | Evolucoes Futuras de Produto | `NOT_STARTED` | P3 | XL | HIGH |

## Arquivos relacionados

- `docs/roadmap/001-development-timeline.md`
- `docs/roadmap/002-phase-status-board.md`
- `docs/specs/000-mvp-scope.md`
- `docs/audits/spec-adherence-audit-2026-06-17.md`
- `docs/audits/roadmap-adherence-matrix-2026-06-17.md`
