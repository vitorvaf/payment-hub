# Escopo do MVP

## Objetivo

Definir o que entra no MVP do Payment Hub: um orquestrador de pagamentos multitenant com checkout hospedado, API consistente, Inbox/Outbox em PostgreSQL, provider Fake e base preparada para provedores reais.

## Escopo

- Tenants, applications e provider accounts.
- API Key server-to-server.
- Criacao de checkout hospedado.
- Entidades `Payment` e `PaymentAttempt`.
- Inbox de webhooks externos em `webhook_events`.
- Status canonicamente mapeado em `PaymentStatus`.
- Outbox de eventos internos em `outbox_events`.
- Workers para processamento e dispatch.
- Provider Fake para desenvolvimento local e testes.
- Documentacao, specs, ADRs e testes automatizados proporcionais ao slice.

## Fora de escopo

- Split financeiro, wallet, cartao salvo, CVV e recorrencia completa.
- Antifraude complexo e conciliacao completa.
- Painel admin completo.
- Broker externo obrigatorio no MVP.
- O Payment Hub atuar como instituicao de pagamento.

## Regras obrigatorias

- Nao armazenar numero de cartao, CVV ou PAN mascarado como dado obrigatorio.
- Usar checkout hospedado.
- Usar PostgreSQL com Inbox/Outbox no MVP.
- Manter dominio preparado para broker externo futuro sem reescrita.
- Manter `FakePaymentProvider` funcional para desenvolvimento local e testes.
- Exigir idempotencia em endpoints de criacao de pagamento.

## Contratos

- A API publica contratos HTTP versionados sob `/api/v1`.
- O banco persiste estado canonico do pagamento, eventos de entrada e eventos de saida.
- Provedores sao acessados por adapters que traduzem vocabulos externos para status canonico.

## Criterios de aceite

- Um consumidor consegue criar tenant, application, checkout e receber evento interno de pagamento.
- Webhook externo e persistido antes de processamento pesado.
- Evento interno passa por outbox e retry.
- Secrets nao aparecem em banco em claro, logs ou respostas.

## Testes esperados

- Unitarios de dominio, status, retry e mapeamento.
- Application tests de checkout, idempotencia e provider routing.
- Integracao para API, banco, middleware e workers quando o slice exigir.

## Arquivos relacionados

- `docs/architecture/overview.md`
- `docs/architecture/mvp-decisions.md`
- `docs/harness/project-context.md`
- `README.md`
