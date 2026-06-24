# Agent Progress

Use este arquivo para tarefas com mais de um passo. Mantenha entradas curtas e verificaveis.

## Entrada atual

- Data: 2026-06-23
- Agente/superficie: Codex local
- Objetivo: Configurar o repositorio para trabalhar melhor com GitHub Copilot, Codex e agentes de codigo, com auditoria, instrucoes, prompts, agentes, skills, docs de IA, estado e scripts de verificacao.
- Fora de escopo: Implementar features de produto, alterar dominio de pagamento, criar CI/CD real ou adicionar testes E2E.
- Specs/ADRs lidas: `docs/specs/README.md`, `docs/harness/project-context.md`, `docs/harness/workflow.md`, `docs/harness/validation.md`, `docs/harness/security.md`, `docs/harness/learnings.md`.
- Plano: Auditar estrutura existente; fortalecer `AGENTS.md` e Copilot; adicionar docs `docs/ai`; criar prompts, agentes e skills; adicionar scripts e estado; validar.
- Arquivos alterados: `AGENTS.md`, `.github/copilot-instructions.md`, `.github/instructions/*`, `.github/prompts/*`, `.github/agents/*`, `.github/skills/*`, `docs/ai/*`, `feature_list.md`, `agent-progress.md`, `scripts/agent-init.sh`, `scripts/agent-verify.sh`, `docs/harness/learnings.md`.
- Validacoes planejadas: `scripts/agent-verify.sh`, `dotnet restore`, `dotnet build`, `dotnet test`.
- Validacoes executadas: `scripts/agent-verify.sh` passou; `dotnet restore` passou; `dotnet build` passou com 0 warnings/0 errors; `dotnet test` passou com 106 testes unitarios e projeto de integracao sem testes descobertos.
- Evidencias: Auditoria em `docs/ai/agent-readiness-audit.md`; scripts executaveis criados; `docker compose config` validado pelo script.
- Riscos residuais: CI/CD e E2E continuam ausentes; testes de integracao ainda sao estruturais.
- Aprendizados para `docs/harness/learnings.md`: Harness agent-ready deve separar instrucao curta, docs progressivos, estado e verificacao mecanica.

## Historico

Registre entradas concluídas abaixo quando fizer sentido manter rastreabilidade no repositorio.

### 2026-06-23 - CI basico

- Objetivo: Criar CI basico com verificacao de harness, restore, build e test.
- Fora de escopo: E2E, publicacao de artefatos, deploy e validacoes com banco real.
- Arquivos alterados: `.github/workflows/ci.yml`, `feature_list.md`, `docs/ai/agent-readiness-audit.md`, `agent-progress.md`.
- Validacoes planejadas: `scripts/agent-verify.sh`, `dotnet restore`, `dotnet build`, `dotnet test`.
- Validacoes executadas: `scripts/agent-verify.sh` passou; `dotnet restore` passou; `dotnet build --no-restore` passou com 0 warnings/0 errors; `dotnet test --no-build` passou com 106 testes unitarios e projeto de integracao sem testes descobertos.
- Riscos residuais: CI ainda nao cobre E2E, publicacao de artefatos, deploy ou validacoes com banco real.
