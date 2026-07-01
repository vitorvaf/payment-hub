# Inbox, Outbox e Workers

## Objetivo

Definir processamento assincrono, retries, concorrencia e idempotencia para Inbox e Outbox, alem das responsabilidades do Worker host e do dispatcher HTTP real do Outbox.

## Escopo

- `WebhookProcessorWorker`.
- `OutboxDispatcherWorker`.
- `IApplicationWebhookDispatcher` + `HttpApplicationWebhookDispatcher`.
- `IOutboxEventStore` + `EfOutboxEventStore`.
- `IOutboxRepository`.
- Estados de `WebhookEvent` e `OutboxEvent`.
- Retry policy, logs e falha permanente.
- Categorias de erro do dispatcher (`WebhookDispatcherCategory`).
- HMAC de webhook interno (referencia: spec 011).
- Validacao HTTPS/SSRF do `WebhookUrl` (referencia: spec 011).
- Fail-fast de `IWebhookSecretProtector` no startup do Worker.

## Fora de escopo

- Broker externo no MVP.
- Testes de integracao com Postgres real (Slice 1-IT).

> **Slice 7-M1 (2026-06-30) — RESOLVIDO:** Sweep automatico de eventos `Processing` orfaos e concorrencia multi-instancia via `FOR UPDATE SKIP LOCKED` foram implementados nesta slice. Ver secao "Multi-instancia (Slice 7-M1)" abaixo.

## Regras obrigatorias

- Workers devem ser idempotentes e tolerantes a reprocessamento.
- Selecionar apenas eventos `Pending` cujo `next_retry_at` esteja vazio ou vencido.
- Marcar `Processing` ou usar mecanismo equivalente antes de executar trabalho critico.
- **Slice 7-M1:** o `SELECT` + `UPDATE` que move o evento para `Processing` deve ser atomico (uma unica transacao) e usar `FOR UPDATE SKIP LOCKED` para garantir que dois workers simultaneos nunca peguem a mesma linha.
- **Slice 7-M1:** o worker DEVE rodar o sweep de `Processing` orfao antes do claim para recuperar rows deixadas por um worker que caiu ou foi reiniciado.
- Atualizar `retry_count`, `last_error` e `next_retry_at` em falhas.
- Apos limite de tentativas, marcar `Failed` e exigir intervencao manual.

## Contratos

### Retry policy

```text
1a tentativa: imediato
2a tentativa: +1 minuto
3a tentativa: +5 minutos
4a tentativa: +15 minutos
5a tentativa: +1 hora
depois: Failed
```

### Estados

| Entidade | Estados |
|----------|---------|
| `WebhookEvent` | `Pending`, `Processing`, `Processed`, `Failed` |
| `OutboxEvent` | `Pending`, `Processing`, `Sent`, `Failed` |

### `OutboxEvent.LastError` (politica segura)

`OutboxEvent.LastError` armazena apenas:

- `WebhookDispatcherCategory` (enum): categoria segura da falha.
- `int?` (HTTP status code, quando aplicavel).

**Nao** armazena:

- `ex.Message` (pode conter body HTTP, query strings, stack traces).
- `ex.StackTrace` (caminhos internos).
- URL com credenciais em query string.
- Segredo raw ou protegido do consumidor.

Categorias aceitas (`PaymentHub.Domain/Enums/WebhookDispatcherCategory.cs`):

| Categoria | Quando | StatusCode obrigatorio |
|-----------|--------|------------------------|
| `HttpFailure` (1) | Consumer retornou nao-2xx | sim |
| `NetworkError` (2) | Falha de DNS, conexao, TLS | nao |
| `Timeout` (3) | `HttpClient` excedeu timeout | nao |
| `UnprotectFailure` (4) | `IWebhookSecretProtector.Unprotect` falhou | nao |
| `MissingWebhookUrl` (5) | Application sem `WebhookUrl` | nao |
| `MissingWebhookSecret` (6) | Reservado (nao deve ocorrer) | nao |
| `UnexpectedDispatcherError` (7) | Excecao nao esperada | nao |
| `ProcessingOrphaned` (8) | **Slice 7-M1:** row estava em `Processing` alem do TTL; sweep moveu para `Pending` | nao |

`OutboxEvent.MarkRetryWithStatus(WebhookDispatcherCategory, int statusCode, DateTime nextRetryAt)` e `OutboxEvent.MarkFailedWithStatus(WebhookDispatcherCategory, int statusCode)` sao os metodos publicos para atualizar `LastError`.

