# ADR-0004 - API Key Server-to-Server

## Status

Aceito

## Contexto

Aplicacoes clientes precisam chamar o Payment Hub de forma server-to-server, com isolamento por tenant e application.

## Decisao

Usar API Key no header `Authorization: Bearer <api_key>`, com `X-Tenant-Id` e `X-Application-Id`. Persistir somente hash HMAC e prefixo auditavel; exibir chave em claro apenas uma vez na criacao.

## Consequencias

- Vazamento de banco nao expõe chaves em claro.
- Middleware precisa validar escopo tenant/application em toda chamada autenticada.
- Rotacao/revogacao de chaves deve ser auditavel.

## Alternativas consideradas

- OAuth completo: excesso para MVP server-to-server.
- Basic auth: menos expressivo para escopo e rotacao.
- Armazenar chave em claro: rejeitado por seguranca.

## Data

2026-06-16
