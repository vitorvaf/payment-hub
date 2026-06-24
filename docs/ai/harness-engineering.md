# Harness Engineering

O harness deste repositorio combina instrucoes, estado, escopo, verificacao e lifecycle.

## Instrucoes

- `AGENTS.md`: indice operacional para qualquer agente.
- `.github/copilot-instructions.md`: regras globais curtas para Copilot.
- `.github/instructions/`: regras por path.
- `.opencode/`: agentes e configuracao OpenCode.
- `docs/specs/`: contratos formais.
- `docs/adr/`: decisoes arquiteturais aceitas.

## Estado

- `feature_list.md`: backlog leve de features, bugs e melhorias para agentes.
- `agent-progress.md`: registro local de plano, progresso e evidencias.
- `docs/harness/learnings.md`: aprendizados reutilizaveis.

## Escopo

- Uma feature ou bug por vez.
- Todo slice deve declarar fora de escopo.
- Mudancas de contrato exigem spec atualizada.
- Decisoes novas exigem ADR ou atualizacao de ADR.

## Verificacao

- `scripts/agent-init.sh`: mostra contexto inicial e comandos disponiveis.
- `scripts/agent-verify.sh`: valida estrutura de harness, docs principais e build/test quando aplicavel.
- `docs/ai/validation-checklist.md`: checklist proporcional ao risco.

## Lifecycle

1. Discovery.
2. Understanding.
3. Plan.
4. Implementation Slice.
5. Validation.
6. Evidence.
7. Harness Learning.

Nenhum agente deve declarar done sem evidencia ou justificativa explicita da validacao que nao rodou.