### Payload minimo de webhook interno

```json
{
  "eventId": "guid",
  "eventType": "payment.approved",
  "paymentId": "guid",
  "externalReference": "job-search-order-123",
  "amount": 2990,
  "currency": "BRL",
  "provider": "Fake",
  "status": "Approved",
  "providerPaymentId": "fake_123",
  "occurredAt": "2026-06-16T12:00:00Z"
}
```

- `eventId` e obrigatorio e deve ser o id estavel do `OutboxEvent`.
- Reprocessar o mesmo `OutboxEvent` mantem o mesmo `eventId`.
- `eventType` e obrigatorio e deve refletir o tipo do evento interno.
- Consumidores devem usar `eventId` como chave preferencial de idempotencia; `paymentId + status` e apenas fallback.
- `occurredAt` representa o momento do evento no Payment Hub.

### Headers de webhook interno

```http
Content-Type: application/json
X-PaymentHub-Event-Id: <eventId>
X-PaymentHub-Event-Type: payment.approved
X-PaymentHub-Timestamp: <unix_time_seconds>
X-PaymentHub-Signature: <hex_lowercase_hmac_sha256>
```

> **Gap documentado (B4-security):** os headers `X-PaymentHub-Event-Type` / `X-PaymentHub-Tenant` / `X-PaymentHub-Application` nao estao sendo validados/autorizados pelo consumidor. O dispatcher envia; o consumidor confia no HMAC. Risco de spoofing e baixo (HMAC e obrigatorio), mas a presenca dos headers e informacional. **Fora do escopo do Slice 7-A.**

### Contrato HMAC

```text
rawBody = corpo HTTP exatamente como enviado
timestamp = valor do header X-PaymentHub-Timestamp
signedPayload = timestamp + "." + rawBody
signature = HMACSHA256(webhookSecret, UTF8(signedPayload))
signatureFormat = hexadecimal lowercase
```

O consumidor deve rejeitar timestamps fora da tolerancia recomendada de 5 minutos e comparar assinaturas em tempo constante quando possivel.

### Dispatcher HTTP real (Slice 7-A)

`HttpApplicationWebhookDispatcher` em `src/PaymentHub.Infrastructure.Postgres/Webhooks/HttpApplicationWebhookDispatcher.cs`:

- Recebe `IApplicationClientRepository`, `IOutboxEventStore`, `IWebhookSecretProtector`, `IWebhookSigner`, `ILogger<HttpApplicationWebhookDispatcher>` e `IOptions<PaymentHubOptions>`.
- Seleciona o `OutboxEvent`, busca o `ApplicationClient` via `_apps.GetByTenantAndIdAsync(outboxEvent.TenantId, outboxEvent.ApplicationId, ct)` (tenant guard).
- Em miss (application nao encontrada para o tenant), loga warning com `tenantId`/`applicationId`/`outboxEventId` e **retorna sem lancar**. O Worker marca o evento como retry sem expor dados sensiveis.
- Em `MissingWebhookUrl`, registra `LastError = MissingWebhookUrl` e marca como `Failed` (sem retry — endereco nao vai aparecer magicamente).
- Em `UnprotectFailure`, registra `LastError = UnprotectFailure` e marca como retry. A chave deve ser corrigida por canal externo.
- Em HTTP 2xx: marca como `Sent` e limpa `LastError`.
- Em HTTP nao-2xx: `MarkRetryWithStatus(HttpFailure, statusCode, nextRetryAt)`.
- Em excecao de rede / timeout / inesperada: categoria apropriada + retry.

O `HttpClient` e obtido via `IHttpClientFactory.CreateClient("application-webhook")` (registrado em `AddPaymentHubPostgres`). Timeout configurado por `PaymentHub:WebhookHttpTimeoutSeconds` (default 10s).

### Validacao de `WebhookUrl` (Slice 7-A.5)

`ApplicationClient.WebhookUrl` deve passar por `RegisterApplicationClientValidator` antes de qualquer persistencia. O validator usa `internal static class WebhookUrlValidator` (helper puro) com regras HTTPS/SSRF descritas em spec 011. Mensagem de erro unificada: `"WebhookUrl must be an absolute HTTPS URL pointing to a public endpoint."`.

### Worker testavel (Slice 7-A.4)

`OutboxDispatcherWorker` recebe `IOutboxRepository`, `IOutboxEventStore` e `IClock` no construtor. Nao acessa `PaymentHubDbContext` diretamente. `DateTime.UtcNow` foi removido; agora usa `_clock.UtcNow`.

