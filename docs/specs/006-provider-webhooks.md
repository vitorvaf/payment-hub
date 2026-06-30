# Webhooks Externos de Provider

## Objetivo

Formalizar recebimento, deduplicacao e processamento assíncrono de webhooks externos.

## Escopo

- Endpoint `POST /api/v1/webhooks/{providerCode}`.
- Persistencia de `WebhookEvent` como Inbox.
- Validacao de assinatura quando suportada.
- Cenarios de webhook aprovado, rejeitado, duplicado, invalido e fora de ordem.

## Fora de escopo

- Entrega de eventos internos; ver `007-inbox-outbox-workers.md`.

## Regras obrigatorias

- Controller nunca processa regra pesada.
- Controller le payload bruto.
- Controller persiste `WebhookEvent` com status `Pending`.
- Controller retorna `202 Accepted` quando evento for aceito.
- Assinatura externa deve ser validada quando o provider suportar.
- Validacao pode acontecer no adapter, mas o comportamento precisa ser testavel.
- Worker deve resolver o `IPaymentProviderAdapter` pelo `ProviderCode` e delegar parsing para `ParseWebhookAsync`.
- Handler de webhook nao deve depender de campos JSON especificos de Stripe, Abacate Pay, Mercado Pago ou Fake.
- Duplicado com mesmo `provider_code + provider_event_id` retorna 202 com referencia ao evento conhecido.
- Payload invalido pode ser recusado com 400 quando nao for JSON parseavel; se aceito, deve ser persistido com status adequado.

## Contratos

```http
POST /api/v1/webhooks/{providerCode}
X-Provider-Event-Id: <event_id opcional>
X-Provider-Event-Type: <event_type opcional>
X-Provider-Signature: <signature opcional> (legacy fallback, ainda suportado)
X-Webhook-Signature: <signature obrigatoria para AbacatePay>
Content-Type: application/json
```

Resposta aceita:

```json
{ "webhookId": "guid" }
```

Cenarios:

| Cenario | Resultado esperado |
|---------|--------------------|
| aprovado/rejeitado | Persistir pending e worker processar status canonico |
| duplicado com event id | 202 e sem novo processamento efetivo |
| sem provider_event_id | Aceitar, mas deduplicacao fica limitada |
| assinatura invalida | Recusar ou marcar falha conforme adapter do provider |
| fora de ordem | Evitar regressao de status terminal |
| pagamento inexistente | Marcar `Failed` com `last_error` claro e nao gerar `OutboxEvent` |
| AbacatePay sem `X-Webhook-Signature` (case-insensitive) | **401 Unauthorized** sem persistir `WebhookEvent` |
| AbacatePay com `X-Webhook-Signature` presente | Recebido normalmente, HMAC verificado no handler |
| `X-Webhook-Signature` e `X-Provider-Signature` ambos presentes | `X-Webhook-Signature` tem prioridade |

### Assinatura HMAC para AbacatePay (Slice 2-B — 2026-06-29)

AbacatePay envia eventos v2 com assinatura HMAC-SHA256 do body UTF-8, codificada em Base64, no header `X-Webhook-Signature`. O segredo compartilhado (`webhookSecret`) e fornecido pelo merchant na criacao do webhook no dashboard AbacatePay e persiste no `ProviderAccount.EncryptedCredentials` como o campo JSON `webhookSecret` (ou `secret` legacy).

Contratos por camadas:

- **Controller (`ProviderWebhooksController.Receive`)**:
  - Aceita `X-Webhook-Signature` como header canonico. Continua aceitando `X-Provider-Signature` para compatibilidade.
  - Quando ambos chegam, prioriza `X-Webhook-Signature`.
  - Quando `providerCode` e "AbacatePay" (case-insensitive) e o header de assinatura esta ausente ou branco, retorna **`401 Unauthorized`** com body `{ "error": "missing_signature" }` antes de qualquer gravacao em banco. Nao chama o handler.
  - Providers nao-AbacatePay continuam com comportamento legacy sem exigir assinatura.
- **`ProcessWebhookEventHandler`**:
  - Para AbacatePay, resolve o `ProviderAccount` via `IProviderAccountRepository.GetByCodeAsync(tenantId, applicationId, AbacatePay)`.
  - Roteamento scoped via metadata do payload (`data.metadata.{tenantId, applicationId, paymentId}`) — nunca varre todos os tenants. Se metadata ou pagamento nao existirem, o webhook e marcado como `Failed` com `LastError` que NAO contem secret nem raw body.
  - Desprotege `EncryptedCredentials` via `ICredentialProtector.Unprotect` e extrai `webhookSecret` (preferindo o campo explicito, caindo para o campo `secret` legacy).
  - Passa o segredo ao adapter via `ProviderWebhookRequest.WebhookSecret` (init-only). O segredo NAO e persistido no `WebhookEvent`, NAO e logado, NAO e retornado em `ErrorMessage`.
  - Para outros providers (Fake, Stripe, MercadoPago) segue o caminho legacy sem exigir HMAC.
