# ADR-0005 - Canonicalizar Status de Provider

## Status

Aceito

## Contexto

Cada provider usa vocabulario proprio para pagamentos. Consumidores internos precisam de contrato estavel.

## Decisao

Persistir e expor `PaymentStatus` canonico independente do provider. Adapters e mappers traduzem status bruto para o enum interno.

## Consequencias

- Trocar ou adicionar provider nao altera o contrato principal do consumidor.
- Status desconhecido deve ter fallback seguro e observavel.
- Testes de mapper por provider sao obrigatorios.

## Alternativas consideradas

- Expor status bruto: acopla consumidores aos providers.
- Criar enum por provider na API publica: aumenta complexidade sem valor para o consumidor.

## Data

2026-06-16
