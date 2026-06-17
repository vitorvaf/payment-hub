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

## Contratos

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
