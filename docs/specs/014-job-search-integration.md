# Integracao Job Search

## Objetivo

Definir a integracao esperada com Job Search / Quero Vagas Tech.

## Escopo

- Fluxo de criacao de checkout.
- Payload de eventos internos.
- Responsabilidades de Payment Hub e Job Search.

## Fora de escopo

- Implementar liberacao de plano dentro do Payment Hub.
- Criar UI ou painel do Job Search.

## Regras obrigatorias

- Payment Hub nao libera recurso diretamente.
- Job Search decide a regra de negocio pos-pagamento.
- Job Search deve validar assinatura HMAC.
- Job Search deve tratar eventos duplicados com idempotencia propria usando `eventId` como chave preferencial.
- `paymentId + status` pode ser fallback operacional, mas nao substitui `eventId`.

## Contratos

Fluxo:

1. Job Search cria ordem interna.
2. Job Search chama Payment Hub para criar checkout.
3. Payment Hub retorna `checkoutUrl`.
4. Usuario paga no checkout hospedado.
5. Provider chama webhook externo do Payment Hub.
6. Payment Hub processa e atualiza status.
7. Payment Hub envia webhook interno para Job Search.
8. Job Search valida assinatura HMAC.
9. Job Search libera plano/recurso.

Payload interno esperado:

```json
{
  "eventId": "2d5f1a98-07cc-4701-b7e7-4adcf60437e8",
  "eventType": "payment.approved",
  "paymentId": "guid",
  "externalReference": "job-search-order-123",
  "amount": 2990,
  "currency": "BRL",
  "provider": "Fake",
  "status": "Approved",
  "providerPaymentId": "fake_...",
  "occurredAt": "2026-06-16T12:00:00Z"
}
```

`eventId` deve ser o id do `OutboxEvent`. Reprocessar o mesmo `OutboxEvent` mantem o mesmo `eventId`, mesmo que uma nova tentativa use outro timestamp de assinatura.
`eventType` e obrigatorio e deve bater com o header `X-PaymentHub-Event-Type`.
`occurredAt` representa o momento do evento no Payment Hub.

Headers de assinatura:

```http
Content-Type: application/json
X-PaymentHub-Event-Id: <eventId>
X-PaymentHub-Event-Type: payment.approved
X-PaymentHub-Timestamp: <unix_time_seconds>
X-PaymentHub-Signature: <hex_lowercase_hmac_sha256>
```

A string assinada e `{timestamp}.{rawBody}` usando UTF-8.
`rawBody` deve ser o corpo HTTP exatamente como recebido, sem reserializar o JSON.
O Job Search deve rejeitar timestamps fora da janela recomendada de 5 minutos,
comparar assinatura em tempo constante quando possivel e aplicar idempotencia por `eventId`.

Eventos:

```text
payment.checkout.created
payment.pending
payment.approved
payment.rejected
payment.expired
payment.cancelled
payment.refunded
payment.chargeback
payment.failed
```

## Criterios de aceite

- Checkout usa `externalReference` da ordem do Job Search.
- Evento aprovado permite reconciliar pagamento por `paymentId` e `externalReference`.
- Duplicatas nao liberam recurso duas vezes.

## Testes esperados

- Fluxo end-to-end com Fake.
- Validacao HMAC no consumidor.
- Duplicidade de evento interno.
- Status rejeitado/expirado nao libera plano.

## Arquivos relacionados

- `docs/api/create-checkout.md`
- `docs/api/webhooks.md`
- `src/PaymentHub.Worker/OutboxDispatcherWorker.cs`
