# Architecture Fitness

Checklist para verificar se uma mudanca respeita Clean Architecture, specs e ADRs do Payment Hub.

## Objetivo

Encontrar problemas de dependencia, acoplamento, responsabilidade de camada e decisoes arquiteturais antes que virem regressao.

## Camadas

| Camada | Responsabilidade | Nao deve depender de |
| --- | --- | --- |
| Domain | Entidades, value objects, enums e invariantes | Application, Infrastructure, API, Worker, EF Core, ASP.NET |
| Application | Use cases, DTOs, interfaces e validacao de aplicacao | API, Worker, Infrastructure concreta |
| Infrastructure.Postgres | EF Core, repositorios, migrations, Inbox/Outbox persistido | API controllers |
| Infrastructure.Providers | Adapters de provider, Fake provider, criptografia de credenciais | API controllers |
| API | Controllers, middleware, auth, Swagger, validacao HTTP | Regras de dominio duplicadas |
| Worker | Background services, retries, processamento Inbox/Outbox | Controllers ou contexto HTTP |

## Checks obrigatorios

1. Domain nao referencia EF Core, ASP.NET Core, logging ou provider externo.
2. Controllers chamam Application e nao implementam regra de dominio.
3. Endpoints autenticados derivam tenant/application de `ITenantContext`.
4. Checkout hospedado permanece o caminho do MVP.
5. Provider explicito nao faz fallback silencioso.
6. Webhooks entram por Inbox antes do processamento.
7. Eventos de saida passam por Outbox.
8. Banco e migrations respeitam specs e ADRs.
9. Novas decisoes arquiteturais viram ADR proposta.

## Como validar

1. Leia specs relacionadas em `docs/specs/README.md`.
2. Leia ADRs relacionadas em `docs/adr/000-adr-index.md`.
3. Revise project references nos `.csproj`.
4. Busque imports proibidos em cada camada.
5. Rode `scripts/agent-architecture-check.sh`.
6. Registre findings em `agent-progress.md` ou no review.

## Sinais de risco

- Logica financeira ou transicao de status em controller.
- Infraestrutura concreta vazando para Application sem interface.
- Worker dependendo de API ou de `HttpContext`.
- DTO HTTP entrando no dominio.
- Migration sem spec/ADR quando altera contrato de banco.
- Abstracao generica criada sem segundo caso concreto.

## Evidencia esperada

- Specs e ADRs consultadas.
- Resultado do script de arquitetura.
- Findings com arquivo e linha quando possivel.
- Riscos residuais e follow-ups.
