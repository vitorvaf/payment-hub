# Payment Hub — Auditoria de Estado Atual

Data: 2026-06-17

Este documento consolida o estado atual do repositorio, diferenciando claramente o que existe em codigo, o que existe em spec, o que existe em teste, o que e gap e o que e premissa nao validada.

Este documento e complementar a `spec-adherence-audit-2026-06-17.md`, que registra achados de aderencia de specs. Este documento foca no estado observavel do repositorio.

---

## Sumario executivo

O Payment Hub tem uma base de codigo funcional que cobre o fluxo central do MVP: criacao de checkout hospedado com provider Fake, autenticacao por API Key, persistencia em Postgres via EF Core 10, processamento assincrono de webhooks externos e publicacao de eventos internos via Outbox. O build esta limpo, 64 testes unitarios passam e a arquitetura segue Clean Architecture com separacao de camadas.

Os principais gaps sao: dispatcher HTTP real ausente no worker de Outbox (P1-4), protecao de `webhook_secret` ausente (P1-5), testes de integracao ausentes (P2) e adapters de providers reais ainda em skeleton (P2). O enforcement de status ativo de tenant/application (P1-1) foi resolvido pelo Slice 6-A em 2026-06-17: tenants e applications inativos sao bloqueados no middleware com `403 Forbidden`. O gap P1-2 (`ProviderAccount` usando tenant/application do body) foi resolvido pelo Slice 6-B em 2026-06-18: `ProviderAccount` agora e criado exclusivamente a partir de `ITenantContext` resolvido pelo middleware. O gap P1-3 (politica de bootstrap/admin seed) foi resolvido pelo Slice 6-D em 2026-06-18 via `IBootstrapPolicy` + `BootstrapOptions` + `IDevelopmentDataSeeder`. O gap P1-5 (protecao de `WebhookSecret`) foi resolvido pelo Slice 6-C em 2026-06-25: `IWebhookSecretProtector` + `AesWebhookSecretProtector` cifram o segredo antes de persistir.

---

## 1. O que existe em codigo

### 1.1 Dominio (`PaymentHub.Domain`)

#### Entidades implementadas

| Entidade | Arquivo | Observacoes |
| -------- | ------- | ----------- |
| `Tenant` | `Entities/Tenant.cs` | Status, slug, nome |
| `ApplicationClient` | `Entities/ApplicationClient.cs` | Pertence a Tenant; `WebhookSecret` em texto claro (gap P1) |
| `ApiKey` | `Entities/ApiKey.cs` | Hash HMAC; prefixo auditavel |
| `ProviderAccount` | `Entities/ProviderAccount.cs` | Credenciais criptografadas com `IDataProtectionProvider` |
| `Payment` | `Entities/Payment.cs` | Status canonico; valor como centavos (Money value object) |
| `PaymentAttempt` | `Entities/PaymentAttempt.cs` | Pertence a Payment; provider payment id; url de checkout |
| `WebhookEvent` | `Entities/WebhookEvent.cs` | Inbox externo; payload bruto; status de processamento |
| `OutboxEvent` | `Entities/OutboxEvent.cs` | Evento interno; retry policy; status de envio |
| `AuditLog` | `Entities/AuditLog.cs` | Entidade presente; handlers administrativos nao gravam (gap P2) |
| `IdempotencyKey` | `Entities/IdempotencyKey.cs` | Chave unica por tenant + application + key |

#### Enums implementados

| Enum | Valores |
| ---- | ------- |
| `PaymentStatus` | `Pending`, `Processing`, `Paid`, `Failed`, `Canceled`, `Expired`, `Refunded` |
| `PaymentAttemptStatus` | `Pending`, `Succeeded`, `Failed` |
| `OutboxEventStatus` | `Pending`, `Processing`, `Sent`, `Failed` |
| `WebhookProcessingStatus` | `Pending`, `Processing`, `Processed`, `Failed` |
| `ProviderCode` | `AbacatePay`, `Stripe`, `MercadoPago`, `Fake` |
| `ProviderEnvironment` | `Sandbox`, `Production` |
| `TenantStatus` | `Active`, `Suspended`, `Inactive` |
| `ApplicationStatus` | `Active`, `Suspended`, `Inactive` |

