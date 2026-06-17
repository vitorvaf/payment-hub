# Criacao de Checkout

## Objetivo

Formalizar o contrato de `POST /api/v1/checkouts`, idempotencia, selecao de provider e resposta.

## Escopo

- Headers, payload, response e erros.
- Calculo de valor.
- Idempotencia por tenant/application/key.
- Provider explicito ou default.

## Fora de escopo

- Implementar provider real.
- Processar pagamento dentro do Payment Hub.

## Regras obrigatorias

- Sem `Idempotency-Key`, retornar 400.
- Mesmo `tenant_id + application_id + idempotency_key + request_hash` retorna o mesmo resultado.
- Mesma idempotency key com payload diferente retorna conflito, preferencialmente 409.
- `AmountInCents` e calculado por `items[].unitAmount * quantity`.
- Moeda inicial: `BRL`.
- `X-Provider`, quando enviado, deve ser validado.
- `X-Provider` invalido deve falhar; o sistema nunca deve trocar silenciosamente o provider solicitado por outro.
- `X-Provider` valido, mas sem `ProviderAccount` ativa para tenant/application, deve falhar.
- Sem provider explicito, usar default da `ApplicationClient`.
- Sem default em Development, permitir Fake provider.
- Em Production, nao usar Fake provider automaticamente.

## Contratos

```http
POST /api/v1/checkouts
Authorization: Bearer <api_key>
X-Tenant-Id: <tenant_id>
X-Application-Id: <application_id>
Idempotency-Key: <idempotency_key>
X-Provider: <provider_code opcional>
Content-Type: application/json
```

```json
{
  "externalReference": "job-search-order-123",
  "customer": { "name": "Cliente Teste", "email": "cliente@email.com" },
  "items": [
    { "id": "premium-monthly", "name": "Plano Premium Mensal", "quantity": 1, "unitAmount": 2990 }
  ],
  "currency": "BRL",
  "successUrl": "https://querovagastech.com.br/pagamento/sucesso",
  "cancelUrl": "https://querovagastech.com.br/pagamento/cancelado",
  "metadata": { "source": "job-search", "plan": "premium" }
}
```

Resposta de sucesso:

```json
{
  "paymentId": "3f1c8e0c-9f0d-4a6b-8a1d-31b7bdf7f6a1",
  "status": "Pending",
  "provider": "Fake",
  "checkoutUrl": "https://fake-checkout.local/payments/3f1c8e0c-9f0d-4a6b-8a1d-31b7bdf7f6a1"
}
```

Erros:

| Status | Causa |
|--------|-------|
| 400 | Payload invalido ou `Idempotency-Key` ausente |
| 401 | API Key invalida ou tenant/application incompativeis |
| 409 | Idempotency key reutilizada com payload diferente |
| 422 | Provider invalido, provider sem conta ativa, default ausente ou falha ao criar checkout |

## Criterios de aceite

- Checkout bem-sucedido cria `Payment`, `PaymentAttempt`, `IdempotencyKey` e `OutboxEvent`.
- Resposta bem-sucedida contem `checkoutUrl`.
- Request repetido com mesmo hash nao cria novo pagamento.
- Erros nao logam API Key nem payload sensivel.

## Testes esperados

- Sucesso com Fake.
- Idempotency key ausente.
- Repeticao com mesmo payload.
- Repeticao com payload diferente.
- Provider explicito valido/invalido.
- Falha do provider.

## Arquivos relacionados

- `docs/api/create-checkout.md`
- `src/PaymentHub.Api/Controllers/CheckoutsController.cs`
- `src/PaymentHub.Application/Checkouts/CreateCheckoutHandler.cs`
- `tests/PaymentHub.UnitTests/Application/CreateCheckoutHandlerTests.cs`
