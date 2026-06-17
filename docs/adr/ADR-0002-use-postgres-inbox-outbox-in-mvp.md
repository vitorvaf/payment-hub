# ADR-0002 - Usar PostgreSQL com Inbox/Outbox no MVP

## Status

Aceito

## Contexto

O MVP precisa ser operavel rapidamente, com menos dependencias, mas ainda tolerante a retries, duplicidade e falhas temporarias.

## Decisao

Usar tabelas `webhook_events` e `outbox_events` no PostgreSQL, consumidas por workers .NET. RabbitMQ, Kafka e Azure Service Bus ficam fora do MVP.

## Consequencias

- Menos infraestrutura no desenvolvimento local e deploy inicial.
- O dominio deve permanecer desacoplado do mecanismo de entrega.
- Quando a escala exigir, o dispatcher de outbox pode ser substituido por publisher de broker.

## Alternativas consideradas

- RabbitMQ no MVP: adiciona operacao antes de haver necessidade.
- Kafka no MVP: excesso para o primeiro slice.
- Azure Service Bus no MVP: acopla a decisao de nuvem cedo demais.

## Data

2026-06-16
