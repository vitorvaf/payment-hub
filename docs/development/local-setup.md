# Setup local

Este documento explica como rodar o Payment Gateway MVP localmente usando Docker Compose ou execução nativa com .NET.

## Pré-requisitos

- .NET SDK 10.0
- Docker + Docker Compose (opcional, mas recomendado)
- PostgreSQL 16 (caso prefira rodar nativamente)

## Variáveis de ambiente

Copie `.env.example` para `.env` e ajuste os valores:

```env
ASPNETCORE_ENVIRONMENT=Development
ConnectionStrings__Postgres=Host=localhost;Port=5432;Database=payment_gateway;Username=payment_gateway;Password=payment_gateway
PaymentHub__DefaultProvider=Fake
PaymentHub__ApiKeyHashSecret=dev-secret-change-me
PaymentHub__CredentialEncryptionKey=dev-encryption-key-change-me-32bytes
```

> Use valores locais, nunca commite `.env` real. Para produção, configure via secret manager (Azure Key Vault, AWS Secrets Manager, etc.).

## Opção 1: Docker Compose (recomendado)

```bash
docker compose up -d
docker compose logs -f payment-gateway-api
docker compose logs -f payment-gateway-worker
```

Serviços expostos:

| Serviço | Porta | Descrição |
|---------|-------|-----------|
| `payment-hub-api` | `http://localhost:8080` | API REST |
| `payment-hub-worker` | — | Processa Inbox e Outbox |
| `postgres` | `localhost:5432` | Banco de dados |
| `pgadmin` | `http://localhost:5050` | UI para Postgres |

Para parar e remover containers/volumes:

```bash
docker compose down -v
```

## Opção 2: nativo

1. Iniciar PostgreSQL (Docker, brew, etc.) e criar o banco `payment_gateway` com usuário `payment_gateway`/`payment_gateway`.
2. Definir `ConnectionStrings__Postgres` no ambiente.
3. Aplicar migrations:

```bash
export DOTNET_ROOT=/usr/lib/dotnet  # se necessário em sistemas com runtimes múltiplos
dotnet ef database update \
  --project src/PaymentHub.Infrastructure.Postgres \
  --startup-project src/PaymentHub.Api
```

> O `IDesignTimeDbContextFactory` em `PaymentHub.Infrastructure.Postgres` permite gerar migrations sem subir a API. Você pode ajustar a connection string via `PAYMENTHUB_DESIGN_CONNECTION`.

4. Rodar API e Worker em terminais separados:

```bash
dotnet run --project src/PaymentHub.Api
dotnet run --project src/PaymentHub.Worker
```

5. Acessar:

- API: `http://localhost:5000` (porta padrão do `WebApplication.CreateBuilder`).
- Swagger: `http://localhost:5000/swagger` (em `Development`).
- Health: `http://localhost:5000/health` e `http://localhost:5000/health/ready`.

## Fluxo de teste ponta a ponta

```bash
# 1. Criar tenant
TENANT=$(curl -s -X POST http://localhost:8080/api/v1/tenants \
  -H "Content-Type: application/json" \
  -d '{"name":"Job Search","slug":"job-search"}')
TENANT_ID=$(echo $TENANT | jq -r .id)

# 2. Criar aplicação
APP=$(curl -s -X POST http://localhost:8080/api/v1/applications \
  -H "Content-Type: application/json" \
  -d "{\"tenantId\":\"$TENANT_ID\",\"name\":\"Quero Vagas Tech\",\"webhookUrl\":\"http://localhost:9999/payment-callback\"}")
APP_ID=$(echo $APP | jq -r .id)
API_KEY=$(echo $APP | jq -r .apiKey)

# 3. Criar checkout
curl -X POST http://localhost:8080/api/v1/checkouts \
  -H "Authorization: Bearer $API_KEY" \
  -H "X-Tenant-Id: $TENANT_ID" \
  -H "X-Application-Id: $APP_ID" \
  -H "Idempotency-Key: $(uuidgen)" \
  -H "Content-Type: application/json" \
  -d '{
    "externalReference": "order-1",
    "customer": { "name": "Cliente", "email": "cliente@email.com" },
    "items": [ { "id": "premium-monthly", "name": "Premium", "quantity": 1, "unitAmount": 2990 } ],
    "currency": "BRL",
    "successUrl": "https://example.com/success",
    "cancelUrl": "https://example.com/cancel"
  }'

# 4. Simular webhook externo
curl -X POST http://localhost:8080/api/v1/webhooks/Fake \
  -H "Content-Type: application/json" \
  -H "X-Provider-Event-Id: evt-1" \
  -H "X-Provider-Event-Type: payment.approved" \
  -d "{\"id\":\"$PAYMENT_ID\",\"status\":\"approved\"}"

# 5. Verificar logs do worker
docker compose logs -f payment-gateway-worker
```

## Validação de mudanças

```bash
dotnet restore
dotnet build
dotnet test
docker compose config
```

## Limpeza

```bash
docker compose down -v
dotnet clean
```
