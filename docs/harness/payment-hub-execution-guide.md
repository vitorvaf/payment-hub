# Payment Hub — Guia de Execucao do Agente

Este documento descreve como um agente deve trabalhar no repositorio `payment-hub`. Ele e o ponto de entrada obrigatorio antes de qualquer execucao de spec ou implementacao.

---

## Visao geral do fluxo

Toda execucao deve seguir este ciclo, sem pular etapas:

```
1. Discovery
2. Spec
3. ADR (quando necessario)
4. Plan
5. Slice
6. Implementation
7. Validation
8. Evidence
9. Final Report
```

Cada etapa esta detalhada abaixo.

---

## Etapa 1 — Discovery

Antes de qualquer coisa, o agente deve entender o estado atual do repositorio.

**Leituras obrigatorias:**

- `docs/harness/project-context.md` — visao geral do projeto e decisoes fundamentais.
- `docs/harness/workflow.md` — ciclo de trabalho esperado.
- `docs/harness/security.md` — regras de seguranca que nao podem ser violadas.
- `docs/harness/learnings.md` — licoes aprendidas de execucoes anteriores.
- `docs/roadmap/002-phase-status-board.md` — estado atual de todas as fases.
- `docs/audits/payment-hub-current-state-audit-2026-06-17.md` — estado observavel do repositorio.
- Spec relacionada a fase ou slice alvo.
- ADRs relevantes em `docs/adr/`.

**Inspecoes de repositorio:**

- `git status --short` — verificar estado do working tree.
- Navegar em `src/` para entender estrutura atual.
- Verificar testes existentes relacionados ao escopo.

**Saida esperada:** o agente deve ser capaz de descrever com suas proprias palavras o objetivo, o escopo, o fora de escopo e os riscos antes de continuar.

---

## Etapa 2 — Spec

Toda implementacao precisa de uma spec existente e revisada antes de comecar.

**Verificar:**

- Spec existe em `docs/specs/`?
- Spec tem: objetivo, escopo, fora de escopo, entidades afetadas, APIs afetadas, eventos afetados, criterios de aceite, plano de testes e riscos?
- Se a spec nao existir, cria-la primeiro antes de qualquer codigo.
- Se a spec existir mas estiver incompleta, completar as secoes faltantes.

**Usar o template:** `docs/harness/phase-template.md` para phases; `docs/harness/slice-template.md` para slices.

**Importante:** nenhuma feature pode entrar em implementacao sem spec com criterios de aceite verificaveis.

---

## Etapa 3 — ADR (quando necessario)

Se a fase ou slice exige uma decisao arquitetural nova (autenticacao, protecao de dados, integracao com sistema externo, mudanca de contrato), um ADR deve ser redigido e decidido antes do codigo.

**Verificar:**

- A decisao ja foi tomada em algum ADR existente (`docs/adr/000-adr-index.md`)?
- Ha alguma ADR proposta que deve ser decidida antes deste slice (ADR-0006 a ADR-0009)?
- Se nenhum ADR cobre a decisao, redigir um usando `docs/harness/adr-template.md`.

**Regra:** ADR nao pode ser escrita `ACCEPTED` pelo proprio agente que implementa. Registrar como `PROPOSED` e indicar que precisa de decisao humana antes de prosseguir.

---

## Etapa 4 — Plan

Antes de alterar muitos arquivos, criar um plano curto.

**O plano deve conter:**

- Objetivo do slice em uma frase.
- Lista de arquivos que provavelmente serao alterados.
- Lista de novos arquivos que serao criados.
- Criterios de aceite que serao verificados.
- Validacoes planejadas (comandos).
- Riscos identificados.

**Regra:** nao alterar mais de 5 arquivos de codigo por slice sem plano explicito. Slices grandes devem ser divididos.

---

## Etapa 5 — Slice

Implementar uma mudanca pequena e coesa por vez.

**Regras de slice:**

- Um slice = uma responsabilidade. Nao misturar seguranca com observabilidade no mesmo slice.
- Respeitar Clean Architecture: dominio nao importa infraestrutura; aplicacao nao importa controllers.
- Nao implementar features fora do escopo do slice sem registrar como novo slice pendente.
- Nao remover arquivos existentes sem justificar e registrar no relatorio.
- Nao alterar contratos HTTP sem criar secao de breaking changes na spec.
- Nao expor secrets, tokens ou credenciais em codigo, testes, logs ou documentacao.
- Nao usar credenciais reais de providers em testes. Usar Fake, mocks ou sandbox com dados de teste.

---

## Etapa 6 — Implementation

Durante a implementacao:

