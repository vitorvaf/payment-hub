# Estrategia de Testes

## Objetivo

Definir matriz de testes esperada para evoluir o Payment Hub com seguranca.

## Escopo

- Unit tests.
- Application tests.
- Integration tests.
- Security tests.

## Fora de escopo

- Testes de carga e caos no MVP inicial.

## Regras obrigatorias

- Cobertura deve crescer conforme risco e blast radius.
- Testes devem priorizar comportamento observavel.
- Idempotencia, retries e seguranca precisam de cenarios negativos.

## Contratos

Unit tests:

- entidades;
- status transitions;
- payment status mapper;
- retry policy;
- request hashing;
- HMAC signature;
- API key hashing.

Application tests:

- create checkout;
- idempotencia;
- provider routing;
- webhook processing;
- outbox creation.

Integration tests:

- PostgreSQL schema;
- unique indexes;
- API key middleware;
- create checkout end-to-end com Fake;
- webhook fake end-to-end;
- worker processing;
- outbox dispatch com HTTP fake.

Security tests:

- API key invalida;
- tenant/application incompatibil;
- idempotency key ausente;
- payload duplicado;
- logs sem secrets quando possivel.

## Criterios de aceite

- Todo slice de implementacao declara validacoes executadas.
- Mudanca de contrato adiciona ou atualiza teste correspondente.
- Falha de teste relevante bloqueia merge ate ser explicada ou corrigida.

## Testes esperados

- Esta spec e a propria matriz de testes esperados.

## Arquivos relacionados

- `tests/PaymentHub.UnitTests/`
- `tests/PaymentHub.IntegrationTests/`
- `.github/instructions/testing.instructions.md`
- `docs/harness/validation.md`
