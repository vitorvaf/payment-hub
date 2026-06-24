# Payment Gateway MVP

Orquestrador de pagamentos multitenant escrito em .NET com Clean Architecture, PostgreSQL, padrão Inbox/Outbox, idempotência e workers assíncronos.

## Quick start

```bash
docker compose up -d
docker compose logs -f payment-gateway-api
```

Swagger em `http://localhost:8080/swagger` quando `ASPNETCORE_ENVIRONMENT=Development`.

## Documentação

- [Specs formais](docs/specs/README.md)
- [ADRs](docs/adr/)
- [Harness para agentes](AGENTS.md)
- [Workflow IA](docs/ai/agent-workflow.md)
- [Checklist de validação](docs/ai/validation-checklist.md)
- [Visão geral da arquitetura](docs/architecture/overview.md)
- [Decisões do MVP](docs/architecture/mvp-decisions.md)
- [API: criação de checkout](docs/api/create-checkout.md)
- [API: webhooks](docs/api/webhooks.md)
- [Schema do banco](docs/database/schema.md)
- [Setup local](docs/development/local-setup.md)

## Spec Driven Development

As especificações formais ficam em `docs/specs/`.
Antes de alterar qualquer fluxo de pagamento, webhooks, workers, segurança ou banco, leia a spec relacionada.
Decisões arquiteturais aceitas ficam em `docs/adr/`.

## Stack

- .NET 10
- PostgreSQL 16
- Entity Framework Core 10 + Npgsql
- Serilog (logs estruturados)
- Swashbuckle (Swagger/OpenAPI)
- Health Checks para API e Postgres
- Docker Compose

## Estrutura da solução

```
PaymentHub.slnx
src/
  PaymentHub.Domain/                 entidades, enums, value objects, regras
  PaymentHub.Application/            casos de uso, DTOs, interfaces, orquestração
  PaymentHub.Infrastructure.Postgres/ DbContext, migrations, repositórios, Inbox/Outbox
  PaymentHub.Infrastructure.Providers/ adapters de provedores (Fake, AbacatePay, Stripe, MercadoPago)
  PaymentHub.Api/                    controllers, middleware, Swagger, health
  PaymentHub.Worker/                 BackgroundServices de Inbox/Outbox
tests/
  PaymentHub.UnitTests/              testes unitários de domínio, application, infraestrutura
  PaymentHub.IntegrationTests/       testes de integração (placeholder estrutural)
```

## Comandos

```bash
dotnet restore
dotnet build
dotnet test
dotnet ef database update --project src/PaymentHub.Infrastructure.Postgres --startup-project src/PaymentHub.Api
docker compose up -d
docker compose down
```

> As migrations EF Core vivem em `src/PaymentHub.Infrastructure.Postgres/Migrations/`.

## Fluxo para agentes

```bash
scripts/agent-init.sh
scripts/agent-verify.sh
dotnet restore
dotnet build
dotnet test
```

- Use `AGENTS.md` como índice de sessão.
- Use `.github/prompts/` para tarefas recorrentes.
- Use `.github/agents/` e `.opencode/agents/` como personas alinhadas ao mesmo harness.
- Registre plano, evidências e riscos em `agent-progress.md` quando a tarefa tiver mais de um passo.