#### Servicos de dominio implementados

| Servico | Arquivo | Responsabilidade |
| ------- | ------- | ---------------- |
| `PaymentStatusMapper` | `Services/PaymentStatusMapper.cs` | Mapeia status de provider para `PaymentStatus` canonico |
| `PaymentStatusTransitionPolicy` | `Services/PaymentStatusTransitionPolicy.cs` | Valida transicoes de status permitidas |
| `RetryPolicy` | `Services/RetryPolicy.cs` | Define janelas de retry para WebhookEvent/OutboxEvent |

#### Value objects

| Value object | Arquivo | Responsabilidade |
| ------------ | ------- | ---------------- |
| `Money` | `ValueObjects/Money.cs` | Valor monetario em centavos com moeda |

### 1.2 Aplicacao (`PaymentHub.Application`)

#### Handlers implementados

| Handler | Arquivo | Responsabilidade |
| ------- | ------- | ---------------- |
| `CreateCheckoutHandler` | `Checkouts/CreateCheckoutHandler.cs` | Cria Payment, PaymentAttempt, IdempotencyKey; chama provider |
| `GetPaymentByIdHandler` | `Payments/GetPaymentByIdHandler.cs` | Consulta Payment por id com isolamento de tenant |
| `ListPaymentsHandler` | `Payments/ListPaymentsHandler.cs` | Lista Payments por tenant/application |
| `ProcessWebhookEventHandler` | `Webhooks/WebhookHandlers.cs` | Processa WebhookEvent, atualiza status, gera OutboxEvent |
| `RegisterTenantHandler` | `Tenants/RegisterTenantHandler.cs` | Cria Tenant (endpoint sem enforcement de bootstrap policy) |
| `RegisterApplicationClientHandler` | `Tenants/RegisterApplicationClientHandler.cs` | Cria ApplicationClient |
| `RegisterProviderAccountHandler` | `Tenants/RegisterProviderAccountHandler.cs` | Cria ProviderAccount (usa body, nao contexto autenticado — gap P1) |

#### Abstrações implementadas

| Interface | Arquivo | Responsabilidade |
| --------- | ------- | ---------------- |
| `ITenantContext` | `Abstractions/Context/ITenantContext.cs` | Contexto de tenant/application da request |
| `IRuntimeEnvironment` | `Abstractions/Context/IRuntimeEnvironment.cs` | Ambiente de execucao (Development, Production) |
| `IPaymentProviderAdapter` | `Abstractions/Providers/IPaymentProviderAdapter.cs` | Contrato de adapter de provider |
| `IPaymentOrchestrator` | `Abstractions/Providers/IPaymentOrchestrator.cs` | Orquestrador de selecao de provider |
| `IApplicationWebhookDispatcher` | `Abstractions/Outbox/IApplicationWebhookDispatcher.cs` | Dispatcher de webhook interno |
| `IOutboxPublisher` | `Abstractions/Outbox/IOutboxPublisher.cs` | Publicador de eventos no Outbox |
| `ICrypto` | `Abstractions/Security/ICrypto.cs` | Servico de criptografia |

### 1.3 API (`PaymentHub.Api`)

#### Controllers implementados

| Controller | Endpoints | Observacoes |
| ---------- | --------- | ----------- |
| `CheckoutsController` | `POST /api/v1/checkouts` | Autenticado; idempotencia por header |
| `PaymentsController` | `GET /api/v1/payments`, `GET /api/v1/payments/{id}` | Autenticado |
| `ProviderWebhooksController` | `POST /api/v1/webhooks/{provider}` | Anonimo (validado pelo provider); persiste WebhookEvent |
| `TenantsController` | `POST /api/v1/tenants` | Politica de autenticacao indefinida (gap P1) |
| `ApplicationsController` | `POST /api/v1/applications` | Politica de autenticacao indefinida (gap P1) |
| `ProviderAccountsController` | `POST /api/v1/provider-accounts` | Autenticado; aceita tenant/application do body (gap P1) |
| `HealthController` | `GET /health` | Anonimo |

#### Middlewares implementados

