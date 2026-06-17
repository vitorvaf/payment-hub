# Definition of Ready — Phase/Slice

Uma phase ou slice so esta pronta para implementacao quando satisfaz todos os criterios abaixo. O objetivo e evitar iniciar trabalho com ambiguidade que gere retrabalho.

---

## Criterios obrigatorios

### 1. Spec criada e revisada

- Existe arquivo de spec em `docs/specs/` com nome e numero corretos.
- Spec contem: objetivo, escopo, fora de escopo, regras obrigatorias, contratos, criterios de aceite e testes esperados.
- Spec foi revisada e nao ha ambiguidades abertas que bloqueiem implementacao.

### 2. Escopo e fora de escopo definidos

- A lista de "o que entra" esta explicita e delimitada.
- A lista de "o que nao entra" esta explicita, evitando gold-plating.
- Decisoes que ainda nao foram tomadas estao listadas como pendencias com responsavel e prazo.

### 3. Entidades afetadas listadas

- Cada entidade de dominio nova ou modificada esta identificada: `Tenant`, `Payment`, `WebhookEvent`, etc.
- Invariantes criticas de novas entidades estao documentadas na spec.
- Migrations necessarias estao identificadas (mesmo que nao escritas ainda).

### 4. APIs afetadas listadas

- Cada endpoint novo ou modificado esta identificado por metodo e path.
- Contratos de entrada e saida (payload, headers, status HTTP) estao descritos na spec ou referenciados de `009-api-contracts.md`.
- Quebras de compatibilidade estao sinalizadas explicitamente.

### 5. Eventos afetados listados

- Cada evento de outbox gerado ou consumido esta identificado por tipo (`payment.approved`, `payment.failed`, etc.).
- Cada webhook externo consumido esta identificado por provider e tipo.
- Mudancas em payload de eventos existentes estao documentadas.

### 6. Criterios de aceite definidos

- Os criterios de aceite sao verificaveis (testavel por automacao ou por validacao manual documentada).
- Cada criterio tem um resultado esperado claro.
- Criterios cobrem ao menos: caminho feliz, principal caminho de erro e idempotencia quando aplicavel.

### 7. Riscos conhecidos documentados

- Riscos tecnicos e de produto estao listados na spec.
- Cada risco tem uma mitigacao sugerida ou uma decisao de aceitar o risco com justificativa.

### 8. Plano de testes minimo definido

- Tipos de teste esperados estao listados: unitario, integracao, e2e, manual.
- Para cada tipo, ha pelo menos um caso de teste descrito.
- A cobertura minima de testes de integracao esta definida quando o slice afeta banco, workers ou middleware.

### 9. Dependencias resolvidas ou explicitamente bloqueadas

- Dependencias de outras phases/slices estao identificadas.
- Para cada dependencia: esta resolvida (fase concluida) ou esta bloqueada com justificativa explicita e plano de desbloqueio.
- Nao ha dependencias implicitas (suposicoes nao documentadas sobre estado externo).

### 10. ADRs necessarios identificados

- Se a implementacao exige decisao arquitetural nova, o ADR esta identificado (pode estar em rascunho).
- Se a implementacao usa uma decisao ja tomada, o ADR relevante esta referenciado na spec.

---

## Verificacao rapida (checklist)

```
[ ] Spec existe em docs/specs/ com formato correto
[ ] Escopo definido (incluso e excluido)
[ ] Entidades afetadas listadas
[ ] APIs afetadas listadas (metodo + path + contrato)
[ ] Eventos afetados listados (outbox + webhooks)
[ ] Criterios de aceite verificaveis
[ ] Riscos com mitigacao
[ ] Plano de testes minimo (unitario + integracao + e2e conforme slice)
[ ] Dependencias resolvidas ou bloqueadas com justificativa
[ ] ADRs necessarios identificados
```

---

## Quando NAO iniciar

- Spec nao existe ou esta apenas em rascunho com campos obrigatorios em branco.
- Escopo nao esta delimitado e pode crescer durante implementacao.
- Dependencias nao resolvidas e sem previsao de resolucao.
- Riscos conhecidos sem mitigacao e com impacto potencial P0/P1.
- Criterios de aceite sao ambiguos ou nao verificaveis.

---

## Arquivos relacionados

- `docs/harness/definition-of-done.md`
- `docs/harness/phase-template.md`
- `docs/harness/slice-template.md`
- `docs/harness/validation-matrix.md`
- `docs/harness/workflow.md`
