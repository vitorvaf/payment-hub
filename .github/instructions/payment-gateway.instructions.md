---
applyTo: "src/**/*.{cs,json};docs/specs/**/*.md;docs/api/**/*.md;docs/database/**/*.md"
---

# Payment Gateway Instructions

- Trate tenant como fronteira obrigatoria de autorizacao, configuracao e dados.
- Use checkout hospedado no MVP; nao armazene cartao nem CVV.
- Modele status canonico interno para pagamentos, independente do vocabulario de cada provider.
- Exija idempotencia em todo endpoint de criacao de pagamento.
- Provider explicito nunca deve cair silenciosamente para outro provider.
- Persista webhooks recebidos em Inbox antes de processar.
- Publique eventos de saida por Outbox.
- Use Worker para processamento assincrono, retries e entrega de eventos.
- Use `FakePaymentProvider` para desenvolvimento local e testes quando provider real nao for parte do slice.
- Antes de alterar contratos de pagamento, leia `docs/specs/004-payment-lifecycle.md`, `005-checkout-creation.md`, `006-provider-webhooks.md`, `007-inbox-outbox-workers.md`, `008-provider-adapters.md`, `009-api-contracts.md` e `014-job-search-integration.md` conforme o caso.
- Fora de escopo do MVP: RabbitMQ, Kafka, Azure Service Bus, split financeiro, wallet, cartao salvo, CVV, antifraude complexo, conciliacao completa e painel admin completo.