| Middleware | Arquivo | Responsabilidade |
| ---------- | ------- | ---------------- |
| `ApiKeyAuthenticationMiddleware` | `Auth/ApiKeyAuthenticationMiddleware.cs` | Valida Bearer token, headers de tenant/application e status ativo; retorna 403 para tenant ou application inativa (Slice 6-A `[RESOLVIDO 2026-06-17]`) |
| `HttpTenantContext` | `Auth/HttpTenantContext.cs` | Implementacao de `ITenantContext` a partir da request |

#### Webhooks

| Componente | Arquivo | Responsabilidade |
| ---------- | ------- | ---------------- |
| `HttpApplicationWebhookDispatcher` | `Webhooks/HttpApplicationWebhookDispatcher.cs` | Implementacao HTTP do dispatcher; nao registrada no Worker host (gap P1) |

### 1.4 Infraestrutura Postgres (`PaymentHub.Infrastructure.Postgres`)

- Migration inicial: `20260616232151_InitialSchema.cs` — cria todas as tabelas principais.
- FK explicita: apenas `payment_attempts.payment_id -> payments.id`. Outras referencias sao logicas (gap P2).
- `PaymentHubDbContext` com DbSets para todas as entidades.
- Repositorios implementados para todas as entidades.
- `OutboxPublisher` — publica em `OutboxEvents`.
- `CryptoServices` — criptografia AES via `IDataProtectionProvider`.

### 1.5 Providers (`PaymentHub.Infrastructure.Providers`)

| Provider | Arquivo | Estado |
| -------- | ------- | ------ |
| `FakePaymentProviderAdapter` | `Fake/FakePaymentProviderAdapter.cs` | Funcional; usado em testes |
| `AbacatePayProviderAdapter` | `AbacatePay/AbacatePayProviderAdapter.cs` | Skeleton; sem validacao de assinatura (gap P2) |
| `StripeProviderAdapter` | `Stripe/StripeProviderAdapter.cs` | Skeleton |
| `MercadoPagoProviderAdapter` | `MercadoPago/MercadoPagoProviderAdapter.cs` | Skeleton |
| `PaymentProviderRouter` | `Routing/PaymentProviderRouter.cs` | Roteia por ProviderAccount ativa |

### 1.6 Worker (`PaymentHub.Worker`)

| Worker | Arquivo | Estado |
| ------ | ------- | ------ |
| `WebhookProcessorWorker` | `WebhookProcessorWorker.cs` | Processa WebhookEvents pendentes via `ProcessWebhookEventHandler` |
| `OutboxDispatcherWorker` | `OutboxDispatcherWorker.cs` | Processa OutboxEvents; usa `NoopApplicationWebhookDispatcher` (gap P1) |

---

## 2. O que existe em spec

### Specs cobrindo codigo existente

| Spec | Fase | Cobertura |
| ---- | ---- | --------- |
| `000-mvp-scope.md` | Phase 0 | Escopo completo do MVP |
| `001-glossary-and-boundaries.md` | Phase 0 | Glossario e limites de responsabilidade |
| `002-multitenancy-and-authentication.md` | Phase 1 | Multitenancy e autenticacao por API Key |
| `003-domain-model.md` | Phase 1 | Entidades, invariantes e modelo |
| `004-payment-lifecycle.md` | Phase 1 | Status canonico e transicoes |
| `005-checkout-creation.md` | Phase 1 | Fluxo de criacao de checkout |
| `006-provider-webhooks.md` | Phase 3 | Processamento de webhooks externos |
| `007-inbox-outbox-workers.md` | Phase 7 | Workers e contrato de webhook interno |
| `008-provider-adapters.md` | Phase 2 | Interface de adapter |
| `009-api-contracts.md` | Phase 1 | Contratos HTTP |
| `010-database-contract.md` | Phase 1 | Schema de banco |
| `011-security-and-compliance.md` | Phase 6 | Seguranca, HMAC e auditoria |
| `013-testing-strategy.md` | Phase 1 | Estrategia de testes |

### Specs sem cobertura de codigo (futuras)

