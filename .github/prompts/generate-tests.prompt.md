# Generate Tests

## Objetivo

Criar ou ajustar testes que protejam comportamento relevante sem acoplar a detalhes frageis.

## Entradas

- Codigo ou bug alvo.
- Spec relacionada.
- Tipo de teste desejado: unitario, application, integracao ou E2E.

## Passos

1. Identifique comportamento observavel.
2. Escolha a camada de teste mais barata que da confianca.
3. Cubra sucesso, falha e edge case relevante.
4. Evite mocks sem valor comportamental.
5. Rode `dotnet test` ou teste especifico.

## Criterios de aceite

- Teste falharia antes da correcao quando for regressao.
- Dados sensiveis nao aparecem em asserts ou logs.
- Teste e deterministico.

## Saida esperada

Testes criados/alterados, cenario coberto e comandos executados.
