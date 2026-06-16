# Payment Gateway Instructions

## Contexto

O Payment Gateway MVP será um orquestrador de pagamentos multitenant para centralizar integrações com provedores como Abacate Pay, Stripe, Mercado Pago e futuros gateways.

## Diretrizes de domínio

- Tratar tenant como fronteira obrigatória de autorização, configuração e dados.
- Usar checkout hospedado no MVP; não armazenar cartão nem CVV.
- Modelar status canônico interno para pagamentos, independente do vocabulário de cada provedor.
- Exigir idempotência em todo endpoint de criação de pagamento.
- Persistir webhooks recebidos em Inbox antes de processar.
- Publicar eventos de saída por Outbox.
- Usar Worker para processamento assíncrono, retries e entrega de eventos.
- Preparar adapters por provedor, começando por `FakePaymentProvider` quando o domínio for implementado.

## Fora de escopo do MVP

- RabbitMQ, Kafka e Azure Service Bus.
- Split financeiro, wallet, cartão salvo, CVV e antifraude complexo.
- Conciliação completa e painel admin completo no primeiro slice.
