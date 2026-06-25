# OpenCode Harness

Guia operacional para usar OpenCode no repositorio `payment-hub`.

## Inicio rapido

1. Inicie na raiz do repositorio.
2. Rode `scripts/agent-init.sh`.
3. Leia `AGENTS.md` e a spec relacionada.
4. Escolha agente e skill conforme o tipo de tarefa.
5. Registre `agent-progress.md` para tarefas multi-step.

Depois de alterar `.opencode/opencode.json`, agentes, skills ou plugins, reinicie o OpenCode. A configuracao nao e recarregada durante a sessao ativa.

## Fonte de verdade

- `AGENTS.md`: regras globais e indice curto.
- `.opencode/opencode.json`: configuracao estrutural e permissoes globais.
- `.opencode/agents/*.md`: comportamento, metadados e permissoes por agente.
- `.opencode/skills/*/SKILL.md`: fluxos sob demanda.
- `docs/harness/`: operacao, validacao, arquitetura e manutencao do harness.

Evite duplicar instrucoes longas entre JSON, agentes, skills e docs. Se um comportamento pertence a um agente, edite o Markdown do agente.

## Agentes

| Agente | Modo | Uso |
| --- | --- | --- |
| `planner` | primary | Planejar contrato de slice antes de editar |
| `implementer` | primary | Executar mudanca pequena com validacao |
| `architect-reviewer` | subagent | Revisar arquitetura, ADRs e limites de camada |
| `qa-reviewer` | subagent | Revisar testes, regressao e evidencia |
| `security-reviewer` | subagent | Revisar seguranca, secrets, auth e webhooks |

O agente padrao e `planner`. Use `implementer` quando o plano estiver claro. `planner` e `implementer` podem acionar apenas `architect-reviewer`, `qa-reviewer` e `security-reviewer`; reviewers nao acionam outros subagents por padrao.

## Skills

Skills locais ficam em `.opencode/skills/*/SKILL.md` e sao indexadas em `docs/harness/skill-index.md`. O `skills.paths` no JSON explicita esse caminho local.

Use skills sob demanda:

- `payment-slice` para fluxo completo de slice.
- `dotnet-validation` para comandos locais.
- `architecture-fitness` para limites de camadas.
- `security-review` para seguranca.
- `docs-maintenance` para docs, specs, ADRs e progresso.

## Fluxo recomendado para feature

1. Use `planner` com `payment-slice`.
2. Leia spec e ADRs aplicaveis.
3. Registre plano em `agent-progress.md`.
4. Use `implementer` para um slice pequeno.
5. Acione reviewers conforme risco.
6. Rode validacoes e registre evidencias.

## Fluxo recomendado para bugfix

1. Reproduza ou delimite a falha.
2. Leia spec relacionada.
3. Planeje a menor correcao segura.
4. Implemente com teste de regressao quando possivel.
5. Rode `dotnet test` ou filtro relevante, depois validacao ampla proporcional.
6. Registre causa raiz, validacao e risco residual.

## Fluxo recomendado para auditoria

1. Use `planner` para escopo da auditoria.
2. Use `architect-reviewer`, `qa-reviewer` ou `security-reviewer` conforme foco.
3. Priorize findings por severidade e evidencia.
4. Nao corrija tudo no mesmo passo; registre follow-ups em `feature_list.md`.
5. Rode scripts de checks quando houver mudanca local.

## Comandos de validacao

```bash
scripts/agent-init.sh
scripts/agent-docs-check.sh
scripts/agent-architecture-check.sh
scripts/agent-smoke.sh
scripts/agent-verify.sh
dotnet restore
dotnet build
dotnet test
```

Use `docker compose config` quando Docker mudar. Use `dotnet format --verify-no-changes` quando formatacao for parte do risco.

## Regras de seguranca

- Nao commitar `.env` real, secrets, tokens, API Keys reais ou credenciais de provider.
- Nao armazenar numero de cartao nem CVV.
- Reviewers nao editam arquivos por padrao.
- `implementer` pede aprovacao para edicoes por padrao.
- `git push`, `git reset`, `git clean`, remocoes amplas, migrations e secrets exigem aprovacao.
- Mudancas em auth, banco, CI, Docker ou scripts destrutivos exigem evidencia e revisao humana.

## Falhas comuns

- Config invalida: valide contra `https://opencode.ai/config.json` e remova chaves desconhecidas.
- Skill nao aparece: confirme `SKILL.md`, `name`, `description` e caminho `.opencode/skills/<name>/SKILL.md`.
- Agente nao aparece: confirme arquivo em `.opencode/agents/<name>.md`, frontmatter valido e `default_agent` quando for o agente padrao.
- Validacao falhou: registre comando, erro e menor correcao; nao declare done.
