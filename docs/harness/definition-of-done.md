# Definition of Done — Phase/Slice

Uma phase ou slice so esta concluida quando satisfaz todos os criterios abaixo. O objetivo e evitar considerar trabalho como "feito" sem evidencias objetivas.

---

## Criterios obrigatorios

### 1. Codigo implementado

- Todos os arquivos de codigo previstos na spec foram criados ou modificados.
- O codigo segue as convencoes do projeto (Clean Architecture, nomenclatura, DI).
- Nenhum `TODO` critico ou `throw new NotImplementedException()` restou no escopo da phase.
- Nenhum secret, credencial ou API Key real foi commitado.

### 2. Testes escritos e passando

- Testes unitarios de dominio e application escritos para os novos comportamentos.
- Testes de integracao escritos quando o slice afeta: banco de dados, middleware, workers ou comunicacao HTTP externa.
- Todos os testes do projeto continuam passando: `dotnet test PaymentHub.slnx`.
- Nenhum teste foi ignorado ou desabilitado sem justificativa documentada.

### 3. Build limpo

- `dotnet build PaymentHub.slnx` retorna 0 erros e 0 warnings relevantes.
- Vulnerabilidades conhecidas (`NU1903` e equivalentes) foram avaliadas e tratadas ou aceitas com justificativa.

### 4. Documentacao atualizada

- Spec correspondente esta atualizada se houver mudancas de contrato.
- `docs/specs/000-spec-index.md` esta atualizado com novas specs criadas.
- `docs/roadmap/002-phase-status-board.md` esta atualizado com o novo status da phase.
- Endpoints novos ou modificados estao refletidos em `docs/specs/009-api-contracts.md`.
- Se a visao de arquitetura mudou, `docs/architecture/overview.md` esta atualizado.

### 5. Migrations documentadas (quando aplicavel)

- Toda migration nova esta referenciada em `docs/specs/010-database-contract.md`.
- A migration foi testada em banco limpo e em banco com dados existentes (quando aplicavel).
- Indices e constraints criticos estao documentados e existem no banco.

### 6. Observabilidade considerada

- Logs estruturados emitem contexto de correlacao (tenant_id, payment_id, event_id) nos fluxos novos.
- Nenhum log emite secrets, API Keys, credenciais ou dados sensiveis.
- Health checks existentes continuam funcionais.
- Se a phase adiciona nova operacao critica, um log de auditoria ou metrica foi considerado.

### 7. Validacoes executadas e registradas

- Os comandos de validacao previstos em `docs/harness/validation-matrix.md` foram executados.
- Resultados estao registrados com data, comando e resultado real versus esperado.
- Falhas foram investigadas, corrigidas e re-validadas.

### 8. Sem gaps P0 abertos

- Nenhum gap P0 foi identificado ou deixado aberto sem plano de correcao imediata.
- Gaps P1 novos identificados durante implementacao foram documentados em auditoria ou spec.

### 9. Relatorio final da phase gerado

- Existe um relatorio em `docs/audits/` ou no arquivo de task cobrindo: o que foi feito, evidencias, gaps encontrados e proximo passo sugerido.
- O relatorio referencia os arquivos criados/modificados.

---

## Verificacao rapida (checklist)

```
[ ] Codigo implementado sem NotImplementedException critico
[ ] Nenhum secret commitado
[ ] dotnet build: 0 erros, 0 warnings relevantes
[ ] dotnet test: todos os testes passando
[ ] Testes unitarios novos escritos
[ ] Testes de integracao escritos (quando aplicavel)
[ ] Spec atualizada (se contrato mudou)
[ ] Spec index atualizado (se spec nova criada)
[ ] Status board atualizado
[ ] Migrations documentadas (se aplicavel)
[ ] Logs sem secrets
[ ] Validacoes executadas e registradas
[ ] Sem gaps P0 abertos
[ ] Relatorio da phase criado
```

---

## Quando NAO considerar concluido

- Build com erros ou warnings de seguranca nao tratados.
- Testes falhando ou desabilitados sem justificativa.
- `NotImplementedException` no caminho critico da feature.
- Contrato de API ou evento mudou sem atualizacao de spec.
- Gaps P1 encontrados durante implementacao nao foram documentados.
- Validacoes nao foram executadas ou resultados nao foram registrados.

---

## Arquivos relacionados

- `docs/harness/definition-of-ready.md`
- `docs/harness/phase-template.md`
- `docs/harness/slice-template.md`
- `docs/harness/validation-matrix.md`
- `docs/harness/workflow.md`
- `docs/harness/validation.md`
