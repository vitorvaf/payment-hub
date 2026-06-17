# ADR-0001 - Usar .NET 10 e EF Core 10

## Status

Aceito

## Contexto

O ambiente local do projeto usa SDK .NET 10.0.109 e runtime 10.0.9. O scaffold inicial foi criado com `net10.0`, EF Core 10 e Npgsql 10 em todos os projetos.

## Decisao

Usar `net10.0`, EF Core 10 e Npgsql 10 no MVP, mantendo `PaymentHub.slnx` como arquivo de solucao.

## Consequencias

- API, Worker, Infrastructure e testes ficam alinhados na mesma plataforma.
- Pipelines e comandos locais devem usar SDK compativel com .NET 10.
- Dependencias devem ser revisadas para compatibilidade com EF Core 10.

## Alternativas consideradas

- .NET 9: menor risco de disponibilidade, mas desalinhado ao ambiente atual.
- Gerar `.sln` legado: possivel, mas `slnx` e o formato atual gerado pelo SDK local.

## Data

2026-06-16
