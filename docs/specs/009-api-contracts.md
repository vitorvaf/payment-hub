# Contratos HTTP

## Objetivo

Consolidar endpoints atuais e esperados, separando o que existe do que e futuro.

## Escopo

- Endpoints REST versionados.
- Autenticacao, headers, requests, responses, erros, eventos e tabelas afetadas.

## Fora de escopo

- Especificacao OpenAPI completa.

## Regras obrigatorias

- Endpoints autenticados usam API Key server-to-server.
- Webhooks externos sao anonimos no middleware, mas precisam de validacao de provider quando suportada.
- Endpoints futuros devem ser marcados como futuro ate existirem no codigo.

## Resumo dos contratos

| Endpoint | Status | Objetivo | Auth | Tabelas/eventos |
|----------|--------|----------|------|-----------------|
| `POST /api/v1/tenants` | atual | Criar tenant | anonimo/admin futuro | `tenants`, `audit_logs` futuro |
| `POST /api/v1/applications` | atual | Criar application e chave inicial | anonimo/admin futuro | `application_clients`, `api_keys` |
| `POST /api/v1/provider-accounts` | atual | Criar conta de provider | API Key/admin futuro | `provider_accounts`, `audit_logs` futuro |
| `POST /api/v1/checkouts` | atual | Criar checkout | API Key | `payments`, `payment_attempts`, `idempotency_keys`, `outbox_events` |
| `GET /api/v1/payments/{paymentId}` | atual | Consultar pagamento | API Key | `payments` |
| `GET /api/v1/payments` | atual | Listar pagamentos | API Key | `payments` |
| `POST /api/v1/webhooks/{providerCode}` | atual | Receber webhook externo | anonimo/provider signature | `webhook_events` |
| `GET /health` | atual | Health basico | anonimo | nao aplicavel |
| `GET /health/ready` | atual | Readiness | anonimo | conexao Postgres |

Headers comuns:

```http
Authorization: Bearer <api_key>
X-Tenant-Id: <tenant_id>
X-Application-Id: <application_id>
```

## Endpoints

### `POST /api/v1/tenants`

- Status: atual.
- Objetivo: criar tenant.
- Autenticacao: anonimo no MVP; admin futuro.
- Request: nome do tenant.
- Response: identificador e dados basicos do tenant.
- Erros: `400` para payload invalido.
- Tabelas afetadas: `tenants`; `audit_logs` futuro.
- Specs relacionadas: `002-multitenancy-and-authentication.md`.

### `POST /api/v1/applications`

- Status: atual.
- Objetivo: criar application client e chave inicial.
- Autenticacao: anonimo no MVP; admin futuro.
- Request: tenant, nome, webhook URL/secret quando aplicavel e provider default opcional.
- Response: application, prefixo auditavel e API Key exibida uma unica vez.
- Erros: `400` para payload invalido.
- Tabelas afetadas: `application_clients`, `api_keys`.
- Specs relacionadas: `002-multitenancy-and-authentication.md`, `011-security-and-compliance.md`.

### `POST /api/v1/provider-accounts`

- Status: atual.
- Objetivo: cadastrar conta ativa de provider para o tenant/application autenticado.
- Autenticacao: API Key; admin futuro.
- Tenant e application: derivados exclusivamente do contexto autenticado (`ITenantContext`). O body **nao** aceita `tenantId`/`applicationId`. Valores divergentes enviados no body sao ignorados por design (campos removidos do DTO). Esta restricao existe para impedir cross-tenant/cross-application registration por uma application autenticada.
- Request: provider, ambiente, nome, credenciais e flag default.
- Response: provider account sem segredo em claro.
- Erros: `400` para payload invalido; `401` para API Key invalida ou contexto autenticado ausente; `403` para tenant/application inativo (retornado pelo middleware).
- Tabelas afetadas: `provider_accounts`; `audit_logs` futuro.
- Specs relacionadas: `002-multitenancy-and-authentication.md`, `008-provider-adapters.md`, `011-security-and-compliance.md`.

### `POST /api/v1/checkouts`

