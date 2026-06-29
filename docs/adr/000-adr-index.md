# Indice de ADRs — Payment Hub

Data de referencia: 2026-06-26

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
| [ADR-0007](ADR-0007-webhook-secret-protection.md) | Protecao de `ApplicationClient.WebhookSecret` em repouso (AES-CBC reversivel com prefixo de proposito) | `ACCEPTED` | 2026-06-25 | Phase 6 (Slice 6-C) / Phase 7 (Slice 7-A.9) |
| [ADR-0010](ADR-0010-real-outbox-dispatcher-location.md) | Localizacao e arquitetura do dispatcher HTTP real do Outbox (Infrastructure.Postgres.Webhooks, Scoped, IOutboxEventStore, IClock, tenant guard, LastError seguro, WebhookUrl HTTPS/SSRF) | `ACCEPTED` | 2026-06-26 | Phase 7 (Slice 7-A) |

> **Nota sobre gaps no indice:** Os numeros 0006, 0008 e 0009 continuam na secao de propostas/observacoes abaixo. O numero 0006 (politica de bootstrap) foi implementado pelo Slice 6-D em 2026-06-18 e tem o conteudo documentado em `docs/audits/slice-6d-bootstrap-admin-seed-policy-report-2026-06-18.md`, mas o arquivo `ADR-0006-bootstrap-admin-seed-policy.md` ainda nao foi criado (fora do escopo do Slice 7-A.9).

---

## ADRs propostas (pendentes de decisao)

Estas ADRs foram identificadas como necessarias pela auditoria de 2026-06-17 ou em auditorias subsequentes. Cada uma deve ser decidida antes que o slice correspondente entre em implementacao.

| ADR | Titulo sugerido | Status | Phase | Urgencia | Contexto |
| --- | --------------- | ------ | ----- | -------- | -------- |
| ADR-0006 | Politica de bootstrap e autenticacao de endpoints admin | `PROPOSED` (conteudo implementado pelo Slice 6-D em 2026-06-18; arquivo de ADR ainda nao criado) | Phase 1, 6 | Alta | Endpoints `POST /api/v1/tenants` e `POST /api/v1/applications` precisam de politica explicita. Slice 6-D resolveu a politica via `IBootstrapPolicy` + `BootstrapOptions` + `IDevelopmentDataSeeder`. Decisao final de formalizar a ADR fica para slice proprio. |
| ADR-0008 | Autenticacao do painel admin | `PROPOSED` | Phase 5 | Media | O painel admin (Phase 5) requer mecanismo de autenticacao diferente do API Key S2S. Deve decidir entre: OAuth 2.0 / OIDC com provider externo, magic link por email, autenticacao basica com MFA, ou acesso via VPN sem autenticacao publica. |
| ADR-0009 | Integridade referencial obrigatoria no banco | `PROPOSED` | Phase 1 | Media | A migracao inicial cria apenas FK de `payment_attempts.payment_id` para `payments.id`. Outras referencias (tenant_id, application_id, provider_account_id) sao logicas. Deve decidir explicitamente quais FKs sao obrigatorias no banco e quais permanecem apenas logicas por decisao consciente de performance e flexibilidade. |

> **Nota sobre o numero ADR-0007:** o conteudo da ADR-0007 (protecao de `WebhookSecret`) foi implementado pelo Slice 6-C em 2026-06-25 e formalizado em `docs/audits/slice-6c-webhook-secret-protection-report-2026-06-25.md`. O arquivo desta ADR foi criado nesta consolidacao do Slice 7-A.9 (2026-06-26) e referencia o report.

> **Nota sobre o numero ADR-0010:** o conteudo da ADR-0010 (dispatcher HTTP real) foi implementado pelos sub-slices 7-A.1 a 7-A.8 entre 2026-06-25 e 2026-06-26 e formalizado em `docs/audits/slice-7a-real-outbox-dispatcher-report-2026-06-26.md`. O arquivo desta ADR foi criado nesta consolidacao do Slice 7-A.9 (2026-06-26) e referencia o report.

---

## Template de ADR

Novo ADR deve seguir o template em `docs/harness/adr-template.md`.

---

## Arquivos relacionados

- `docs/harness/adr-template.md`
- `docs/specs/000-spec-index.md`
- `docs/roadmap/000-payment-hub-roadmap.md`
- `docs/audits/spec-adherence-audit-2026-06-17.md`
- `docs/audits/slice-6c-webhook-secret-protection-report-2026-06-25.md`
- `docs/audits/slice-6d-bootstrap-admin-seed-policy-report-2026-06-18.md`
- `docs/audits/slice-7a-real-outbox-dispatcher-report-2026-06-26.md`
