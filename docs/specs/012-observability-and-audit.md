# Observabilidade e Auditoria

## Objetivo

Definir o contrato minimo de observabilidade (correlation id, metricas,
logs estruturados) e de auditoria (`AuditLog`) para o Payment Gateway MVP.
A spec e a fonte de verdade para os eventos emitidos, os identificadores
propagados e o conjunto de campos proibidos em logs. Slice 9-O1 introduziu
a primeira onda do contrato; slices subsequentes (9-O2+) podem estender o
catalogo sem invalidar este documento.

## Escopo

- Header HTTP `X-Correlation-Id` (entrada e saida).
- Coluna `correlation_id` em `webhook_events` e `outbox_events`.
- Instrumentos `System.Diagnostics.Metrics` no meter `PaymentHub`.
- Catalogo canonico `PaymentHubLogEvents` (event names).
- Helpers `SafeLog` para formatacao anti-vazamento.
- `AuditLog` para acoes administrativas sensiveis.

## Fora de escopo (Phase 9 / 9-O2+)

- Stack completa de APM (OpenTelemetry collector, Jaeger, Datadog).
- Distributed tracing end-to-end via `Activity`/`OpenTelemetry`.
- Dashboards pre-prontos. O slice 9-O1 expoe apenas os instrumentos; a
  composicao dos dashboards e tarefa da plataforma.
- AuditLog middleware. Slice 9-O1 nao introduz captura automatica de
  acoes; o `AuditLog` permanece como entidade disponivel para handlers
  administrativos quando evoluirem (Fase 5/Phase 5 admin panel).

## Decisoes locked (slice 9-O1)

1. **Propagacao via colunas dedicadas**: `correlation_id VARCHAR(64) NULL`
   em `webhook_events` e `outbox_events`. NAO injetar no payload JSON
   canonico (este permanece `jsonb` em `outbox_events.payload` e `text`
   em `webhook_events.raw_payload`).
2. **Header invalido**: substituido silenciosamente por novo GUID. Nunca
   retorna `400` para `X-Correlation-Id` malformado. O middleware loga
   `observability.correlation_id_generated` sem expor o valor recebido.
3. **Metricas sempre habilitadas**: instruments sao registrados na
   inicializacao do processo. Custo e negligivel; evita surpresas em
   dashboards. Config gate foi rejeitado para impedir "metricas opacas
   em prod".
4. **Payload nao e tocado**: o campo `payload` do outbox continua sendo
   o conteudo de negocio serializado. Toda observabilidade adicional
   fica fora do JSON canonico para preservar a forma byte-exact.

## Contratos

### Header HTTP `X-Correlation-Id`

- Inbound: lido em `CorrelationIdMiddleware`. Valores invalidos sao
  substituidos por novo GUID-N (32 chars hex).
- Validacao: regex `^[A-Za-z0-9\-]{8,128}$` (charset ASCII + length
  window). Implementada por `CorrelationIdGenerator.IsValid`.
- Outbound: ecoado no response de toda requisicao HTTP pelo middleware.
- Edge outbound: o `HttpApplicationWebhookDispatcher` adiciona o mesmo
  valor no header `X-Correlation-Id` do POST para o consumidor do
  webhook do application client.
- Edge inbound: `ProviderWebhooksController` propaga o valor via
  `ICorrelationIdAccessor` para o `WebhookEvent` persistido.

### Coluna `correlation_id`

- Tipo: `character varying(64)` em ambas as tabelas.
- Nullable: rows pre-9-O1 e seeds sem HTTP context persistem `NULL`.
- Sem indice: queries por correlation id sao incident-triage, nao
  steady-state. Indice sera adicionado se workloads o justificarem.
- Cap em Domain: `NormalizeCorrelationId` trunca em 64 chars antes do
  insert, garantindo que a coluna nunca recebe valor fora do limite.

### Instrumentos (`System.Diagnostics.Metrics`)

Meter name canonico: `PaymentHub` (versao `1.0.0`). Lista completa em
`src/PaymentHub.Application/Observability/PaymentHubMetrics.cs`. Tag
whitelist: `provider`, `operation`, `status`, `error_category`,
`event_type`, `environment`, `worker`. Qualquer outra chave e
rejeitada em runtime pelo helper `PaymentHubMetrics.Tag(...)`.

**Counters** (sufixo `_total`):

| Instrumento | Descricao |
|---|---|
| `paymenthub_checkouts_created_total` | Checkouts aceitos (replay idempotente excluido) |
| `paymenthub_checkouts_idempotent_replay_total` | Replays resolvidos para payment existente |
| `paymenthub_checkouts_idempotency_conflict_total` | Rejeicoes por hash mismatch |
| `paymenthub_provider_webhooks_received_total` | Webhooks inbound aceitos |
| `paymenthub_provider_webhooks_rejected_total` | Webhooks inbound rejeitados pre-persist |
| `paymenthub_webhook_events_processed_total` | WebhookEvent -> Processed |
| `paymenthub_webhook_events_failed_total` | WebhookEvent -> Failed permanente |
| `paymenthub_webhook_events_retried_total` | WebhookEvent backoff agendado |
| `paymenthub_outbox_events_sent_total` | OutboxEvent dispatch 2xx |
| `paymenthub_outbox_events_retried_total` | OutboxEvent retry agendado |
| `paymenthub_outbox_events_failed_total` | OutboxEvent -> Failed permanente |
| `paymenthub_outbox_orphans_recovered_total` | Sweep recuperou Processing orfao |
| `paymenthub_authorization_denied_total` | 401/403 do ApiKeyAuthenticationMiddleware |