- Status: atual.
- Objetivo: criar checkout hospedado.
- Autenticacao: API Key.
- Headers: `Idempotency-Key` obrigatorio; `X-Provider` opcional e validado quando informado.
- Request: `externalReference`, `customer`, `items`, `currency`, URLs de retorno e `metadata`.
- Response: `paymentId`, `status`, `provider` e `checkoutUrl`.
- Erros: `400` para payload invalido ou idempotency ausente; `409` para mesma key com payload diferente; `422` para provider invalido, provider sem conta ativa, default ausente ou falha do provider.
- Tabelas afetadas: `payments`, `payment_attempts`, `idempotency_keys`, `outbox_events`.
- Eventos gerados: `payment.checkout.created` em sucesso.
- Payload interno gerado deve conter `eventId`, `eventType`, `paymentId`, `externalReference`, `amount`, `currency`, `provider`, `status`, `providerPaymentId` e `occurredAt`.
- Specs relacionadas: `005-checkout-creation.md`, `004-payment-lifecycle.md`.

### `GET /api/v1/payments/{paymentId}`

- Status: atual.
- Objetivo: consultar pagamento por id.
- Autenticacao: API Key.
- Response: dados canonicos do pagamento, sem dados sensiveis de provider.
- Erros: `401` para API Key invalida; `404` quando nao encontrado para o tenant/application.
- Tabelas afetadas: `payments`.
- Eventos gerados: nenhum.
- Specs relacionadas: `004-payment-lifecycle.md`.

### `GET /api/v1/payments`

- Status: atual.
- Objetivo: listar pagamentos do tenant/application autenticado.
- Autenticacao: API Key.
- Request: parametros de paginacao quando suportados pelo controller.
- Response: lista paginada ou limitada de pagamentos.
- Erros: `401` para API Key invalida.
- Tabelas afetadas: `payments`.
- Eventos gerados: nenhum.
- Specs relacionadas: `004-payment-lifecycle.md`.

### `POST /api/v1/webhooks/{providerCode}`

- Status: atual.
- Objetivo: receber webhook externo de provider e persistir Inbox.
- Autenticacao: anonimo no middleware; assinatura do provider quando suportada pelo adapter.
- Headers: `X-Provider-Event-Id`, `X-Provider-Event-Type`, `X-Provider-Signature`.
- Request: payload bruto do provider.
- Response: `202 Accepted` com `webhookId`.
- Erros: `400` para JSON invalido quando o controller precisar ler tipo do evento; `422` para provider desconhecido ou falha de persistencia.
- Tabelas afetadas: `webhook_events`; processamento posterior pode atualizar `payments`, `payment_attempts` e `outbox_events`.
- Eventos gerados: eventos internos de pagamento quando houver transicao canonica valida.
- Eventos internos enviados por Outbox devem usar `eventId` como id estavel de idempotencia do consumidor e `eventType` como tipo do evento.
- Specs relacionadas: `006-provider-webhooks.md`, `007-inbox-outbox-workers.md`, `008-provider-adapters.md`.

### `PUT /api/v1/provider-accounts/{providerAccountId}/webhook` (Slice 2-C — 2026-06-30)

- Status: atual (AbacatePay apenas).
- Objetivo: configurar (substituir) a inscricao de webhook AbacatePay para um `ProviderAccount` existente. Persiste `callbackUrl`, lista de eventos e dispara (opcionalmente) chamada de registro remoto.
- Autenticacao: API Key.
- Tenant e application: derivados exclusivamente de `ITenantContext`. O path carrega `providerAccountId`; o body **nao** aceita `tenantId`/`applicationId` (campos removidos do DTO).
- Request (body JSON):

  ```json
  {
    "callbackUrl": "https://merchant.example.com/webhooks/abacate",
    "events": ["transparent.completed", "transparent.refunded"],
    "webhookSecret": "abcd...16-500chars",
    "registerRemotely": false
  }
  ```

  Todos os campos sao opcionais. `events` aceita apenas `transparent.completed|refunded|disputed|lost` (whitelist). `webhookSecret` segue 16-500 chars; nunca e persistido em coluna propria, e entra apenas no JSON protegido de `ProviderAccount.EncryptedCredentials`.
