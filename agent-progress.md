# Agent Progress

Use este arquivo para tarefas com mais de um passo. Mantenha entradas curtas e verificaveis.

## Entrada atual

- Data: 2026-06-24
- Agente/superficie: OpenCode
- Objetivo: Evoluir o harness OpenCode para agentes primarios/subagents, skills sob demanda, docs operacionais e validacoes rapidas sem alterar dominio de pagamento.
- Fora de escopo: Implementar feature funcional de negocio, alterar dominio/API/Worker, criar provider real, introduzir broker externo, alterar secrets, criar dependencias pesadas ou mudar CI/CD alem dos checks locais de harness.
- Specs/ADRs/docs lidas: `AGENTS.md`, `.opencode/README.md`, `.opencode/opencode.json`, `.opencode/agents/*`, `.github/copilot-instructions.md`, `.github/agents/*`, `.github/instructions/*`, `.github/prompts/*`, `docs/harness/*`, `docs/specs/README.md`, `docs/adr/000-adr-index.md`, `docs/ai/*`.
- Discovery: `.opencode/opencode.json` ainda so carregava instrucoes; agentes OpenCode existentes tinham nomes legados e nao tinham frontmatter de agente; nao havia `.opencode/skills`; scripts nao verificavam skills/agentes novos; docs de operacao OpenCode estavam incompletos; `.codex/` e `.agents/` existem vazios; `AGENTS.md` continua curto e adequado como indice.
- Plano: Atualizar `opencode.json` com `agent`, permissao e skills; criar/ajustar agentes OpenCode curtos; criar skills locais; adicionar docs complementares de harness; criar scripts seguros de arquitetura/docs/smoke; atualizar README OpenCode e checks; validar schema, scripts e build/test.
- Arquivos alterados/criados/removidos: `.opencode/opencode.json`, `.opencode/README.md`, `.opencode/agents/planner.md`, `.opencode/agents/implementer.md`, `.opencode/agents/architect-reviewer.md`, `.opencode/agents/qa-reviewer.md`, `.opencode/agents/security-reviewer.md`, `.opencode/skills/*/SKILL.md`, `docs/harness/agent-operating-model.md`, `docs/harness/architecture-fitness.md`, `docs/harness/skill-index.md`, `docs/harness/opencode.md`, `docs/harness/README.md`, `scripts/agent-architecture-check.sh`, `scripts/agent-docs-check.sh`, `scripts/agent-smoke.sh`, `scripts/agent-init.sh`, `scripts/agent-verify.sh`, `AGENTS.md`, `feature_list.md`, `docs/harness/learnings.md`, `agent-progress.md`; removidos agentes OpenCode legados `architect.md`, `backend-engineer.md` e `qa-engineer.md` para evitar duplicidade com os novos nomes.
- Validacoes planejadas: validacao manual do schema de `.opencode/opencode.json`, `scripts/agent-init.sh`, `scripts/agent-docs-check.sh`, `scripts/agent-architecture-check.sh`, `scripts/agent-smoke.sh`, `scripts/agent-verify.sh`, `dotnet restore`, `dotnet build`, `dotnet test`.
- Validacoes executadas: `scripts/agent-init.sh` passou; `scripts/agent-docs-check.sh` passou; `scripts/agent-architecture-check.sh` passou; `scripts/agent-verify.sh` passou; `scripts/agent-smoke.sh` passou com `dotnet restore` e `dotnet build --no-restore` em 0 erros/0 warnings; `dotnet restore` passou; `/usr/bin/dotnet build` passou com 0 erros/0 warnings; `/usr/bin/dotnet test` passou com 106 testes unitarios e projeto de integracao sem testes descobertos; `opencode debug config >/dev/null` passou; `docker compose config` passou via verify/smoke.
- Evidencias: `.opencode/opencode.json` validado pela CLI com chave `agent` e sem `agents`/`notes`; cinco agentes OpenCode registrados; cinco skills locais com frontmatter validado; docs complementares criados em `docs/harness/`; scripts novos executaveis e validados; `AGENTS.md` mantido como indice curto.
- Riscos residuais: OpenCode precisa ser reiniciado para carregar config/agentes/skills novos; testes de integracao continuam sem testes descobertos; checks de arquitetura/docs sao heurísticos e nao substituem revisao humana; config global do usuario ainda pode injetar plugins/MCP fora do controle do repositorio.
- Aprendizados para `docs/harness/learnings.md`: Registrado em `2026-06-24 - OpenCode harness deve separar config estrita, agentes curtos e skills sob demanda`.

## Historico

Registre entradas concluídas abaixo quando fizer sentido manter rastreabilidade no repositorio.

### 2026-06-23 - CI basico

- Objetivo: Criar CI basico com verificacao de harness, restore, build e test.
- Fora de escopo: E2E, publicacao de artefatos, deploy e validacoes com banco real.
- Arquivos alterados: `.github/workflows/ci.yml`, `feature_list.md`, `docs/ai/agent-readiness-audit.md`, `agent-progress.md`.
- Validacoes planejadas: `scripts/agent-verify.sh`, `dotnet restore`, `dotnet build`, `dotnet test`.
- Validacoes executadas: `scripts/agent-verify.sh` passou; `dotnet restore` passou; `dotnet build --no-restore` passou com 0 warnings/0 errors; `dotnet test --no-build` passou com 106 testes unitarios e projeto de integracao sem testes descobertos.
- Riscos residuais: CI ainda nao cobre E2E, publicacao de artefatos, deploy ou validacoes com banco real.

### 2026-06-24 - Configuracoes topico 3 em diante

- Objetivo: Aprimorar CI, alinhar OpenCode, documentar uso diario, adicionar gate simples de secrets e preparar roteiro de auditoria specs versus codigo.
- Fora de escopo: Implementar testes de integracao/E2E, alterar dominio de pagamento, adicionar provider real, deploy ou validacao com banco real no CI.
- Arquivos alterados: `.github/workflows/ci.yml`, `scripts/agent-verify.sh`, `.opencode/README.md`, `.opencode/agents/*`, `README.md`, `docs/ai/harness-engineering.md`, `docs/ai/validation-checklist.md`, `docs/ai/agent-readiness-audit.md`, `docs/ai/spec-adherence-next-audit.md`, `docs/audits/spec-adherence-refresh-2026-06-24.md`, `feature_list.md`, `agent-progress.md`.
- Validacoes planejadas: `scripts/agent-verify.sh`, `dotnet restore`, `dotnet build --no-restore`, `dotnet test --no-build --logger "trx;LogFilePrefix=test-results" --results-directory TestResults`.
- Validacoes executadas: `scripts/agent-verify.sh` passou; `dotnet restore` passou; `dotnet build --no-restore` passou com 0 warnings/0 errors; `dotnet test --no-build --logger "trx;LogFilePrefix=test-results" --results-directory TestResults` passou com 106 testes unitarios e gerou arquivos `.trx`.
- Riscos residuais: CI ainda nao executa testes de integracao reais, E2E, deploy ou validacao com banco real; o scan de secrets e simples e nao substitui ferramenta dedicada.
