# Visão geral da arquitetura

Payment Gateway MVP é um orquestrador de pagamentos multitenant. O objetivo é centralizar integrações com provedores como Abacate Pay, Stripe e Mercado Pago, oferecendo uma API consistente para produtos consumidores (ex.: Job Search / Quero Vagas Tech).

## Objetivos

- Cadastrar tenants, aplicações clientes e contas de provedores.
- Criar checkouts de pagamento com checkout hospedado (sem armazenar cartão/CVV).
- Receber webhooks externos em uma tabela de Inbox, processá-los de forma assíncrona e atualizar o estado canônico do pagamento.
- Gerar eventos de saída via Outbox e despachar webhooks internos para aplicações clientes.
- Evoluir para RabbitMQ/Kafka/Azure Service Bus sem reescrever o domínio.

## Camadas

```text
PaymentHub.Api              → camada de entrada HTTP (REST + Swagger)
PaymentHub.Application      → casos de uso, DTOs, orquestração
PaymentHub.Domain           → entidades, enums, value objects, regras
PaymentHub.Infrastructure.Postgres  → DbContext, migrations, repositórios
PaymentHub.Infrastructure.Providers → adapters de provedores de pagamento
PaymentHub.Worker           → BackgroundServices de Inbox/Outbox
```

Regras de dependência:

- Domain não depende de nenhuma outra camada.
- Application depende apenas de Domain.
- Infrastructure pode depender de Application e Domain.
- API e Worker dependem de Application, Domain e Infrastructure (somente para DI e config).

## Status canônico de pagamento

```text
Created
Pending
Processing
RequiresAction
Approved
Rejected
Cancelled
Expired
Refunded
Chargeback
Failed
```

O `Domain.Services.PaymentStatusMapper` traduz o vocabulário bruto de cada provedor para o status canônico interno.

## Fluxo de checkout

```text
Cliente → POST /api/v1/checkouts
        ↓
[ApiKeyAuthenticationMiddleware]
        ↓
CheckoutsController
        ↓
CreateCheckoutHandler (idempotency-key obrigatório)
        ↓
PaymentProviderRouter → IPaymentProviderAdapter (Fake por padrão)
        ↓
Payment persistido com PaymentStatus.Pending + PaymentAttempt
        ↓
OutboxEvent "payment.checkout.created" enfileirado
        ↓
HTTP 201 + checkoutUrl
```

## Fluxo de webhook externo

```text
Provider → POST /api/v1/webhooks/{providerCode}
        ↓
ProviderWebhooksController persiste WebhookEvent (status=Pending) e responde 2xx
        ↓
WebhookProcessorWorker (BackgroundService)
        ↓
IPaymentProviderAdapter.ParseWebhookAsync
        ↓
ProcessWebhookEventHandler
        ↓
Payment atualizado (status canônico) + PaymentAttempt
        ↓
OutboxEvent gerado se houver mudança de status
        ↓
WebhookEvent marcado como Processed
```

## Fluxo de outbox interno

```text
OutboxEvent (Pending) → OutboxDispatcherWorker (BackgroundService)
        ↓
IApplicationWebhookDispatcher (HTTP)
        ↓
POST {application.WebhookUrl}
        ↓
X-PaymentHub-Signature: HMAC-SHA256(payload, WebhookSecret)
        ↓
200 OK → OutboxEvent.Sent
Erro → RetryPolicy (0s, 1m, 5m, 15m, 1h) → Failed após 5 tentativas
```

## Inbox/Outbox no PostgreSQL

Tabelas principais:

- `webhook_events` (Inbox) — payload bruto em JSONB, status, retry_count, next_retry_at, índice único em (provider_code, provider_event_id).
- `outbox_events` (Outbox) — payload em JSONB, status, retry_count, next_retry_at.

Sem broker externo. Os workers consultam o banco periodicamente. Quando o sistema precisar migrar para RabbitMQ/Kafka/Azure Service Bus, basta substituir o `OutboxDispatcherWorker` por um publisher para o broker. O domínio permanece inalterado.

## Multitenancy

- Toda tabela carrega `tenant_id` e/ou `application_id` quando aplicável.
- Toda chamada autenticada exige `X-Tenant-Id` e `X-Application-Id` além do API Key.
- O `ITenantContext` resolve Tenant e Application a partir do `HttpContext` para uso nos handlers.
- `ApiKey.TenantId` e `ApiKey.ApplicationId` validam o escopo da chave.

## Segurança inicial

- API Key server-to-server via `Authorization: Bearer <api_key>`.
- API Key armazenada apenas como hash (HMAC-SHA256 com `PaymentHub:ApiKeyHashSecret`).
- Credenciais de provedores criptografadas com AES (key derivada de `PaymentHub:CredentialEncryptionKey`).
- Webhooks internos assinados com HMAC-SHA256 (header `X-PaymentHub-Signature`).
- Validação opcional de assinatura em webhooks externos (depende do provider; hoje o payload é persistido e a validação é feita pelo adapter).
- `AuditLog` para ações administrativas sensíveis.
- Sem cartão, sem CVV, sem dados sensíveis persistidos.

## Estado de ProviderAccount

- `ProviderCode` (Fake, AbacatePay, Stripe, MercadoPago)
- `ProviderEnvironment` (Sandbox, Production)
- `IsDefault` indica o provider preferencial da ApplicationClient
- `EncryptedCredentials` armazena JSON criptografado (`apiKey` + `secret` opcionais)

## Próximos passos

- Implementar HTTP real de Abacate Pay e validar fluxo completo.
- Adicionar testes de integração com Testcontainers (PostgreSQL).
- Adicionar `dotnet user-secrets` para credenciais locais.
- Adicionar pipeline CI para `dotnet test` + `docker compose config`.
- Migrar outbox para broker externo sem alterar domínio.
