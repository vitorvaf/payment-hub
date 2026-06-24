# Spec Adherence Refresh - 2026-06-24

## Escopo

Auditoria curta de configuracao/documentacao para atualizar o estado de aderencia entre specs e codigo sem alterar produto.

Foram revisados:

- `docs/specs/README.md`, `011-security-and-compliance.md` e `013-testing-strategy.md`.
- Reports existentes em `docs/audits/`.
- Evidencias estaticas em `src/`, `tests/`, `docs/specs/` e `docs/audits/`.
- Resultado atual de build/test local.

Fora de escopo:

- Corrigir dominio, migrations, workers, providers ou testes.
- Subir banco real ou executar E2E.
- Alterar contratos HTTP.

## Resultado

Status geral: Parcial.

O repositorio continua aderente ao desenho base do MVP: Clean Architecture, checkout hospedado, API Key server-to-server, Postgres com Inbox/Outbox, provider Fake para desenvolvimento/testes, idempotencia e ausencia de cartao/CVV no dominio principal. A configuracao de agentes e CI melhorou desde a auditoria anterior, mas os gaps de produto e teste abaixo continuam pendentes.

## Gaps confirmados

| ID | Prioridade | Area | Evidencia | Proximo slice sugerido |
|----|------------|------|-----------|------------------------|
| PH-SEC-001 | P1 | Seguranca | `ApplicationClient.WebhookSecret` e mapeado como `webhook_secret` sem protecao equivalente a provider credentials. | Definir ADR-0007 e proteger secret em repouso. |
| PH-WORKER-001 | P1 | Outbox | `PaymentHub.Worker` registra dispatcher no-op para `IApplicationWebhookDispatcher`; risco de marcar evento como enviado sem HTTP real no host worker. | Implementar dispatcher real no worker ou separar processo dedicado explicitamente. |
| AI-002 | P2 | Testes | `PaymentHub.IntegrationTests` compila, mas `dotnet test` nao descobre testes. | Criar fixture de integracao com Postgres e cenarios minimos. |
| AI-003 | P2 | Testes | Nao ha smoke/E2E API + banco + worker automatizado. | Criar smoke local com `docker compose`, health e checkout fake. |
| PH-SEC-002 | P2 | Webhooks externos | Adapters reais ainda sao skeleton e nao validam assinatura externa. | Implementar validacao antes de uso fora de sandbox. |
| PH-AUD-001 | P2 | Auditoria | `AuditLog` existe, mas handlers administrativos nao gravam eventos sensiveis. | Adicionar auditoria em tenants, applications e provider accounts. |
| PH-DB-001 | P2 | Banco | Migration inicial usa poucas FKs; specs nao deixam claro quais referencias sao logicas no MVP. | Decidir contrato em spec/ADR antes de nova migration. |
| DOC-001 | P3 | Docs | `docs/architecture/overview.md` ainda pode carregar descricao antiga de HMAC interno em trechos historicos. | Alinhar overview ao contrato `{timestamp}.{rawBody}`. |

## Melhorias de configuracao aplicadas neste slice

- CI com cache NuGet, verificacao de harness e upload de resultados `.trx`.
- `scripts/agent-verify.sh` validando arquivos OpenCode e scan simples de secrets obvios.
- OpenCode alinhado a `AGENTS.md`, Copilot instructions, specs, agents equivalentes e validacoes.
- README e docs IA explicando uso diario do harness.
- Backlog atualizado em `feature_list.md`.

## Validacao

- `scripts/agent-verify.sh`: passou.
- `dotnet restore`: passou.
- `dotnet build --no-restore`: passou com 0 warnings e 0 errors.
- `dotnet test --no-build --logger "trx;LogFilePrefix=test-results" --results-directory TestResults`: passou com 106 testes unitarios; projeto de integracao ainda sem testes descobertos.

## Risco residual

Esta auditoria e estatica e curta. Ela prioriza backlog e configuracao; nao substitui testes de integracao, E2E, analise dinamica de seguranca ou revisao humana de mudancas sensiveis.
