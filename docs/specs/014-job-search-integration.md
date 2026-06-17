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
- Job Search deve tratar eventos duplicados com idempotencia propria usando `eventId` ou `paymentId + status`.

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
  "paymentId": "guid",
  "externalReference": "job-search-order-123",
  "amount": 2990,
  "currency": "BRL",
  "provider": "Fake",
  "status": "Approved",
  "providerPaymentId": "fake_...",
  "updatedAt": "2026-06-16T12:00:00Z"
}
```

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
