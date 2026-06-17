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
- Duplicado com mesmo `provider_code + provider_event_id` retorna 202 com referencia ao evento conhecido.
- Payload invalido pode ser recusado com 400 quando nao for JSON parseavel; se aceito, deve ser persistido com status adequado.

## Contratos

```http
POST /api/v1/webhooks/{providerCode}
X-Provider-Event-Id: <event_id opcional>
X-Provider-Event-Type: <event_type opcional>
X-Provider-Signature: <signature opcional>
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
| pagamento inexistente | Retry ou Failed com `last_error` claro |

## Criterios de aceite

- Recebimento e rapido e nao depende do provider ficar disponivel.
- Inbox contem payload bruto suficiente para reprocessamento.
- Logs nao expõem assinatura ou payload sensivel.

## Testes esperados

- Webhook valido.
- JSON invalido.
- Duplicado.
- Provider desconhecido.
- Assinatura invalida quando provider suportar.
- Pagamento inexistente e fora de ordem.

## Arquivos relacionados

- `docs/api/webhooks.md`
- `src/PaymentHub.Api/Controllers/ProviderWebhooksController.cs`
- `src/PaymentHub.Application/Webhooks/WebhookHandlers.cs`
- `src/PaymentHub.Worker/WebhookProcessorWorker.cs`
