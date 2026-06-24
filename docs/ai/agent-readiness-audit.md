# Agent Readiness Audit

Data: 2026-06-23

## Mapa do projeto

- Nome: Payment Gateway MVP / Payment Hub.
- Stack principal: .NET 10, ASP.NET Core, EF Core 10, PostgreSQL 16, Docker Compose, Serilog, Swagger/OpenAPI e xUnit.
- Arquitetura: Clean Architecture com `Domain`, `Application`, `Infrastructure.Postgres`, `Infrastructure.Providers`, `Api` e `Worker`.
- Modulos principais:
  - `src/PaymentHub.Domain`: entidades, enums, value objects e politicas de dominio.
  - `src/PaymentHub.Application`: casos de uso, DTOs, interfaces, bootstrap e orquestracao.
  - `src/PaymentHub.Infrastructure.Postgres`: DbContext, migrations, repositorios, Inbox/Outbox e criptografia.
  - `src/PaymentHub.Infrastructure.Providers`: adapters Fake, AbacatePay, Stripe e MercadoPago.
  - `src/PaymentHub.Api`: controllers, middleware de API Key, Swagger e health.
  - `src/PaymentHub.Worker`: processamento de Inbox/Outbox e retries.
  - `tests/PaymentHub.UnitTests`: testes unitarios existentes.
  - `tests/PaymentHub.IntegrationTests`: projeto estrutural de integracao.
- Como rodar localmente: `docker compose up -d`; Swagger em `http://localhost:8080/swagger` em Development.
- Como testar: `dotnet restore`, `dotnet build`, `dotnet test`.
- Como validar compose: `docker compose config`.
- Como validar antes de PR: executar validacoes proporcionais ao slice e registrar evidencias no resumo ou em `agent-progress.md`.

## Diagnostico

| Eixo | Status | Evidencia |
|------|--------|-----------|
| Clareza da arquitetura | Bom | README, specs, ADRs e estrutura `src/` por camada. |
| Legibilidade da estrutura | Bom | Projetos e pastas nomeados por responsabilidade. |
| Instrucoes para agentes | Bom | `AGENTS.md`, `.github/copilot-instructions.md`, `.github/instructions/`, `.opencode/`. |
| Comandos de verificacao | Bom | `docs/harness/validation.md` e README listam comandos reais. |
| Testes unitarios | Bom | `tests/PaymentHub.UnitTests` com cobertura de dominio, application, API e providers. |
| Testes de integracao | Parcial | Projeto existe, mas sem evidencia de cenarios completos. |
| Testes E2E | Ausente | Nao ha suite E2E dedicada. |
| CI/CD | Parcial | Workflow `.github/workflows/ci.yml` valida harness, restore, build e test em push para `main` e pull requests. |
| Scripts de setup | Parcial | Docker Compose e docs existem; scripts de agente foram adicionados neste slice. |
| Inicio de sessao por agente | Bom | Harness e docs IA indicam leitura inicial e lifecycle. |
| Risco de fora de escopo | Parcial | Specs e harness reduzem risco; agentes ainda precisam registrar escopo. |
| Risco de "done" sem evidencia | Parcial | `definition-of-done` e validacao existem; scripts/checklists reforcam o gate. |

## Lacunas

- Faltavam prompts versionados em `.github/prompts/` para tarefas recorrentes.
- Faltavam custom agents em `.github/agents/` para superfícies que suportam esse formato.
- Faltavam skills versionadas em `.github/skills/` para rotinas repetiveis.
- Faltavam docs dedicados em `docs/ai/` para surface map, workflow, model routing, governanca e checklist.
- Faltavam arquivos simples de estado operacional (`feature_list.md`, `agent-progress.md`).
- Faltava script mecanico de init/verify para agentes.
- E2E continua pendente fora deste slice.

## Proposta implementada

- Manter `AGENTS.md` como indice operacional curto.
- Manter `.github/copilot-instructions.md` curto e usar `.github/instructions/` por path.
- Adicionar prompts, agentes e skills apenas para processos recorrentes de planejamento, implementacao, revisao, testes, debugging, ADR e evidencias.
- Adicionar `docs/ai/` como camada de operacao e governanca para Copilot/Codex.
- Adicionar scripts `scripts/agent-init.sh` e `scripts/agent-verify.sh` para feedback rapido.

## Riscos residuais

- CI cobre validacao basica, mas ainda nao inclui E2E, publicacao de artefatos ou validacoes com banco real.
- Sem E2E, fluxos reais API + banco + worker dependem de testes manuais ou integracao futura.
- Scripts de agente validam estrutura e comandos basicos, mas nao substituem testes de produto.
