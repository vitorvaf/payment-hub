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
- Objetivo: cadastrar conta ativa de provider para tenant/application.
- Autenticacao: API Key; admin futuro.
- Request: provider, ambiente, nome, credenciais e flag default.
- Response: provider account sem segredo em claro.
- Erros: `400` para payload invalido; `401` para API Key invalida.
- Tabelas afetadas: `provider_accounts`; `audit_logs` futuro.
- Specs relacionadas: `008-provider-adapters.md`, `011-security-and-compliance.md`.

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
- Specs relacionadas: `006-provider-webhooks.md`, `007-inbox-outbox-workers.md`, `008-provider-adapters.md`.

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