- Response: o mesmo DTO de `GET` (vide abaixo).
- Erros:
  - `400` para payload invalido (URL nao-HTTPS, evento fora da whitelist, segredo fora da faixa).
  - `401` quando `ITenantContext` falha (sem tenant/application autenticado).
  - `404` quando `providerAccountId` nao existe no escopo (tenant + application).
  - `409` quando a conta existe mas esta inativa, OU quando a conta nao e AbacatePay (Slice 2-C cobre apenas AbacatePay).
  - `200` em sucesso.
- Tabelas afetadas: `provider_accounts` (4 colunas non-sensitive + `encrypted_credentials` quando o segredo e atualizado).
- Eventos gerados: nenhum.
- Configuracao remota (Slice 2-C.1 — 2026-06-30): o client real `AbacatePayWebhookManagementClient` faz `POST /webhooks/create` no upstream AbacatePay. Requer todos os 3 gates: `registerRemotely=true` (request), `webhookSecret` nao-nulo (request), `Providers:AbacatePay:AllowWebhookRegistration=true` (config). Quando algum gate falha, `webhook_remote_status` e gravado como `RemoteRegistrationDeferred` ou `NotRegistered` (sem chamada HTTP). Quando todos os gates passam: o client real chama o upstream com `Authorization: Bearer {apiKey}` (apiKey extraido do `EncryptedCredentials` via `IProviderAccountCredentialsReader`), body `{ name, endpoint, secret, events }`, e mapeia a resposta para `Registered` (2xx + envelope `success=true` + `data.id`) ou `RegistrationFailed` (qualquer outro caso). O response do PUT permanece o mesmo do Slice 2-C (`{ ..., "remoteRegistrationStatus": "Registered" | "RegistrationFailed" | "RemoteRegistrationDeferred" | "NotRegistered" }`). **Nunca** expoe `apiKey`, `webhookSecret` ou `encryptedCredentials` no response (validado por reflexao nos 23 testes da suite 2-C.1).
- Specs relacionadas: `006-provider-webhooks.md`, `008-provider-adapters.md`, `011-security-and-compliance.md` (categoria "gerenciamento de webhook via API").

### `GET /api/v1/provider-accounts/{providerAccountId}/webhook` (Slice 2-C — 2026-06-30)

- Status: atual (AbacatePay apenas).
- Objetivo: retornar a configuracao de webhook AbacatePay para auditoria ou consulta operacional.
- Autenticacao: API Key.
- Tenant e application: `ITenantContext`.
- Response:

  ```json
  {
    "providerAccountId": "guid",
    "providerCode": "AbacatePay",
    "environment": "Sandbox",
    "callbackUrl": "https://merchant.example.com/webhooks/abacate",
    "events": ["transparent.completed", "transparent.refunded"],
    "hasWebhookSecret": true,
    "remoteRegistrationStatus": "Registered",
    "configuredAt": "2026-06-30T12:34:56.789Z",
    "updatedAt": "2026-06-30T12:34:56.789Z"
  }
  ```

  `hasWebhookSecret` indica apenas se ha um segredo armazenado (boolean), **nunca** o valor. `remoteRegistrationStatus` e a string do enum `ProviderWebhookRemoteStatus` (`NotRegistered`, `Registered`, `RegistrationFailed`, `RemoteRegistrationDeferred`).
- Erros: mesma matriz do PUT.
- Tabelas afetadas: `provider_accounts` (somente leitura).
- Specs relacionadas: mesmas do PUT.

### `GET /health`

- Status: atual.
- Objetivo: health basico da API.
- Autenticacao: anonimo.
- Response: status de saude.
- Tabelas afetadas: nenhuma.
- Specs relacionadas: `012-observability-and-audit.md`.

### `GET /health/ready`

- Status: atual.
- Objetivo: readiness da API e dependencias essenciais.
- Autenticacao: anonimo.
- Response: status de readiness.
- Tabelas afetadas: conexao com Postgres quando configurada.
- Specs relacionadas: `012-observability-and-audit.md`.

## Criterios de aceite

- Mudancas em endpoint atual atualizam esta spec.
- Erros de producao nao expõem stack trace.
- Responses nao incluem secrets.

## Testes esperados

- Autenticacao por endpoint.
- Status codes esperados.
- Contratos de payload para checkout e webhooks.
- Health/readiness.

## Arquivos relacionados

- `src/PaymentHub.Api/Controllers/`
- `docs/api/create-checkout.md`
- `docs/api/webhooks.md`
