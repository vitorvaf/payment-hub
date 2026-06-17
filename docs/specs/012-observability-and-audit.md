# Observabilidade e Auditoria

## Objetivo

Definir informacoes esperadas em logs, traces futuros e `AuditLog`.

## Escopo

- Campos de log.
- Dados proibidos em logs.
- Acoes administrativas auditaveis.

## Fora de escopo

- Stack completa de APM, metricas e dashboards.

## Regras obrigatorias

- Logs devem ser estruturados.
- Logs nunca devem incluir API Key, provider secret, webhook secret, payload sensivel, dados de cartao ou CVV.
- Erros em producao nao devem retornar stack trace.
- Acoes administrativas sensiveis geram `AuditLog`.

## Contratos

Logs devem incluir quando disponivel:

- correlation id;
- tenant id;
- application id;
- payment id;
- provider;
- event id;
- status;
- retry count.

`AuditLog` deve registrar:

- criacao/alteracao de tenant;
- criacao/alteracao de application;
- criacao/revogacao de API key;
- criacao/alteracao de provider account;
- reprocessamento manual futuro de webhook/outbox;
- alteracoes sensiveis.

## Criterios de aceite

- Incidentes podem ser investigados por tenant, application, payment e event id.
- Logs de erro preservam mensagem util sem segredo.
- AuditLog contem ator, acao, entidade, id e metadata segura.

## Testes esperados

- Validacao manual ou automatizada de campos de log em fluxos criticos.
- Testes de AuditLog quando handlers administrativos evoluirem.

## Arquivos relacionados

- `src/PaymentHub.Domain/Entities/AuditLog.cs`
- `src/PaymentHub.Api/Program.cs`
- `src/PaymentHub.Worker/`