### Fail-fast de `IWebhookSecretProtector` (Slice 7-A.3 + 7-A.6)

`src/PaymentHub.Worker/Program.cs` resolve `IWebhookSecretProtector` em um scope anonimo antes de `host.Run()`. Em producao sem `PaymentHub:WebhookSecretEncryptionKey`, o startup falha com `InvalidOperationException`.

`appsettings.json` (production) tem placeholder vazio; `appsettings.Development.json` tem valor fake `dev-webhook-secret-key-change-me-32bytes` (39 chars, >= 32). Producao recebe valor real por `PaymentHub__WebhookSecretEncryptionKey` ou secret manager.

### `NoopApplicationWebhookDispatcher`

Removido do codigo de producao e dos registros de DI. Qualquer teste que precise simular o dispatcher usa `Mock<IApplicationWebhookDispatcher>`.

## Multi-instancia (Slice 7-M1)

> Implementado em 2026-06-30. Resolve os gaps P1-multi-instance (`FOR UPDATE SKIP LOCKED`) e M1-security (sweep automatico de `Processing` orfao) que estavam documentados em `docs/adr/ADR-0010-real-outbox-dispatcher-location.md` e nos relatorios de auditoria do Slice 7-A.

### Claim transacional com `FOR UPDATE SKIP LOCKED`

O Worker agora chama `IOutboxRepository.ClaimPendingForDispatchAsync(batchSize, now, ct)` em vez do antigo `GetPendingForDispatchAsync`. A implementacao (`OutboxRepository` em `src/PaymentHub.Infrastructure.Postgres/Repositories/Repositories.cs`) abre uma transacao `ReadCommitted` no `NpgsqlConnection` raw (EF Core 10 nao traduz `SKIP LOCKED` em LINQ), executa:

```sql
SELECT id
FROM outbox_events
WHERE status = 'Pending'
  AND (next_retry_at IS NULL OR next_retry_at <= @now)
ORDER BY created_at
FOR UPDATE SKIP LOCKED
LIMIT @batchSize;

UPDATE outbox_events
SET status = 'Processing',
    processing_started_at = @now,
    updated_at = @now
WHERE id = ANY(@claimedIds);
```

e devolve as entidades recarregadas via EF Core (com `AsNoTracking`) em ordem de `created_at`. As duas operacoes rodam **na mesma transacao**; o commit so ocorre depois do UPDATE. Concorrencia multi-instancia:

- Worker A pega `SELECT FOR UPDATE` em N rows; Worker B ve as locks via `SKIP LOCKED` e pula para as proximas disponiveis.
- Worker B nunca recebe uma row que Worker A ja claimou.
- Crash do Worker A entre SELECT e UPDATE = a transacao faz rollback automaticamente; nenhuma row fica em `Processing` para sempre.

### `OutboxEvent.ProcessingStartedAt`

Nova coluna non-sensitive (`timestamp with time zone`, nullable) que registra o instante exato em que o claim moveu a row para `Processing`. Limpa em **toda** saida de `Processing` (`MarkSent`, `MarkRetryWithCategory`, `MarkRetryWithStatus`, `MarkFailedWithCategory`, `MarkFailedWithStatus`, `RequeueOrphaned`). Worker faz sanity-check: se o claim devolve uma row em estado invalido (Pending ou sem `ProcessingStartedAt`), o Worker loga `Error` e pula o dispatch (anti-regressao contra remocao futura acidental do UPDATE).

### Sweep de `Processing` orfao

`IOutboxRepository.SweepOrphanedProcessingAsync(cutoff, ct)` faz um unico `UPDATE` atomico que move rows `Processing` com `processing_started_at < cutoff` de volta para `Pending`. Implementacao via `ExecuteSqlRawAsync` (uma unica round-trip, sem EF tracker):

```sql
UPDATE outbox_events
SET status = 'Pending',
    retry_count = retry_count + 1,
    last_error = 'ProcessingOrphaned',
    next_retry_at = NULL,           -- imediato, mesma iteracao do claim
    processing_started_at = NULL,
    updated_at = @now
WHERE status = 'Processing'
  AND processing_started_at IS NOT NULL
  AND processing_started_at < @cutoff;
```

`cutoff = now - OutboxProcessingTimeoutSeconds` (default 900s = 15 minutos). O Worker chama o sweep **antes** do claim em toda iteracao de `DispatchOnceAsync`; o log `Information` reporta quantas rows foram recuperadas. Defaults e tuning:

- `PaymentHubOptions.OutboxProcessingTimeoutSeconds = 900` (production). Cobrir o `WebhookHttpTimeoutSeconds = 10s` mais retries, sem SLA apertado.
- Tune **para baixo** em ambientes com SLO de entrega apertado (ex.: 60s).
- Tune **para cima** se dispatches legitimos demoram mais de 15min.
- `last_error` recebe **apenas** o literal `"ProcessingOrphaned"` (enum value). Nunca o motivo original do crash, a URL, o segredo, o body, a stack trace. O sweep NAO reabre rows terminais (`Sent`, `Failed`).

### Indice composto

Migration `20260630184619_AddOutboxProcessingStartedAtAndIndexes` substitui `(status, next_retry_at)` por `(status, next_retry_at, created_at)` para servir o `ORDER BY created_at` do claim sem sort step. Adiciona tambem um indice parcial `(status, processing_started_at) WHERE status = 'Processing'` que cobre o sweep e permanece pequeno em steady state (so' rows em `Processing` entram no indice).

### Tenant guard (inalterado)

A regra de tenant guard ja' documentada no `HttpApplicationWebhookDispatcher` (linha 137 desta spec) NAO muda: a row ja' vem com `tenant_id` + `application_id`, e o dispatcher chama `_apps.GetByTenantAndIdAsync(tenantId, applicationId, ct)`. O claim multi-instancia nao introduz nenhum caminho cross-tenant.

### Garantias operacionais

- **Sem double-dispatch:** testado por `OutboxDispatcherConcurrencyTests.ShouldNotDoubleDispatch_WhenTwoInstancesRunConcurrently` (2 workers concorrentes, 1 evento, `CallCount = 1`).
- **Distribuicao sem perdas:** testado por `OutboxDispatcherConcurrencyTests.ShouldDistributePendingEventsAcrossConcurrentInstances` (10 eventos, 3 workers, `CallCount = 10`, todos `Sent`).
- **Sweep recupera crash:** testado por `OutboxProcessingSweepTests.OutboxSweep_ShouldRequeueOrphanedProcessingEvents` (Processing de 2h atras, `OutboxProcessingTimeoutSeconds = 60`, sweep + claim na mesma iteracao entregam o webhook).
- **Sweep NAO perturba workers ativos:** testado por `OutboxProcessingSweepTests.OutboxSweep_ShouldNotRequeueRecentProcessingEvents` (Processing de 1s atras, `OutboxProcessingTimeoutSeconds = 60`, row intacta).
- **Sweep NAO reabre terminais:** testado por `OutboxProcessingSweepTests.OutboxSweep_ShouldNotReopenTerminalEvents`.
- **Claim respeita `NextRetryAt`:** testado por `OutboxProcessingSweepTests.OutboxDispatcher_ShouldRespectNextAttemptAt`.
- **Claim respeita `OutboxWorkerBatchSize`:** testado por `OutboxProcessingSweepTests.OutboxDispatcher_ShouldRespectBatchSize_WhenClaimingPendingEvents` (10 eventos, batch=3, 4 iteracoes para entregar tudo).

### Migracao aplicada

`src/PaymentHub.Infrastructure.Postgres/Migrations/20260630184619_AddOutboxProcessingStartedAtAndIndexes.cs`:

- `AddColumn processing_started_at timestamptz NULL`
- `DropIndex IX_outbox_events_status_next_retry_at`
- `CreateIndex IX_outbox_events_status_next_retry_at_created_at`
- `CreateIndex IX_outbox_events_status_processing_started_at` (partial, `WHERE status = 'Processing'`)

`OutboxEvent.payload` permanece `jsonb` (decisao do Slice 7-IT, NAO regressao). `webhook_events.raw_payload` permanece `text` (decisao do Slice 3-IT, NAO regressao). `provider_accounts.webhook_events` permanece `text` (decisao do Slice 2-C, NAO regressao).

## Criterios de aceite

- Evento processado com sucesso e marcado como finalizado.
- Falhas temporarias sao reagendadas.
- Falha permanente preserva erro (apenas categoria + statusCode) e permite acao manual futura.
- Webhook interno e assinado com HMAC sobre `{timestamp}.{rawBody}`.
- Reprocessamento do mesmo `OutboxEvent` preserva `eventId`; a assinatura pode variar se o timestamp variar.
- Worker nao depende de API (validado por `scripts/agent-architecture-check.sh`).
- `OutboxEvent.LastError` nunca contem body HTTP, query strings ou segredos.
- `WebhookUrl` rejeitada em validator antes de qualquer dispatch.

## Testes esperados

- Selecao de pendentes.
- Retry count e next retry.
- Falha apos 5 tentativas.
- Dispatch HTTP 2xx versus nao-2xx.
- Reprocessamento idempotente.
- Tenant guard: `_apps.GetByTenantAndIdAsync` chamado com `(tenantId, applicationId)`.
- `LastError` seguro: `WebhookDispatcherCategory` correto + status code; body HTTP nao persistido.
- `UnprotectFailure` nao envia HTTP request.
- `MissingWebhookUrl` marca como `Failed` sem retry.
- `WebhookUrl` rejeitada em validator (HTTPS/SSRF) — 80+ testes em `WebhookUrlValidatorTests` + `RegisterApplicationClientValidatorTests`.
- **Slice 7-M1:** claim com `FOR UPDATE SKIP LOCKED` nao entrega o mesmo row a dois workers (`OutboxDispatcherConcurrencyTests.ShouldNotDoubleDispatch_WhenTwoInstancesRunConcurrently`).
- **Slice 7-M1:** distribuicao de N eventos entre M workers preserva `CallCount == N` sem perdas nem duplicacoes (`OutboxDispatcherConcurrencyTests.ShouldDistributePendingEventsAcrossConcurrentInstances`).
- **Slice 7-M1:** sweep recupera `Processing` orfao dentro do TTL configurado (`OutboxProcessingSweepTests.OutboxSweep_ShouldRequeueOrphanedProcessingEvents`).
- **Slice 7-M1:** sweep nao perturba `Processing` recente de outro worker (`OutboxProcessingSweepTests.OutboxSweep_ShouldNotRequeueRecentProcessingEvents`).
- **Slice 7-M1:** sweep nao reabre `Sent`/`Failed` (`OutboxProcessingSweepTests.OutboxSweep_ShouldNotReopenTerminalEvents`).
- **Slice 7-M1:** claim respeita `NextRetryAt` futuro (`OutboxProcessingSweepTests.OutboxDispatcher_ShouldRespectNextAttemptAt`).
- **Slice 7-M1:** claim respeita `OutboxWorkerBatchSize` (`OutboxProcessingSweepTests.OutboxDispatcher_ShouldRespectBatchSize_WhenClaimingPendingEvents`).

## End-to-end integration tests (Slice 7-IT)

Apos a Slice 7-IT, o dispatcher e o `OutboxEvent` sao cobertos por uma suite E2E
real (`tests/PaymentHub.IntegrationTests/EndToEnd/OutboxDispatcherE2ETests.cs`)
que sobe `PaymentHub.Api` via `WebApplicationFactory<Program>`, aponta para
Postgres real (Testcontainers) e invoca `OutboxDispatcherWorker.DispatchOnceAsync`
manualmente — o worker hospedado (`BackgroundService`) NAO e' hospedado dentro
do `WebApplicationFactory` por questao de testabilidade (decisao herdada da
Slice 3-IT). Cada teste:

1. Cria uma `PaymentHubApiFactory` fresca, faz `ResetDatabaseAsync()` e seed via
   `E2ESeedHelpers` (tenant, application com `WebhookUrl` HTTPS publico + blob
   protegido por `IWebhookSecretProtector`).
2. Persiste o `OutboxEvent` via `IOutboxPublisher.EnqueueAsync` (mesma rota do
   codigo de producao, nao insere direto via `DbContext`).
3. Constroi `OutboxDispatcherWorker` via `factory.Services` e chama
   `DispatchOnceAsync(CancellationToken.None)` uma unica vez.
4. Recarrega o `OutboxEvent` do banco real e asserta `Status`, `RetryCount`,
   `LastError`, `SentAt` e `NextRetryAt`.
5. Asserta o `ApplicationWebhookCaptureHandler.Captured` (method, URL, body,
   headers `X-PaymentHub-*`).

Cobertura atual (P1 + P2):

| Cenario | Path | Ultima transicao esperada |
|---------|------|---------------------------|
| Happy path | `payment.checkout.created` | `Sent`, `LastError = null` |
| HMAC do webhook interno | qualquer evento com secret | `X-PaymentHub-Signature = sha256_hex_lowercase(secret, "{ts}.{body}")` |
| HTTP 500 do consumer | qualquer evento | `Pending`, `RetryCount = 1`, `LastError = "HttpFailure: status=500"` |
| HTTP 429 do consumer | qualquer evento | `Pending`, `RetryCount = 1`, `LastError = "HttpFailure: status=429"` |
| `UnprotectFailure` | secret corrompido | `Pending`, `RetryCount = 1`, `LastError = "UnprotectFailure"`, ZERO HTTP POSTs |
| Fluxo AbacatePay E2E | checkout + webhook + dispatch | ambos outbox `Sent`, HMAC interno valido |
| Evento ja `Sent` nao e' redespachado | iteracao 1 + iteracao 2 | `CallCount = 1` na segunda iteracao |

A Slice 7-IT introduziu um helper puro compartilhado
`InternalWebhookHmac.Compute/Matches` em
`tests/PaymentHub.IntegrationTests/Support/ApplicationWebhookCaptureHandler.cs`
para recomputar a assinatura esperada sem copiar a logica de
`HmacWebhookSigner` em cada teste.

Apos a Slice 7-M1, a suite E2E inclui tambem `OutboxDispatcherConcurrencyTests`
(2 testes: 1 evento + 2 workers concorrentes; 10 eventos + 3 workers concorrentes
com 5 iteracoes por worker) e `OutboxProcessingSweepTests` (5 testes: requeue
de orfao, preservacao de Processing recente, nao-reabertura de terminais,
respect a `NextRetryAt`, respect a `OutboxWorkerBatchSize`). Estas suites
operam contra o mesmo `PaymentHubApiFactory` + Testcontainer Postgres
introduzido na Slice 3-IT e seguem o mesmo padrao de invocacao manual
de `OutboxDispatcherWorker.DispatchOnceAsync`.

## Gaps conhecidos (deferidos)

- Headers adicionais B4-security (`X-PaymentHub-Tenant`/`X-PaymentHub-Application`).
- API `appsettings.json` placeholder para `PaymentHub` (paridade com Worker).
- `OutboxDispatcherWorker` rodando dentro do `WebApplicationFactory` (decisao
  explicita da Slice 3-IT — testes continuam invocando `DispatchOnceAsync`
  diretamente; mudar isso exigira um harness de hosting dentro do teste).

## Arquivos relacionados

- `src/PaymentHub.Worker/WebhookProcessorWorker.cs`
- `src/PaymentHub.Worker/OutboxDispatcherWorker.cs`
- `src/PaymentHub.Infrastructure.Postgres/Webhooks/HttpApplicationWebhookDispatcher.cs`
- `src/PaymentHub.Application/Abstractions/Outbox/IApplicationWebhookDispatcher.cs`
- `src/PaymentHub.Application/Abstractions/Outbox/IOutboxEventStore.cs`
- `src/PaymentHub.Application/Abstractions/Outbox/WebhookDispatcherException.cs`
- `src/PaymentHub.Infrastructure.Postgres/Outbox/EfOutboxEventStore.cs`
- `src/PaymentHub.Domain/Enums/WebhookDispatcherCategory.cs`
- `src/PaymentHub.Domain/Entities/OutboxEvent.cs`
- `src/PaymentHub.Domain/Entities/WebhookEvent.cs`
- `src/PaymentHub.Domain/Services/RetryPolicy.cs`
- `src/PaymentHub.Application/Tenants/Validation/WebhookUrlValidator.cs`
- `src/PaymentHub.Application/Tenants/RegisterApplicationClientHandler.cs` (RegisterApplicationClientValidator)
- `src/PaymentHub.Worker/Program.cs` (fail-fast)
- `src/PaymentHub.Worker/appsettings.json` (placeholder)
- `src/PaymentHub.Worker/appsettings.Development.json` (valor dev)
- `tests/PaymentHub.IntegrationTests/EndToEnd/OutboxDispatcherE2ETests.cs` (Slice 7-IT)
- `tests/PaymentHub.IntegrationTests/Support/ApplicationWebhookCaptureHandler.cs` (fakes Slice 3-IT + Slice 7-IT)
- `tests/PaymentHub.IntegrationTests/Support/E2ESeedHelpers.cs`
- `docs/specs/011-security-and-compliance.md`
- `docs/adr/ADR-0007-webhook-secret-protection.md`
- `docs/adr/ADR-0010-real-outbox-dispatcher-location.md`
- `docs/audits/slice-7a-real-outbox-dispatcher-report-2026-06-26.md`
- `docs/audits/slice-7-it-outbox-dispatcher-e2e-report-2026-06-30.md`
