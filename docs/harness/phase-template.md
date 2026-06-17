# Template de Phase

Copie este arquivo para `docs/specs/NNN-nome-da-phase.md` e preencha todos os campos.
Remova os comentarios em italico antes de commitar.

---

# [Nome da Phase]

## Status

_Status atual da phase. Use o enum:_
_`NOT_STARTED` | `DISCOVERY` | `SPEC_DRAFTED` | `SPEC_REVIEW_REQUIRED` | `READY_FOR_IMPLEMENTATION` | `IMPLEMENTING` | `IMPLEMENTED` | `VALIDATED` | `BLOCKED` | `DEFERRED`_

**Status**: `NOT_STARTED`
**Prioridade**: _P0 | P1 | P2 | P3_
**Esforco estimado**: _XS | S | M | L | XL_
**Risco**: _LOW | MEDIUM | HIGH | CRITICAL_

---

## Objetivo

_Uma frase clara descrevendo o que esta phase entrega e qual problema ela resolve._

---

## Contexto

_Por que esta phase e necessaria agora? Qual e o estado atual sem ela? Qual e o gatilho para implementa-la?_

---

## Escopo

_Lista explicita do que entra nesta phase. Seja especifico._

- Item 1
- Item 2
- Item 3

---

## Fora de escopo

_Lista explicita do que NAO entra, com justificativa quando nao for obvio._

- Item A (justificativa)
- Item B (justificativa)

---

## Dependencias

_Outras phases ou slices que precisam estar concluidos antes desta._

| Dependencia | Status | Observacao |
|-------------|--------|-----------|
| Phase N | `IMPLEMENTED` | Descricao |
| Slice X | `READY_FOR_IMPLEMENTATION` | Descricao |

---

## Entidades afetadas

_Lista de entidades de dominio criadas ou modificadas. Para entidades novas, liste invariantes criticas._

| Entidade | Criada/Modificada | Invariantes principais |
|----------|------------------|----------------------|
| `Payment` | Modificada | Novo campo X, nova transicao Y |
| `NovaEntidade` | Criada | Descricao de invariantes |

---

## APIs afetadas

_Endpoints criados ou modificados. Use a mesma nomenclatura de `009-api-contracts.md`._

| Metodo | Path | Criado/Modificado | Descricao |
|--------|------|------------------|-----------|
| `POST` | `/api/v1/exemplo` | Criado | Descricao |
| `GET` | `/api/v1/exemplo/{id}` | Modificado | Descricao |

---

## Eventos afetados

_Eventos de outbox gerados ou consumidos, webhooks externos tratados._

| Tipo | Criado/Modificado | Descricao |
|------|------------------|-----------|
| `payment.aprovado` | Criado | Descricao |
| `webhook.externo.provider` | Modificado | Descricao |

---

## Decisoes arquiteturais

_Decisoes tecnicas relevantes tomadas nesta phase. Se uma decisao e grande o suficiente, crie um ADR._

- Decisao 1: [justificativa]
- Decisao 2: [justificativa]

---

## ADRs relacionados

_ADRs que embasam ou sao gerados por esta phase._

- `ADR-000X-nome.md` — descricao

---

## Criterios de aceite

_Lista de criterios verificaveis. Cada item deve ser testavel por automacao ou validacao manual documentada._

1. [Criterio 1]: dado [contexto], quando [acao], entao [resultado esperado].
2. [Criterio 2]: ...
3. [Criterio 3]: ...

---

## Plano de implementacao sugerido

_Divida a phase em slices sequenciais. Cada slice deve ser entregavel de forma independente._

### Slice 1 — [Nome]

- Objetivo: ...
- Arquivos esperados: ...
- Criterio de aceite do slice: ...

### Slice 2 — [Nome]

- Objetivo: ...
- Arquivos esperados: ...
- Criterio de aceite do slice: ...

### Slice 3 — [Nome]

- Objetivo: ...
- Arquivos esperados: ...
- Criterio de aceite do slice: ...

---

## Plano de testes

| Tipo | Descricao | Obrigatorio |
|------|-----------|------------|
| Unitario | [caso de teste] | Sim |
| Integracao | [caso de teste] | Sim (quando afeta banco/workers) |
| E2E / Manual | [caso de teste] | Conforme slice |

---

## Riscos

| Risco | Probabilidade | Impacto | Mitigacao |
|-------|--------------|---------|----------|
| Risco 1 | Alta/Media/Baixa | Alto/Medio/Baixo | Descricao da mitigacao |

---

## Evidencias esperadas

_O que deve existir ao fim da phase para considera-la concluida?_

- `dotnet build`: 0 erros, 0 warnings relevantes.
- `dotnet test`: todos os testes passando, incluindo os novos.
- Spec atualizada (se contrato mudou).
- Validation matrix atualizada.
- Relatorio de validacao em `docs/audits/`.

---

## Gaps conhecidos

_Itens identificados durante o planejamento que nao serao resolvidos nesta phase. Documente para rastreabilidade._

- Gap 1: descricao e motivo de nao estar nesta phase.

---

## Proximo passo

_Qual e o proximo slice ou phase logico apos esta conclusao?_

---

## Arquivos relacionados

_Lista de arquivos de codigo e documentacao diretamente relacionados._

- `docs/specs/NNN-nome.md` (este arquivo)
- `src/PaymentHub.Domain/...`
- `src/PaymentHub.Application/...`
- `tests/PaymentHub.UnitTests/...`
