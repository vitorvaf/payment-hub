# ADR Generation Skill

Use quando uma decisao arquitetural duradoura precisar ser registrada.

## Nao use quando

- A mudanca for operacional ou temporaria.
- Uma spec simples for suficiente.

## Entradas

- Contexto.
- Decisao.
- Alternativas.
- Consequencias.
- Specs afetadas.

## Passos

1. Leia ADRs existentes.
2. Escolha criar ADR nova ou atualizar existente.
3. Use `docs/harness/adr-template.md`.
4. Registre status, contexto, decisao e consequencias.
5. Atualize `docs/adr/000-adr-index.md`.

## Checklist

- Decisao rastreavel.
- Alternativas consideradas.
- Consequencias explicitas.
- Specs linkadas quando aplicavel.

## Saida esperada

ADR curta, indice atualizado e resumo de impacto.
