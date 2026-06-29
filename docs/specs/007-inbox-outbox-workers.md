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
- Sweep automatico de eventos `Processing` orfaos.
- Concorrencia multi-instancia via `FOR UPDATE SKIP LOCKED`.
- Testes de integracao com Postgres real (Slice 1-IT).

## Regras obrigatorias

- Workers devem ser idempotentes e tolerantes a reprocessamento.
- Selecionar apenas eventos `Pending` cujo `next_retry_at` esteja vazio ou vencido.
- Marcar `Processing` ou usar mecanismo equivalente antes de executar trabalho critico.
- Evitar que dois workers processem o mesmo evento; em Postgres futuro, preferir lock transacional ou `FOR UPDATE SKIP LOCKED`.
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

## Gaps conhecidos (deferidos)

- Sweep automatico de eventos `Processing` orfaos (recovery apos crash do Worker).
- Concorrencia multi-instancia via `FOR UPDATE SKIP LOCKED` ou lock transacional.
- Integracao end-to-end com banco real (Slice 1-IT).
- Headers adicionais B4-security (`X-PaymentHub-Tenant`/`X-PaymentHub-Application`).
- API `appsettings.json` placeholder para `PaymentHub` (paridade com Worker).

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
- `docs/specs/011-security-and-compliance.md`
- `docs/adr/ADR-0007-webhook-secret-protection.md`
- `docs/adr/ADR-0010-real-outbox-dispatcher-location.md`
- `docs/audits/slice-7a-real-outbox-dispatcher-report-2026-06-26.md`
