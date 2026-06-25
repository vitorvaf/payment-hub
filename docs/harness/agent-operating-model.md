# Agent Operating Model

Este documento define como agentes trabalham no Payment Hub sem transformar `AGENTS.md` em manual. Use junto com `docs/harness/workflow.md`, `validation.md`, `security.md` e as specs.

## Inicio de sessao

1. Rode `scripts/agent-init.sh` para ver arquivos de contexto, comandos conhecidos e estado do repositorio.
2. Leia `AGENTS.md` e os docs basicos de harness.
3. Leia `docs/specs/README.md` e a spec relacionada ao slice.
4. Leia ADRs quando tocar arquitetura, banco, seguranca, checkout hospedado, API Key, Inbox/Outbox ou providers.
5. Inspecione `git status --short` antes de editar.

## Escolha de agente

| Situacao | Agente |
| --- | --- |
| Tarefa ambigua ou multi-step | `planner` |
| Implementacao com plano claro | `implementer` |
| Revisao de camadas, ADRs ou decisoes | `architect-reviewer` |
| Revisao de testes e evidencias | `qa-reviewer` |
| Revisao de secrets, auth, tenant e webhooks | `security-reviewer` |

Reviewers sao avaliadores independentes. Eles nao devem editar arquivos por padrao e nao substituem revisao humana em mudancas sensiveis.

## Escolha de skill

| Situacao | Skill |
| --- | --- |
| Feature, bugfix ou harness slice | `payment-slice` |
| Restore, build, test, format e scripts | `dotnet-validation` |
| Clean Architecture e dependencias | `architecture-fitness` |
| Multitenancy, API Key, HMAC e secrets | `security-review` |
| Specs, ADRs, docs e progresso | `docs-maintenance` |

Use skills sob demanda para reduzir contexto inicial. Nao carregue todas as skills por padrao.

## Fluxo para tarefa pequena

1. Entenda objetivo e fora de escopo.
2. Leia apenas a spec ou doc diretamente relacionado.
3. Faça a menor mudanca segura.
4. Rode validacao proporcional.
5. Registre evidencia curta se a tarefa tiver mais de um passo.

## Fluxo para tarefa media

1. Use `planner` ou a skill `payment-slice`.
2. Registre contrato em `agent-progress.md`.
3. Use `implementer` para um slice coeso.
4. Use reviewer independente quando houver risco de arquitetura, QA ou seguranca.
5. Rode scripts e comandos de validacao.
6. Atualize evidencias, riscos e proximos passos.

## Fluxo para tarefa grande

1. Divida em slices pequenos antes de editar.
2. Garanta spec completa ou registre gap antes da implementacao.
3. Identifique ADRs necessarios antes de mudar arquitetura.
4. Execute um slice por vez com review entre slices.
5. Nao misture refatoracao ampla, produto e harness no mesmo slice.
6. Registre pendencias em `feature_list.md` quando nao forem resolvidas agora.

## Registro de progresso

Use `agent-progress.md` para tarefas multi-step. A entrada deve conter objetivo, fora de escopo, docs lidos, plano, arquivos, validacoes planejadas, validacoes executadas, evidencias e riscos.

Ao concluir, mantenha a entrada atual verificavel. Se fizer sentido preservar rastreabilidade longa, mova um resumo para o historico.

## Validacao

Escolha validacoes pelo escopo real:

- Harness/docs/scripts: `scripts/agent-docs-check.sh` e `scripts/agent-verify.sh`.
- Arquitetura: `scripts/agent-architecture-check.sh`.
- .NET: `dotnet restore`, `dotnet build`, `dotnet test`.
- Docker: `docker compose config`.
- Smoke local seguro: `scripts/agent-smoke.sh`.

Se um comando nao rodar, registre motivo e risco residual.

## Falhas

1. Pare de declarar sucesso.
2. Capture o comando, erro e contexto minimo.
3. Corrija a menor causa provavel.
4. Rode novamente o comando afetado.
5. Se for bloqueio externo, registre como risco residual e proximo passo.

## ADRs

Crie ou atualize ADR apenas para decisao duradoura de arquitetura. Use `docs/harness/adr-template.md` e atualize `docs/adr/000-adr-index.md`.

ADRs aceitas sao historicas. Para mudar uma decisao aceita, crie novo ADR que substitui o anterior.

## Evitar drift

- `AGENTS.md` fica como indice curto.
- Contratos de produto ficam em `docs/specs/`.
- Decisoes duradouras ficam em `docs/adr/`.
- Processo operacional fica em `docs/harness/`.
- Tooling especifico fica em `.opencode/`, `.github/`, `.codex/` ou pasta equivalente.
- Aprendizados reutilizaveis ficam em `docs/harness/learnings.md`.
