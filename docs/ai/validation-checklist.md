# Validation Checklist

Use esta checklist junto com `docs/harness/validation.md`.

## Docs e harness

- `AGENTS.md` aponta para docs principais.
- `.github/copilot-instructions.md` continua curto e acionavel.
- Instrucoes em `.github/instructions/` tem `applyTo`.
- Prompts, agentes e skills nao duplicam specs.
- Links principais existem.
- Nao ha secrets, tokens ou `.env` real.

## Codigo .NET

- Spec relacionada foi lida.
- ADR relevante foi lido quando houver decisao arquitetural.
- `dotnet restore` executou com sucesso ou foi justificado.
- `dotnet build` executou com sucesso ou foi justificado.
- `dotnet test` executou com sucesso ou foi justificado.
- Teste novo cobre bug ou comportamento novo quando aplicavel.

## Docker e banco

- `docker compose config` executou com sucesso para mudancas em compose.
- Migrations foram revisadas quando schema mudou.
- Plano de rollback existe para mudanca de banco.
- Valores de ambiente sao fake/dev-safe.

## Segurança

- Nenhum dado de cartao ou CVV foi armazenado.
- API Keys reais nao aparecem em docs, codigo ou logs.
- Endpoints autenticados usam `ITenantContext`.
- Idempotencia foi preservada para criacao de checkout.
- Inbox/Outbox foi preservado para webhooks/eventos.

## Evidencia

- Arquivos alterados foram listados.
- Comandos e resultados foram listados.
- Riscos residuais foram descritos.
- `docs/harness/learnings.md` foi atualizado quando necessario.