| Spec | Fase | Estado |
| ---- | ---- | ------ |
| `012-observability-and-audit.md` | Phase 9 | Spec existe; codigo nao iniciado |
| `014-job-search-integration.md` | Phase 1 | Spec existe; integracao nao validada |
| `015-financial-reconciliation.md` | Phase 8 | Pendente de criacao |
| `016-multi-provider-routing.md` | Phase 4 | Pendente de criacao |
| `017-admin-panel.md` | Phase 5 | Pendente de criacao |
| `018-external-broker.md` | Phase 10 | Pendente de criacao |

---

## 3. O que existe em testes

### Testes unitarios (`PaymentHub.UnitTests`) — 64 testes, todos passando

| Suite | Arquivo | O que cobre |
| ----- | ------- | ----------- |
| `ApiKeyAuthenticationMiddlewareTests` | `Api/` | Validacao de Bearer token, headers, caminho anonimo |
| `CreateCheckoutHandlerTests` | `Application/` | Criacao de checkout, idempotencia, provider Fake |
| `ProcessWebhookEventHandlerTests` | `Application/` | Processamento de webhook, deduplicacao, status intermediarios |
| `OutboxEventTests` | `Domain/` | Invariantes de OutboxEvent |
| `PaymentStatusMapperTests` | `Domain/` | Mapeamento de status de provider para canonico |
| `PaymentTests` | `Domain/` | Invariantes de Payment |
| `RetryPolicyTests` | `Domain/` | Politica de retry |
| `WebhookEventTests` | `Domain/` | Invariantes de WebhookEvent |
| `HmacWebhookSignerTests` | `Infrastructure/` | Assinatura HMAC de webhooks internos |
| `FakePaymentProviderAdapterTests` | `Infrastructure/Providers/` | Comportamento do adapter Fake |

### Testes de integracao (`PaymentHub.IntegrationTests`)

- Projeto existe e compila.
- **Zero testes descobertos** — gap P2-2.
- Nenhuma fixture com banco real, middleware ou workers.

---

## 4. Gaps identificados

### Gaps P1 (criticos — resolver antes de producao)

| ID | Descricao | Evidencia no codigo | Slice sugerido |
| -- | --------- | ------------------- | -------------- |
| ~~P1-1~~ | Tenant/application inativos nao bloqueiam fluxos autenticados | `ApiKeyAuthenticationMiddleware` agora consulta `Tenant.Status` e `ApplicationClient.Status`; 403 para entidades inativas | Slice 6-A `[RESOLVIDO 2026-06-17]` |
| P1-2 | `RegisterProviderAccountHandler` usa tenant/application do body, nao do contexto | `ProviderAccountsController` agora deriva `tenantId`/`applicationId` de `ITenantContext`; body nao aceita mais esses campos | Slice 6-B `[RESOLVIDO 2026-06-18]` |
| P1-3 | Endpoints de bootstrap/admin sem politica explicita de autenticacao | `TenantsController` e `ApplicationsController` sem mecanismo claro para primeiro uso | Slice 6-D + ADR-0006 `[RESOLVIDO 2026-06-18 — IBootstrapPolicy + BootstrapOptions + DevelopmentDataSeeder]` |
| P1-4 | Worker de Outbox usa `NoopApplicationWebhookDispatcher` | `PaymentHub.Worker/Program.cs` registra `Noop`; `HttpApplicationWebhookDispatcher` nao esta no host worker | Slice 7-A |
| P1-5 | `ApplicationClient.WebhookSecret` persistido em texto claro | Coluna `application_clients.webhook_secret` agora armazena blob AES-CBC cifrado via `IWebhookSecretProtector` (chave em `PaymentHub:WebhookSecretEncryptionKey`); DTO de resposta expoe apenas `hasWebhookSecret: bool` | Slice 6-C + ADR-0007 `[RESOLVIDO 2026-06-25]` |

### Gaps P2 (importantes — resolver antes de expansao)

| ID | Descricao | Evidencia no codigo | Acao sugerida |
| -- | --------- | ------------------- | ------------- |
| P2-1 | Assinatura de webhooks externos nao validada nos adapters reais | `AbacatePayProviderAdapter`, `StripeProviderAdapter`, `MercadoPagoProviderAdapter` sao skeleton | Slice 2-A (AbacatePay), depois Stripe/MP |
| P2-2 | Projeto de integracao sem testes | `PaymentHub.IntegrationTests` compila, sem testes | Slice 1-IT |
| P2-3 | `AuditLog` nao gravado em handlers administrativos | Entidade e repositorio existem; nenhum handler chama `AddAsync(new AuditLog(...))` | Slice 6-D |
| P2-4 | FKs relacionais parciais no banco | Migration inicial cria apenas FK de `payment_attempts`; outros referencias sao logicas | ADR-0009, depois migration |
| P2-5 | `overview.md` usa formato antigo de assinatura HMAC | Doc descreve `HMAC-SHA256(payload, secret)`; implementacao usa `timestamp.payload` com header separado | Slice documental |

