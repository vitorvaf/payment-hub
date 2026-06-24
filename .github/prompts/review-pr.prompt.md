# Review PR

## Objetivo

Revisar uma mudanca priorizando bugs, regressao, seguranca, contratos e testes.

## Entradas

- Diff ou branch.
- Specs/ADRs relacionados.
- Descricao do PR.

## Passos

1. Leia o diff antes de conclusoes.
2. Compare com specs e ADRs.
3. Procure quebra de multitenancy, idempotencia, Inbox/Outbox, auth e logs sensiveis.
4. Verifique testes e validacoes.
5. Liste achados por severidade.

## Criterios de aceite

- Achados incluem arquivo e linha quando possivel.
- Nao misturar nitpicks com riscos reais.
- Se nao houver achados, declarar isso e apontar riscos residuais.

## Saida esperada

Findings primeiro, perguntas abertas, resumo curto e lacunas de teste.
