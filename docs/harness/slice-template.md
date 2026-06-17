# Template de Slice

Copie este arquivo para o documento de task ou para uma secao de phase ao planejar um slice de implementacao.
Remova os comentarios em italico antes de commitar.

---

# Slice [NNN] — [Nome do Slice]

## Phase relacionada

_Qual phase este slice pertence? Ex.: Phase 6 — Seguranca e Confiabilidade._

**Phase**: Phase N — Nome
**Spec relacionada**: `docs/specs/NNN-nome.md`
**Data**: AAAA-MM-DD

---

## Objetivo

_Uma frase descrevendo o que este slice entrega especificamente. Deve ser um incremento de valor entregavel de forma independente._

---

## Escopo

_O que este slice muda ou cria. Seja especifico o suficiente para que um segundo engenheiro saiba exatamente o que implementar._

- [ ] Item 1
- [ ] Item 2
- [ ] Item 3

---

## Fora de escopo

_O que este slice NAO faz, mesmo que relacionado._

- Item A
- Item B

---

## Arquivos esperados

_Lista de arquivos a criar ou modificar. Inclua caminho relativo ao repositorio._

### Criados

- `src/PaymentHub.Domain/...`
- `src/PaymentHub.Application/...`
- `tests/PaymentHub.UnitTests/...`

### Modificados

- `src/PaymentHub.Api/...`
- `src/PaymentHub.Infrastructure.Postgres/...`

---

## Mudancas esperadas

_Descricao das mudancas em cada arquivo ou componente. Nao escreva codigo aqui; descreva a intencao._

- `ApiKeyAuthenticationMiddleware.cs`: adicionar verificacao de `TenantStatus.Active` e `ApplicationStatus.Active` apos resolver entidades.
- `CreateCheckoutHandler.cs`: retornar erro 403 quando tenant ou application estiverem inativos.

---

## Criterios de aceite

_Cada criterio deve ser verificavel. Use o formato: dado [contexto], quando [acao], entao [resultado]._

1. Dado tenant com `Status = Suspended`, quando chamada autenticada for recebida, entao retornar 403.
2. Dado application com `Status = Inactive`, quando checkout for solicitado, entao retornar 403.
3. Dado tenant ativo e application ativa, quando checkout for solicitado, entao comportamento nao muda.

---

## Testes obrigatorios

_Testes que devem ser escritos neste slice. Sem estes testes, o slice nao esta concluido._

| Tipo | Descricao | Arquivo |
|------|-----------|---------|
| Unitario | Tenant inativo retorna 403 | `tests/.../NomeTestes.cs` |
| Unitario | Application inativa retorna 403 | `tests/.../NomeTestes.cs` |
| Unitario | Tenant e application ativos — comportamento inalterado | `tests/.../NomeTestes.cs` |
| Integracao | (se aplicavel) | `tests/PaymentHub.IntegrationTests/...` |

---

## Validacoes manuais

_Passos de validacao manual quando automacao nao for suficiente ou pratica._

1. `dotnet build PaymentHub.slnx` — esperado: 0 erros, 0 warnings.
2. `dotnet test PaymentHub.slnx` — esperado: todos os testes passando.
3. (se aplicavel) Testar endpoint manualmente via Swagger ou curl.

---

## Riscos

| Risco | Mitigacao |
|-------|----------|
| Risco de regressao em fluxo existente | Testes de regressao cobrindo caminho feliz com tenant e application ativos |

---

## Evidencias de conclusao

_O que deve existir como evidencia de que este slice foi concluido corretamente?_

- `dotnet test` passando com novos testes incluidos.
- Saida do build sem erros.
- (se aplicavel) Screenshot ou log de validacao manual.
- Spec ou doc atualizada se contrato mudou.

---

## Rollback ou mitigacao

_Como desfazer este slice se necessario? O que monitorar nas primeiras horas apos implantacao?_

- Reverter commit do slice via `git revert`.
- Monitorar logs de autenticacao para 403 inesperados.
- Health check `/health` deve continuar respondendo 200.

---

## Arquivos relacionados

- `docs/specs/NNN-nome.md`
- `docs/harness/definition-of-ready.md`
- `docs/harness/definition-of-done.md`
- `docs/harness/validation-matrix.md`