**Histograms** (sufixo `_duration_ms`):

| Instrumento | Descricao |
|---|---|
| `paymenthub_checkout_duration_ms` | Latencia do CreateCheckoutHandler |
| `paymenthub_provider_webhook_duration_ms` | Latencia do ProviderWebhooksController |
| `paymenthub_outbox_dispatch_duration_ms` | Latencia outbound do dispatcher |

### Catalogo de eventos de log

`PaymentHubLogEvents` define 31 nomes canonicos. Novas emissores DEVEM
usar constantes deste catalogo; strings ad-hoc sao proibidas.

Familias:
- `checkout.*` (5 eventos)
- `provider_webhook.*` (4 eventos)
- `webhook_event.*` (5 eventos)
- `outbox_event.*` (9 eventos)
- `auth.*` (3 eventos)
- `observability.correlation_id_*` (2 eventos)

Cada evento loga apenas categorias enum e identificadores truncados.
Nunca loga valores raw de `apiKey`, `webhookSecret`, `rawPayload`,
`signature` ou `body`. Use `SafeLog` para formatacao.

### `SafeLog`

Helpers puros em `Application/Observability/SafeLog.cs`:
- `SafeLog.Id(Guid?)` -> primeiros 8 chars do GUID-N ou `"-"`.
- `SafeLog.Length(string?)` -> contagem de chars sem revelar conteudo.
- `SafeLog.Flag(label, bool?)` -> `"label=yes"`, `"label=no"`, ou `"label=-"`.
- `SafeLog.Category<TEnum>(TEnum)` -> `Enum.GetName` puro.

### Anti-vazamento

`scripts/agent-docs-check.sh` falha o build quando qualquer chamada
`Log(Warning|Information|Error|Debug|Critical|Trace)` no codigo de
producao interpola tokens sensiveis (`apiKey`, `webhookSecret`,
`rawPayload`, `signature`, `Authorization`, `body`). `NoLeakLogTests`
cobre a mesma propriedade via reflection em runtime.

`OutboxEvent.LastError` continua persistindo apenas a categoria enum
+ status code (decisao slice 7-A.7). NUNCA `ex.Message`, body, URL,
signature ou stack trace.

### `AuditLog`

Entidade persistida em `audit_logs` (jsonb `metadata`). Acoes
auditaveis (catalogadas em 9-O2+):
- Criacao/alteracao de tenant.
- Criacao/alteracao de application.
- Criacao/revogacao de API key.
- Criacao/alteracao de provider account.
- Reprocessamento manual futuro de webhook/outbox.
- Configuracoes sensiveis.

A slice 9-O1 NAO introduz a captura automatica via middleware/handler;
a tabela permanece pronta para Phase 5 (admin panel). O
`EntityConfigurations.AuditLogConfiguration` ja mapeia
`metadata jsonb`.

## Criterios de aceite

- Toda requisicao HTTP tem um `X-Correlation-Id` no response.
- O id se propaga de checkout -> outbox -> webhook outbound.
- Webhooks inbound persistem o id do controller na coluna
  `webhook_events.correlation_id`.
- Logs de erro NAO incluem `ex.Message` quando este pode conter
  URL/body/signature.
- Metrics counters incrementam 1x por evento de negocio relevante.
- Tag whitelist e enforced em runtime pelo `PaymentHubMetrics.Tag`.
- `dotnet test` cobre:
  - `~CorrelationId` (8 testes)
  - `~SafeLog` (11 testes)
  - `~PaymentHubMetrics` (10 testes)
  - `~NoLeak` (2 testes)

## Testes esperados

- `tests/PaymentHub.UnitTests/Observability/CorrelationIdGeneratorTests.cs`
- `tests/PaymentHub.UnitTests/Observability/SafeLogTests.cs`
- `tests/PaymentHub.UnitTests/Observability/PaymentHubMetricsTests.cs`
- `tests/PaymentHub.UnitTests/Observability/NoLeakLogTests.cs`
- `tests/PaymentHub.UnitTests/Api/CorrelationIdMiddlewareTests.cs`
- `tests/PaymentHub.UnitTests/Api/HttpCorrelationIdAccessorTests.cs`
- `tests/PaymentHub.IntegrationTests/EndToEnd/CorrelationIdE2ETests.cs`

## Arquivos relacionados

- `src/PaymentHub.Application/Observability/CorrelationIdGenerator.cs`
- `src/PaymentHub.Application/Observability/PaymentHubMetrics.cs`
- `src/PaymentHub.Application/Observability/PaymentHubLogEvents.cs`
- `src/PaymentHub.Application/Observability/SafeLog.cs`
- `src/PaymentHub.Application/Abstractions/Observability/ICorrelationIdAccessor.cs`
- `src/PaymentHub.Api/Auth/CorrelationIdMiddleware.cs`
- `src/PaymentHub.Api/Auth/HttpCorrelationIdAccessor.cs`
- `src/PaymentHub.Worker/NullCorrelationIdAccessor.cs`
- `src/PaymentHub.Infrastructure.Postgres/Migrations/20260701000001_AddObservabilityColumns.cs`
- `src/PaymentHub.Domain/Entities/OutboxEvent.cs` (campo `CorrelationId`)
- `src/PaymentHub.Domain/Entities/WebhookEvent.cs` (campo `CorrelationId`)
- `src/PaymentHub.Infrastructure.Postgres/Webhooks/HttpApplicationWebhookDispatcher.cs` (header outbound)
- `docs/audits/slice-9-o1-observability-minimal-report-2026-07-01.md`
