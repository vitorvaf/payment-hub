# Harness Engineering

Este diretório define o contrato operacional para desenvolvimento assistido por agentes no Payment Gateway MVP.

O objetivo do harness é padronizar como agentes entendem contexto, planejam tarefas, implementam pequenos slices, validam mudanças, registram evidências e aprendem com o projeto.

## Leitura inicial obrigatória

1. `AGENTS.md`
2. `docs/harness/project-context.md`
3. `docs/harness/workflow.md`
4. `docs/harness/validation.md`
5. `docs/harness/security.md`
6. `docs/harness/learnings.md`

## Princípio

Nenhum agente deve começar implementando uma feature de pagamento sem antes passar por descoberta, plano, validação e evidências.

## Guias complementares

- `agent-operating-model.md`: escolha de agente/skill, fluxos por tamanho de tarefa, progresso, falhas e ADRs.
- `opencode.md`: uso especifico do OpenCode neste repositorio.
- `skill-index.md`: indice das skills sob demanda.
- `architecture-fitness.md`: checklist de Clean Architecture e dependencias.

## Mapa de fontes

- `AGENTS.md`: regras globais e indice curto.
- `.opencode/agents/*.md`: comportamento e permissoes dos agentes OpenCode.
- `.opencode/skills/*/SKILL.md`: fluxos sob demanda.
- `docs/specs/`: contratos de produto.
- `docs/adr/`: decisoes arquiteturais.
