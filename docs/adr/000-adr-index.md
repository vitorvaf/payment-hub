# Indice de ADRs — Payment Hub

Data de referencia: 2026-06-17

Este arquivo indexa todas as decisoes arquiteturais registradas (ADRs) do Payment Hub. ADRs sao imutaveis apos aceitas — novas decisoes exigem novo ADR, nao edicao do anterior.

Status possivel: `PROPOSED` | `ACCEPTED` | `DEPRECATED` | `SUPERSEDED`

> **Nota sobre status de ADR:** Os status acima sao exclusivos do lifecycle de ADRs e sao independentes do enum operacional do harness (`NOT_STARTED`, `IMPLEMENTING`, `IMPLEMENTED`, etc.). Um ADR `ACCEPTED` nao significa que a feature esta implementada — significa apenas que a decisao arquitetural foi tomada e registrada. O status de implementacao da feature e rastreado no roadmap e nos specs, nao no ADR.
>
> | Status ADR | Significado |
> | ---------- | ----------- |
> | `PROPOSED` | Decisao identificada como necessaria; aguarda revisao e aprovacao. |
> | `ACCEPTED` | Decisao tomada e registrada; orienta implementacao. Imutavel. |
> | `DEPRECATED` | ADR ainda valido historicamente, mas a decisao nao e mais relevante para o produto atual. |
> | `SUPERSEDED` | Substituido por outro ADR mais recente. O ADR deve referenciar o substituto. |

---

## ADRs aceitas

| ADR | Titulo | Status | Data | Phase |
| --- | ------ | ------ | ---- | ----- |
| [ADR-0001](ADR-0001-use-dotnet-10-and-ef-core-10.md) | Usar .NET 10 e EF Core 10 | `ACCEPTED` | 2026-06-16 | Phase 0 |
| [ADR-0002](ADR-0002-use-postgres-inbox-outbox-in-mvp.md) | Usar PostgreSQL com Inbox/Outbox no MVP | `ACCEPTED` | 2026-06-16 | Phase 0 |
| [ADR-0003](ADR-0003-hosted-checkout-only.md) | Usar Apenas Checkout Hospedado | `ACCEPTED` | 2026-06-16 | Phase 0 |
| [ADR-0004](ADR-0004-api-key-server-to-server.md) | API Key Server-to-Server | `ACCEPTED` | 2026-06-16 | Phase 0 |
| [ADR-0005](ADR-0005-provider-status-canonicalization.md) | Canonicalizar Status de Provider | `ACCEPTED` | 2026-06-16 | Phase 0 |

---

## ADRs propostas (pendentes de decisao)

Estas ADRs foram identificadas como necessarias pela auditoria de 2026-06-17. Cada uma deve ser decidida antes que o slice correspondente entre em implementacao.

| ADR | Titulo sugerido | Status | Phase | Urgencia | Contexto |
| --- | --------------- | ------ | ----- | -------- | -------- |
| ADR-0006 | Politica de bootstrap e autenticacao de endpoints admin | `PROPOSED` | Phase 1, 6 | Alta | Endpoints `POST /api/v1/tenants` e `POST /api/v1/applications` precisam de politica explicita. O middleware atual exige API Key para todos os caminhos nao listados como anonimos, criando deadlock operacional para criacao do primeiro tenant. Deve decidir entre: seed de bootstrap via CLI, endpoint anonimo protegido por segredo de ambiente, ou endpoint admin com autenticacao separada. |
| ADR-0007 | Protecao de `ApplicationClient.WebhookSecret` em repouso | `PROPOSED` | Phase 6 | Alta | `webhook_secret` esta persistido em texto claro. Credenciais de provider usam `IDataProtectionProvider` (AES). Deve decidir entre: (a) aplicar mesma protecao do `IDataProtectionProvider`, (b) hash unidirecional com sal, (c) aceitar risco com mitigacoes documentadas e rotacao periodica. |
| ADR-0008 | Autenticacao do painel admin | `PROPOSED` | Phase 5 | Media | O painel admin (Phase 5) requer mecanismo de autenticacao diferente do API Key S2S. Deve decidir entre: OAuth 2.0 / OIDC com provider externo, magic link por email, autenticacao basica com MFA, ou acesso via VPN sem autenticacao publica. |
| ADR-0009 | Integridade referencial obrigatoria no banco | `PROPOSED` | Phase 1 | Media | A migracao inicial cria apenas FK de `payment_attempts.payment_id` para `payments.id`. Outras referencias (tenant_id, application_id, provider_account_id) sao logicas. Deve decidir explicitamente quais FKs sao obrigatorias no banco e quais permanecem apenas logicas por decisao consciente de performance e flexibilidade. |

---

## Template de ADR

Novo ADR deve seguir o template em `docs/harness/adr-template.md`.

---

## Arquivos relacionados

- `docs/harness/adr-template.md`
- `docs/specs/000-spec-index.md`
- `docs/roadmap/000-payment-hub-roadmap.md`
- `docs/audits/spec-adherence-audit-2026-06-17.md`