- **`AbacatePayProviderAdapter.ParseWebhookAsync`**:
  - Recusa silencioosamente (`ProviderWebhookParseResult.IsValid = false`) quando `WebhookSecret` ou `Signature` estao ausentes.
  - Verifica HMAC-SHA256(base64) com `CryptographicOperations.FixedTimeEquals`. Em qualquer caso de falha, retorna `IsValid = false` e mensagem categorizada (ex.: `AbacatePay webhook signature invalid (SignatureMismatch).`); a mensagem NAO inclui o segredo, a assinatura raw, o body bruto ou `apiKey`.
  - Apos a verificacao, normaliza o payload via `IAbacatePayWebhookNormalizer` suportando eventos `transparent.completed | transparent.refunded | transparent.disputed | transparent.lost`.
- **Endpoints**: `/api/v1/webhooks/{providerCode}` permanece anonimo (whitelist do `ApiKeyAuthenticationMiddleware`).

Cenarios de cobertura:

| Cenario | Resultado esperado |
|---------|--------------------|
| AbacatePay sem `X-Webhook-Signature` | `401 Unauthorized` sem persistir |
| AbacatePay com `X-Webhook-Signature` valido, `transparent.completed+PAID` | `WebhookEvent` persistido e processado, status canonico `Approved` |
| AbacatePay com signature invalida | `WebhookEvent` marcado como `Failed` com `LastError` categorizado |
| AbacatePay com body JSON malformado | `WebhookEvent` marcado como `Failed` com `LastError` seguro |
| AbacatePay com evento nao suportado (ex.: `checkout.completed`) | `WebhookEvent` marcado como `Failed` com `LastError` "Unsupported..." |
| AbacatePay sem metadata no payload | `WebhookEvent` marcado como `Failed` (sem roteamento seguro) |
| AbacatePay com `ProviderAccount` inativo/inexistente | `WebhookEvent` marcado como `Failed` com `LastError` seguro |
| Outros providers sem assinatura | Recebidos normalmente (legacy) |

## Tests esperados

- Webhook valido.
- JSON invalido.
- Duplicado.
- Provider desconhecido.
- Assinatura invalida quando provider suportar.
- Pagamento inexistente e fora de ordem.
- AbacatePay: HMAC valido/invalido/ausente, payload malformado, evento unsupported, metadata ausente, secret ausente.
- Nenhum teste loga ou persiste `webhookSecret`, `apiKey`, signature ou body bruto.
- Endpoints de configuracao (Slice 2-C, 2026-06-30):
  - PUT sucesso preserva `apiKey` no JSON protegido e atualiza `webhookSecret` quando fornecido.
  - PUT sem `webhookSecret` no body preserva o valor atual (ou o `secret` legacy).
  - PUT retorna 404 quando o `providerAccountId` nao existe no escopo (tenant+application) e 409 quando a conta existe mas esta inativa ou nao e AbacatePay.
  - PUT chama `IProviderWebhookManagementClient.RegisterWebhookAsync` apenas quando caller seta `registerRemotely=true`, forneceu `webhookSecret`, e a feature policy (`Providers:AbacatePay:AllowWebhookRegistration`) esta ligada; caso contrario grava `RemoteRegistrationDeferred` (ou `NotRegistered`) sem chamar o client.
  - GET nega qualquer chave/secret/credentials no response (reflexao no DTO).
  - Validator: `callbackUrl` fora do padrao HTTPS/SSRF rejeitado, eventos fora da whitelist `transparent.*` rejeitados, `webhookSecret` fora da faixa 16-500 chars rejeitado.
  - Migration: `provider_accounts.webhook_events` permanece como `text` (NAO `jsonb`) para garantir round-trip byte-exact. Anti-regression documentada na migration `20260630001726_AddProviderAccountWebhookColumns` e no entry de learnings de 2026-06-30.

## Arquivos relacionados

- `docs/api/webhooks.md`
- `docs/specs/008-provider-adapters.md`
- `docs/specs/011-security-and-compliance.md`
- `src/PaymentHub.Api/Controllers/ProviderWebhooksController.cs`
- `src/PaymentHub.Application/Webhooks/WebhookHandlers.cs`
- `src/PaymentHub.Application/Abstractions/Providers/ProviderModels.cs`
- `src/PaymentHub.Infrastructure.Providers/AbacatePay/AbacatePayProviderAdapter.cs`
- `src/PaymentHub.Infrastructure.Providers/AbacatePay/Webhooks/*`
- `src/PaymentHub.Infrastructure.Providers/AbacatePay/Models/AbacatePayWebhookEnvelope.cs`
- `src/PaymentHub.Worker/WebhookProcessorWorker.cs`
- `docs/audits/slice-2b-abacatepay-webhooks-report-2026-06-29.md`
