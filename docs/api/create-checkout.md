# API: criação de checkout

Endpoint: `POST /api/v1/checkouts`

## Headers obrigatórios

| Header | Valor | Obrigatório |
|--------|-------|-------------|
| `Authorization` | `Bearer <api_key>` | sim |
| `X-Tenant-Id` | `<tenant_id>` (Guid) | sim |
| `X-Application-Id` | `<application_id>` (Guid) | sim |
| `Idempotency-Key` | chave única por checkout | sim |
| `X-Provider` | `Fake`, `AbacatePay`, `Stripe`, `MercadoPago` | opcional |

> Se `X-Provider` for omitido, o sistema usa o `DefaultProvider` configurado para a `ApplicationClient`.
> Se nenhum default estiver cadastrado, usa `Fake` somente em `Development`.
> Um `X-Provider` explícito inválido ou sem conta ativa falha; o sistema não troca silenciosamente para outro provider.

## Payload

```json
{
  "externalReference": "job-search-order-123",
  "customer": {
    "name": "Cliente Teste",
    "email": "cliente@email.com"
  },
  "items": [
    {
      "id": "premium-monthly",
      "name": "Plano Premium Mensal",
      "quantity": 1,
      "unitAmount": 2990
    }
  ],
  "currency": "BRL",
  "successUrl": "https://querovagastech.com.br/pagamento/sucesso",
  "cancelUrl": "https://querovagastech.com.br/pagamento/cancelado",
  "metadata": {
    "source": "job-search",
    "plan": "premium"
  }
}
```

> `items[].unitAmount` é em **centavos**. O valor total do pagamento é a soma de `unitAmount * quantity` para todos os itens.

## Resposta (201 Created)

```json
{
  "paymentId": "3f1c8e0c-9f0d-4a6b-8a1d-31b7bdf7f6a1",
  "status": "Pending",
  "provider": "Fake",
  "checkoutUrl": "https://fake-checkout.local/payments/3f1c8e0c-9f0d-4a6b-8a1d-31b7bdf7f6a1"
}
```

## Comportamento

- `Idempotency-Key` é obrigatório. Se a mesma chave for reutilizada com o mesmo payload, o endpoint retorna o pagamento já criado (200/201).
- Se a mesma chave for reutilizada com payload diferente, o endpoint retorna `409 Conflict` e não chama o provider.
- Se o `Idempotency-Key` for enviado sem `IdempotencyKey` no banco, um novo pagamento é criado.
- O status inicial é `Created`. Após o provider confirmar, o `WebhookProcessorWorker` atualiza para `Pending` (na resposta imediata o status é `Pending` porque o adapter Fake já confirma a URL).
- Um `OutboxEvent` é gerado com `eventType = payment.checkout.created` e despachado via `OutboxDispatcherWorker` para a `WebhookUrl` da `ApplicationClient`.

## Erros

| Status | Causa |
|--------|-------|
| 400 | Payload inválido ou `Idempotency-Key` ausente |
| 401 | API Key inválida ou Tenant/Application incompatíveis |
| 409 | `Idempotency-Key` reutilizada com payload diferente |
| 422 | Provider inválido, provider sem conta ativa, default ausente ou falha ao criar checkout |

## Exemplo de curl

```bash
curl -X POST http://localhost:8080/api/v1/checkouts \
  -H "Authorization: Bearer $API_KEY" \
  -H "X-Tenant-Id: $TENANT_ID" \
  -H "X-Application-Id: $APPLICATION_ID" \
  -H "Idempotency-Key: $(uuidgen)" \
  -H "Content-Type: application/json" \
  -d '{
    "externalReference": "job-search-order-123",
    "customer": {
      "name": "Cliente Teste",
      "email": "cliente@email.com"
    },
    "items": [
      { "id": "premium-monthly", "name": "Plano Premium Mensal", "quantity": 1, "unitAmount": 2990 }
    ],
    "currency": "BRL",
    "successUrl": "https://querovagastech.com.br/pagamento/sucesso",
    "cancelUrl": "https://querovagastech.com.br/pagamento/cancelado",
    "metadata": { "source": "job-search", "plan": "premium" }
  }'
```

## Fluxo end-to-end

1. Aplicação cliente chama o endpoint.
2. `CreateCheckoutHandler` valida idempotência.
3. `PaymentProviderRouter` resolve o adapter (Fake por padrão).
4. `IPaymentProviderAdapter.CreateCheckoutAsync` é chamado.
5. `Payment` é persistido com `Status = Pending` e um `PaymentAttempt` bem-sucedido.
6. `IdempotencyKey` é persistido com hash do request.
7. `OutboxEvent` é gerado.
8. Resposta 201 com `checkoutUrl` para o cliente.
9. `OutboxDispatcherWorker` envia `payment.checkout.created` para a `WebhookUrl` da aplicação cliente.
