# Project Context

## Nome

Payment Gateway MVP.

## Objetivo

Construir um orquestrador de pagamentos multitenant para centralizar integrações com provedores de pagamento e oferecer uma API consistente para produtos consumidores.

## Spec Driven Development

As especificações formais em `docs/specs/` são a fonte de verdade para contratos de implementação. ADRs em `docs/adr/` registram decisões arquiteturais aceitas.

## Consumidores previstos

- Job Search / Quero Vagas Tech.
- Futuros produtos internos.

## Provedores previstos

- Abacate Pay.
- Stripe.
- Mercado Pago.
- Futuros gateways.

## Decisões de MVP

- O projeto não será uma instituição de pagamento.
- O MVP não armazenará cartão.
- O MVP nunca armazenará CVV.
- O MVP usará checkout hospedado.
- O MVP usará PostgreSQL com padrões Inbox/Outbox em vez de RabbitMQ, Kafka ou Azure Service Bus no início.
- A arquitetura deve permitir evolução futura para broker sem reescrever o domínio.
- O primeiro provedor implementável deve poder ser um `FakePaymentProvider` para testes e desenvolvimento local.

## Stack esperada

- .NET.
- PostgreSQL.
- Docker.
- API REST.
- Swagger/OpenAPI.
- Worker.
- Clean Architecture.
- Logs estruturados.
- Testes automatizados.

## Fora de escopo inicial

- Split financeiro.
- Wallet.
- Cartão salvo.
- CVV.
- Antifraude complexo.
- Conciliação completa.
- Painel admin completo no primeiro slice.
