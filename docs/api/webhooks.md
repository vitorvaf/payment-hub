# API: webhooks

Esta documentação cobre dois fluxos:

1. **Webhooks externos** — provedores (Abacate Pay, Stripe, etc.) notificam o Payment Hub sobre eventos de pagamento.
2. **Webhooks internos** — Payment Hub notifica a aplicação cliente sobre eventos de pagamento (criação, aprovação, rejeição etc.).

## Webhooks externos

Endpoint: `POST /api/v1/webhooks/{providerCode}`

### Headers opcionais

| Header | Descrição |
|--------|-----------|
| `X-Provider-Event-Id` | Identificador único do evento no provedor (usado para deduplicação) |
| `X-Provider-Event-Type` | Tipo do evento (`payment.approved`, `payment.rejected` etc.) |
| `X-Provider-Signature` | Assinatura HMAC do payload (validação depende do provedor) |

### Comportamento

1. O `ProviderWebhooksController` lê o body em texto puro.
2. Persiste um `WebhookEvent` com `Status = Pending` no banco. Esse é o **Inbox**.
3. Responde `202 Accepted` com `{ "webhookId": "..." }` em poucos milissegundos.
4. O `WebhookProcessorWorker` (BackgroundService) busca `WebhookEvent` com `Status = Pending` e processa:
   - Tenta `IPaymentProviderAdapter.ParseWebhookAsync` para extrair `ProviderPaymentId`, `EventType` e `ProviderStatus`.
   - Localiza o `Payment` correspondente via `Id` ou via `ProviderPaymentId`.
   - Mapeia o status do provider para o status canônico (`PaymentStatusMapper`).
   - Aplica a transição no `Payment` e registra um `PaymentAttempt`.
   - Se o status mudou, gera um `OutboxEvent` (ex.: `payment.approved`).
   - Marca o `WebhookEvent` como `Processed`.
5. Em caso de erro, incrementa `RetryCount` e calcula `NextRetryAt`:
   - `0s → 1m → 5m → 15m → 1h`
   - Após 5 tentativas, marca como `Failed`.

### Idempotência

Existe um índice único em `(provider_code, provider_event_id)` quando `provider_event_id` é informado. Webhooks duplicados pelo mesmo provedor retornam 202 com o `webhookId` original.

### Exemplo de curl (simulando Abacate Pay)

```bash
curl -X POST http://localhost:8080/api/v1/webhooks/Fake \
  -H "Content-Type: application/json" \
  -H "X-Provider-Event-Id: evt_001" \
  -H "X-Provider-Event-Type: payment.approved" \
  -d '{
    "id": "00000000-0000-0000-0000-000000000001",
    "status": "approved"
  }'
```

> O `id` é o `providerPaymentId` (no MVP pode ser o `paymentId` salvo no Fake).

## Webhooks internos

Os webhooks internos são entregues a partir do `OutboxEvent` pelo `OutboxDispatcherWorker`.

### URL

Configurada no campo `WebhookUrl` da `ApplicationClient` (definida no momento de cadastro ou atualizada depois).

### Headers

| Header | Descrição |
|--------|-----------|
| `Content-Type` | `application/json` |
| `X-PaymentHub-Event` | Tipo do evento (ex.: `payment.approved`) |
| `X-PaymentHub-Event-Id` | Id do `OutboxEvent` |
| `X-PaymentHub-Tenant` | Tenant id |
| `X-PaymentHub-Application` | Application id |
| `X-PaymentHub-Signature` | HMAC-SHA256(payload, WebhookSecret) em hexadecimal |

### Payload

O payload é o JSON serializado do evento. Para `payment.approved` por exemplo:

```json
{
  "paymentId": "3f1c8e0c-9f0d-4a6b-8a1d-31b7bdf7f6a1",
  "externalReference": "job-search-order-123",
  "amount": 2990,
  "currency": "BRL",
  "provider": "Fake",
  "status": "Approved",
  "providerPaymentId": "fake_3f1c8e0c",
  "updatedAt": "2026-06-16T12:00:00Z"
}
```

### Verificação de assinatura (lado da aplicação cliente)

```csharp
using System.Security.Cryptography;
using System.Text;

string payload = await ReadBodyAsync(httpRequest);
string secret = "shared-secret-between-payment-hub-and-app";
string expectedSignature = HMACSHA256.HashData(
    Encoding.UTF8.GetBytes(secret),
    Encoding.UTF8.GetBytes(payload));
string presentedSignature = httpRequest.Headers["X-PaymentHub-Signature"].ToString();

if (!CryptographicOperations.FixedTimeEquals(
        Encoding.UTF8.GetBytes(expectedSignature),
        Encoding.UTF8.GetBytes(presentedSignature)))
{
    return Results.Unauthorized();
}
```

### Resposta esperada

A aplicação cliente deve responder `2xx` em até `PaymentHub__WebhookHttpTimeoutSeconds` (10s por padrão). Qualquer outro status é tratado como falha e gera retry.

### Retry

O `OutboxDispatcherWorker` aplica a mesma política de retry dos webhooks externos:

| Tentativa | Espera |
|-----------|--------|
| 1ª | imediato |
| 2ª | +1 minuto |
| 3ª | +5 minutos |
| 4ª | +15 minutos |
| 5ª | +1 hora |
| depois | `Failed` (requer ação manual) |

## Auditoria

Toda ação administrativa relevante gera um `AuditLog`:

- cadastro/atualização de tenants
- cadastro/atualização de aplicações
- cadastro/atualização de provider accounts
- revogação de API keys

Os logs carregam `actor`, `action`, `entity`, `entityId`, `metadata` (JSONB) e `created_at`.
