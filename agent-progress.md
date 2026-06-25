# Agent Progress

Use este arquivo para tarefas com mais de um passo. Mantenha entradas curtas e verificaveis.

## Entrada atual

- Data: 2026-06-25
- Agente/superficie: OpenCode
- Objetivo: Corrigir ressalvas leves do harness OpenCode, removendo duplicidade entre JSON e agentes Markdown, endurecendo permissoes e melhorando validacoes/documentacao sem alterar produto.
- Fora de escopo: Domínio, API, Worker, providers, contratos de pagamento, regras de negocio, secrets, migrations, dependencias pesadas e CI/CD.
- Specs/ADRs/docs lidas: `AGENTS.md`, `.opencode/opencode.json`, `.opencode/README.md`, `.opencode/agents/*`, `docs/harness/opencode.md`, `docs/harness/agent-operating-model.md`, `docs/harness/skill-index.md`, `docs/harness/README.md`, `docs/specs/README.md`, `docs/adr/000-adr-index.md`, `scripts/agent-docs-check.sh`, `scripts/agent-verify.sh`.
- Discovery: `opencode.json` ainda duplicava metadados/permissoes dos agentes que ja existem em `.opencode/agents/*.md`; `implementer` tinha `edit: '*': allow`; reviewers tinham `edit: deny`, mas nao havia bloqueio explicito de chamada de subagents; scripts ainda nao detectavam essas ambiguidades.
- Decisoes: `.opencode/agents/*.md` sera a fonte de verdade de comportamento, metadados e permissoes por agente; `.opencode/opencode.json` ficara estrutural e global. `implementer` usara `edit: '*': ask`; planner/implementer poderao chamar somente reviewers; reviewers terao `task: deny` e `edit: deny`.
- Plano: Reduzir JSON; ajustar frontmatter dos agentes; atualizar README/docs de harness com fonte de verdade; fortalecer `agent-docs-check.sh`; rodar validacoes obrigatorias e registrar evidencias.
- Arquivos alterados: `.opencode/opencode.json`, `.opencode/agents/planner.md`, `.opencode/agents/implementer.md`, `.opencode/agents/architect-reviewer.md`, `.opencode/agents/qa-reviewer.md`, `.opencode/agents/security-reviewer.md`, `.opencode/README.md`, `docs/harness/opencode.md`, `docs/harness/agent-operating-model.md`, `docs/harness/skill-index.md`, `docs/harness/README.md`, `docs/harness/learnings.md`, `scripts/agent-docs-check.sh`, `agent-progress.md`. `AGENTS.md` revisado e mantido sem mudanca para preservar indice curto.
- Validacoes planejadas: `scripts/agent-init.sh`, `scripts/agent-docs-check.sh`, `scripts/agent-architecture-check.sh`, `scripts/agent-smoke.sh`, `scripts/agent-verify.sh`, `dotnet restore`, `dotnet build`, `dotnet test`, `git diff --check`, `opencode debug config >/dev/null` se disponivel.
- Validacoes executadas: `scripts/agent-init.sh` passou; `scripts/agent-docs-check.sh` passou; `scripts/agent-architecture-check.sh` passou; `scripts/agent-smoke.sh` passou com restore/build e `docker compose config`; `scripts/agent-verify.sh` passou; `/usr/bin/dotnet restore` passou; `/usr/bin/dotnet build` passou com 0 erros/0 warnings; `/usr/bin/dotnet test` passou com 106 testes unitarios e projeto de integracao sem testes descobertos; `git diff --check` passou; `opencode debug config >/dev/null` passou.
- Evidencias: `opencode.json` nao contem `agent`, `agents`, `notes` ou `prompt`; `.opencode/agents/*.md` contem metadados/permissoes por agente; `implementer` usa `edit: '*': ask`; reviewers usam `edit: deny` e `task: deny`; `planner`/`implementer` podem acionar apenas os tres reviewers; `agent-docs-check.sh` valida essas regras.
- Riscos residuais: OpenCode precisa ser reiniciado para carregar config/agentes alterados; testes de integracao continuam sem testes descobertos; permissao `task` foi validada pelo schema/CLI, mas a granularidade exata de matching depende da implementacao do OpenCode; scripts continuam heurísticos e nao substituem revisao humana.
- Aprendizados para `docs/harness/learnings.md`: Atualizado aprendizado de 2026-06-24 para fixar `.opencode/agents/*.md` como fonte de verdade e impedir duplicacao no JSON.

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