### Gaps P3 (evolucao futura)

- Sem relatorios financeiros, metricas, painel admin, reconciliacao ou broker externo.
- Sem testes de contrato para adapters (suite compartilhada por providers).
- Sem validacao de `is_default` unico por provider account em banco.

---

## 5. Premissas nao validadas

As seguintes afirmacoes sao assumidas verdadeiras pelo design mas nao possuem teste automatizado ou evidencia direta:

| Premissa | Risco se falsa | Como validar |
| -------- | -------------- | ------------ |
| A migration inicial cria todos os indices necessarios para performance em producao | Degradacao de performance sob carga | Revisar `InitialSchema.cs` e comparar com `010-database-contract.md`; adicionar `EXPLAIN` em queries criticas |
| O `PaymentProviderRouter` seleciona corretamente a ProviderAccount ativa para o tenant/application | Checkout criado com provider errado ou indisponivel | Teste de integracao com multiplas ProviderAccounts |
| A idempotencia de checkout evita criacao duplicada em race condition | Dupla cobranca em ambiente com alta concorrencia | Teste de stress com requests concorrentes e mesma `Idempotency-Key` |
| O `WebhookProcessorWorker` nao processa o mesmo evento em paralelo | Dupla atualizacao de status | Teste de concorrencia; adicionar `FOR UPDATE SKIP LOCKED` ou lock distribuido |
| O mapeamento de status de provider para canonico cobre todos os payloads de producao | Pagamentos presos em `Pending` ou com status incorreto | Rodar adapters reais em sandbox e verificar todos os cenarios de status |
| Credenciais de provider descriptografam corretamente em runtime com a mesma chave usada na criptografia | Indisponibilidade total de checkout | Teste de integracao de roundtrip criptografico |
| O `HttpApplicationWebhookDispatcher` entrega corretamente com retry e timeout | Perda silenciosa de webhooks internos | Teste de integracao com servidor mock e falhas simuladas |

---

## 6. Estado das ADRs

Cinco ADRs aceitas cobrem as decisoes fundamentais do MVP. Quatro ADRs propostas aguardam decisao antes dos slices correspondentes.

Ver `docs/adr/000-adr-index.md` para lista completa.

---

## Proximos passos imediatos

Com base no estado atual, a ordem recomendada e:

1. **ADR-0006** — Formalizar politica de bootstrap/admin.
2. **ADR-0007** — Decidir protecao de `WebhookSecret`.
3. ~~**Slice 6-A** — Enforcement de status ativo no middleware.~~ `[CONCLUIDO 2026-06-17]`
4. **Slice 7-A** — Substituir `NoopApplicationWebhookDispatcher` por HTTP real no Worker host.
5. **Slice 6-B** — Corrigir `RegisterProviderAccountHandler` para usar `ITenantContext`.
6. **Slice 6-C** — Proteger `WebhookSecret` conforme ADR-0007.
7. **Slice 6-D** — Gravar `AuditLog` em handlers administrativos + politica de bootstrap.
8. **Slice 1-IT** — Primeira fixture de integracao com Postgres.

---

## Arquivos relacionados

- `docs/audits/spec-adherence-audit-2026-06-17.md` — auditoria de aderencia a specs (complementar)
- `docs/audits/roadmap-adherence-matrix-2026-06-17.md` — matriz de aderencia roadmap/spec/codigo/testes
- `docs/audits/specs-bootstrap-report-2026-06-17.md` — relatorio do bootstrap de specs
- `docs/adr/000-adr-index.md` — indice de ADRs
- `docs/specs/000-spec-index.md` — indice de specs
- `docs/roadmap/002-phase-status-board.md` — painel de status das fases