- Criar ou atualizar testes unitarios antes ou junto com o codigo (nao depois).
- Para slices que afetam banco, middleware ou workers: criar ou atualizar testes de integracao.
- Garantir que `dotnet build` passa sem warnings novos.
- Garantir que `dotnet test` passa com todos os testes existentes mais os novos.

---

## Etapa 7 — Validation

Executar os comandos de validacao aplicaveis ao escopo do slice.

**Comandos obrigatorios:**

```bash
git status --short
dotnet restore PaymentHub.slnx
dotnet build PaymentHub.slnx
dotnet test PaymentHub.slnx
```

**Comandos adicionais por tipo de slice:**

- Slice de banco: verificar migration com `dotnet ef migrations list`.
- Slice de API: verificar contrato com `dotnet run` e chamada manual.
- Slice de worker: verificar logs de startup do worker host.
- Slice de seguranca: verificar que testes de autorizacao passam com e sem credentials.

**Registrar na `docs/harness/validation-matrix.md`:**

- Fase, slice, tipo de validacao, comando, resultado esperado, resultado real, status e data.

---

## Etapa 8 — Evidence

Ao final de cada slice, registrar evidencias.

**O que registrar:**

- Lista de arquivos criados.
- Lista de arquivos alterados com resumo da mudanca.
- Comandos executados e resultados.
- Testes adicionados (nome e contagem).
- Riscos residuais.
- Gaps que permaneceram em aberto (para o proximo slice).
- Decisoes tomadas durante a implementacao que nao estavam na spec (registrar como aprendizado ou ADR).

---

## Etapa 9 — Final Report

Ao final de cada phase completa (todas as slices concluidas), gerar um relatorio final.

**Arquivo:** `docs/audits/phase-X-report-YYYY-MM-DD.md`

**Conteudo obrigatorio:**

- Resumo da phase.
- Arquivos criados.
- Arquivos alterados.
- Testes adicionados.
- Criterios de aceite verificados (passou / nao passou).
- Gaps corrigidos.
- Gaps remanescentes.
- Decisoes pendentes.
- ADRs recomendadas.
- Validacoes executadas.
- Falhas de validacao.
- Observacoes.
- Proximos slices recomendados.

Apos o relatorio, atualizar:

- `docs/roadmap/002-phase-status-board.md` — status da phase.
- `docs/roadmap/001-development-timeline.md` — se alguma fase mudou de status.
- `docs/audits/roadmap-adherence-matrix-2026-06-17.md` — linhas afetadas.
- `docs/harness/learnings.md` — novos aprendizados relevantes.

---

## Regras de seguranca (nao negociaveis)

Ver `docs/harness/security.md` para a lista completa. Resumo:

- Nunca commitar secrets, tokens, API keys ou credenciais reais.
- Nunca logar dados sensiveis (CPF, cartao, CVV, token).
- Nunca armazenar dado de cartao no dominio.
- Nunca expor credenciais de provider em texto claro.
- Sempre validar entrada nos endpoints publicos (tenantId, applicationId, valores monetarios).
- Sempre usar `IDataProtectionProvider` ou equivalente para segredos persistidos.

---

## Regras de escopo (nao negociaveis)

- Nao implementar feature sem spec aprovada.
- Nao alterar migrations em producao sem ADR e plano de rollback.
- Nao alterar contratos HTTP sem secao de breaking changes na spec.
- Nao fazer refactor amplo junto com feature ou bugfix.
- Nao quebrar testes existentes sem justificar e resolver.

---

## Definition of Ready e Definition of Done

Antes de comecar qualquer slice:

- Verificar `docs/harness/definition-of-ready.md` — todos os criterios devem estar atendidos.

Antes de considerar qualquer slice concluido:

- Verificar `docs/harness/definition-of-done.md` — todos os criterios devem estar atendidos.

---

## Arquivos de referencia do harness

| Arquivo | Finalidade |
| ------- | ---------- |
| `project-context.md` | Contexto do produto e decisoes fundamentais |
| `workflow.md` | Ciclo de trabalho resumido |
| `security.md` | Regras de seguranca obrigatorias |
| `validation.md` | Comandos de validacao por contexto |
| `learnings.md` | Licoes de execucoes anteriores |
| `definition-of-ready.md` | Checklist de entrada para implementacao |
| `definition-of-done.md` | Checklist de saida de implementacao |
| `phase-template.md` | Template para documentar phases |
| `slice-template.md` | Template para documentar slices |
| `adr-template.md` | Template para redigir ADRs |
| `validation-matrix.md` | Rastreamento de validacoes por phase/slice |
| `agent-roles.md` | Papeis e responsabilidades dos agentes |
| `prompt-catalog.md` | Catalogo de prompts reutilizaveis |
| `runbook-local-dev.md` | Runbook para desenvolvimento local |
