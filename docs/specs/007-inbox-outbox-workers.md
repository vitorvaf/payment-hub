# Inbox, Outbox e Workers

## Objetivo

Definir processamento assincrono, retries, concorrencia e idempotencia para Inbox e Outbox.

## Escopo

- `WebhookProcessorWorker`.
- `OutboxDispatcherWorker`.
- Estados de `WebhookEvent` e `OutboxEvent`.
- Retry policy, logs e falha permanente.

## Fora de escopo

- Broker externo no MVP.

## Regras obrigatorias

- Workers devem ser idempotentes e tolerantes a reprocessamento.
- Selecionar apenas eventos `Pending` cujo `next_retry_at` esteja vazio ou vencido.
- Marcar `Processing` ou usar mecanismo equivalente antes de executar trabalho critico.
- Evitar que dois workers processem o mesmo evento; em Postgres futuro, preferir lock transacional ou `FOR UPDATE SKIP LOCKED`.
- Atualizar `retry_count`, `last_error` e `next_retry_at` em falhas.
- Apos limite de tentativas, marcar `Failed` e exigir intervencao manual.

## Contratos

Retry policy:

```text
1a tentativa: imediato
2a tentativa: +1 minuto
3a tentativa: +5 minutos
4a tentativa: +15 minutos
5a tentativa: +1 hora
depois: Failed
```

Estados:

| Entidade | Estados |
|----------|---------|
| `WebhookEvent` | `Pending`, `Processing`, `Processed`, `Failed` |
| `OutboxEvent` | `Pending`, `Processing`, `Sent`, `Failed` |

## Criterios de aceite

- Evento processado com sucesso e marcado como finalizado.
- Falhas temporarias sao reagendadas.
- Falha permanente preserva erro e permite acao manual futura.
- Webhook interno e assinado com HMAC.

## Testes esperados

- Selecao de pendentes.
- Retry count e next retry.
- Falha apos 5 tentativas.
- Dispatch HTTP 2xx versus nao 2xx.
- Reprocessamento idempotente.

## Arquivos relacionados

- `src/PaymentHub.Worker/WebhookProcessorWorker.cs`
- `src/PaymentHub.Worker/OutboxDispatcherWorker.cs`
- `src/PaymentHub.Domain/Services/RetryPolicy.cs`
- `src/PaymentHub.Domain/Entities/WebhookEvent.cs`
- `src/PaymentHub.Domain/Entities/OutboxEvent.cs`
