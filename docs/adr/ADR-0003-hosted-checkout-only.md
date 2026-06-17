# ADR-0003 - Usar Apenas Checkout Hospedado

## Status

Aceito

## Contexto

O Payment Hub MVP nao e instituicao de pagamento e nao deve ampliar escopo PCI armazenando ou processando dados de cartao.

## Decisao

Usar apenas checkout hospedado por providers. O Payment Hub nunca armazena numero de cartao, CVV ou dados equivalentes.

## Consequencias

- O MVP reduz risco de seguranca e compliance.
- `Payment` armazena referencias, valor, moeda, status, provider e URLs, nao dados de cartao.
- UX de pagamento depende da experiencia hospedada do provider.

## Alternativas consideradas

- Formulario proprio de cartao: fora de escopo e aumenta risco PCI.
- Cartao salvo: fora de escopo do MVP.

## Data

2026-06-16
