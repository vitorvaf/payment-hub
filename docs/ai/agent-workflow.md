# Agent Workflow

Este fluxo complementa `docs/harness/workflow.md` para uso diario com Copilot, Codex e agentes locais.

## Feature

1. Revisar issue, spec relacionada e ADRs.
2. Rodar o prompt `.github/prompts/plan-feature.prompt.md`.
3. Gerar plano tecnico com escopo, fora de escopo, riscos e validacoes.
4. Quebrar em slices pequenos.
5. Implementar um slice por vez.
6. Criar ou ajustar testes proporcionais ao risco.
7. Rodar validacoes aplicaveis.
8. Registrar evidencias.
9. Revisar com `.github/prompts/review-pr.prompt.md` ou agente reviewer.
10. Preparar PR ou resumo final.

## Bug

1. Reproduzir ou descrever a falha observavel.
2. Formular hipoteses.
3. Coletar evidencias em logs, testes ou codigo.
4. Identificar causa raiz.
5. Aplicar a menor correcao segura.
6. Criar teste de regressao quando possivel.
7. Validar.
8. Documentar evidencia e risco residual.

## Mudanca cross-repo

1. Mapear repositorios e contratos impactados.
2. Identificar ownership, APIs, eventos, schemas e segredos envolvidos.
3. Criar plano por repositorio.
4. Definir ordem de implementacao e rollout.
5. Definir testes por camada.
6. Executar em branches separadas.
7. Integrar com gates e revisao humana.

## Finalizacao

- Liste arquivos alterados.
- Liste comandos executados e resultados.
- Informe o que nao foi possivel validar.
- Atualize `docs/harness/learnings.md` quando houver aprendizado reutilizavel.
