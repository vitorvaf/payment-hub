# Payment Hub — Painel de Status de Fases

Data de referencia: 2026-06-17

## Dashboard de status

| Phase | Nome | Status | Gaps P1 | Gaps P2 | Proximo slice |
|-------|------|--------|---------|---------|--------------|
| 0 | Produto, Arquitetura e Fronteiras | `IMPLEMENTED` | 0 | 1 (doc HMAC desatualizada) | Slice documental |
| 1 | Core Domain MVP e API | `IMPLEMENTED` | 3 | 2 | Slice 6-A (status ativo) |
| 2 | Primeiro Adapter de Provider | `IMPLEMENTING` | 0 | 1 (assinatura webhook) | Slice 2-A (AbacatePay) |
| 3 | Webhooks Externos e Internos | `IMPLEMENTING` | 1 (noop dispatcher) | 1 (assinatura provider) | Slice 7-A (dispatcher real) |
| 4 | Multi-Provider | `SPEC_DRAFTED` | 0 | 0 | Aguarda Phase 2 + Phase 6 |
| 5 | Painel Admin | `NOT_STARTED` | 0 | 0 | Aguarda Phase 6 |
| 6 | Seguranca e Confiabilidade | `IMPLEMENTING` | 4 (criticos) | 1 (audit log) | Slice 6-A |
| 7 | Workers e Outbox | `IMPLEMENTING` | 1 (noop) | 1 (testes integracao) | Slice 7-A |
| 8 | Conciliacao Financeira | `NOT_STARTED` | 0 | 0 | Aguarda Phase 4 + 7 |
| 9 | Relatorios e Observabilidade | `SPEC_DRAFTED` | 0 | 0 | Aguarda Phase 6 + 7 |
| 10 | Evolucoes Futuras | `NOT_STARTED` | 0 | 0 | Backlog de produto |

---

## Estado atual do MVP (2026-06-17)

### O que esta funcionando

- Criacao de checkout hospedado com provider Fake.
- Idempotencia de checkout por `Idempotency-Key`.
- Recebimento de webhook externo persistido como Inbox.
- Processamento assincrono de webhooks e atualizacao de status canonico.
- Outbox de eventos internos criado, mas dispatch ainda e no-op no worker dedicado.
- Autenticacao por API Key com hash HMAC.
- Credenciais de providers protegidas por AES.
- Status canonico independente de provider.
- 64 testes unitarios passando; build limpo.

### Gaps P1 abertos (auditoria 2026-06-17)

Fonte: `docs/audits/spec-adherence-audit-2026-06-17.md`

| # | Gap | Phase afetada | Slice sugerido |
|---|-----|--------------|---------------|
| P1-1 | Tenant/application inativos nao bloqueiam fluxos autenticados | Phase 1, 6 | Slice 6-A |
| P1-2 | `RegisterProviderAccountHandler` usa tenant/application do body, nao do contexto autenticado | Phase 1, 6 | Slice 6-B |
| P1-3 | Endpoints de tenant/application divergem entre spec e middleware quanto a autenticacao | Phase 1, 6 | Slice 6-D (bootstrap policy) |
| P1-4 | Worker dedicado de outbox usa `NoopApplicationWebhookDispatcher` | Phase 3, 7 | Slice 7-A |
| P1-5 | `ApplicationClient.WebhookSecret` persistido em texto claro | Phase 6 | Slice 6-C |

### Gaps P2 relevantes (auditoria 2026-06-17)

| # | Gap | Phase afetada |
|---|-----|--------------|
| P2-1 | Assinatura de webhooks externos nao e validada nos adapters reais | Phase 2, 4 |
| P2-2 | Projeto de testes de integracao sem testes descobertos | Phase 1, 3, 7 |
| P2-3 | Acoes administrativas sensiveis nao gravam `AuditLog` | Phase 6 |
| P2-4 | Integridade referencial no banco e parcial (poucas FKs) | Phase 1 |
| P2-5 | Documentacao de arquitetura usa formato antigo de assinatura HMAC | Phase 0 |

---

## Proximo bloco de trabalho recomendado

### Bloco A — Seguranca e Confiabilidade (Phase 6 + Phase 7)

Resolver os 5 gaps P1 antes de qualquer expansao de provider ou feature de produto.

```
Slice 6-A  Enforcement de TenantStatus.Active + ApplicationStatus.Active
Slice 7-A  Substituir NoopApplicationWebhookDispatcher por HTTP real
Slice 6-B  RegisterProviderAccountHandler via ITenantContext
Slice 6-C  Protecao de ApplicationClient.WebhookSecret em repouso
Slice 6-D  Politica de bootstrap/admin + AuditLog em handlers administrativos
```

### Bloco B — Testes de Integracao (Phase 1 + 3 + 7)

Criar primeira fixture de integracao com Testcontainers ou Docker Compose.

```
Slice 1-IT  Fixture Postgres + migrations + indices criticos
Slice 3-IT  Testes de middleware, checkout autenticado e idempotencia
Slice 7-IT  Testes de workers (inbox/outbox) com banco real
```

### Bloco C — Provider AbacatePay (Phase 2)

Ativar primeiro provider real apos seguranca e confiabilidade estarem solidas.

```
Slice 2-A  Adapter AbacatePay funcional + validacao de assinatura webhook
Slice 2-T  Testes do adapter e documentacao
```

---

## Indicadores de saude

| Indicador | Valor atual | Meta |
|-----------|------------|------|
| Testes unitarios passando | 64 | >= 64 |
| Testes de integracao | 0 | >= 5 |
| Gaps P0 abertos | 0 | 0 |
| Gaps P1 abertos | 5 | 0 |
| Gaps P2 abertos | 5 | <= 2 |
| Build status | Limpo | Limpo |
| Providers reais funcionais | 0 (Fake ok) | >= 1 (AbacatePay) |

---

## Arquivos relacionados

- `docs/roadmap/000-payment-hub-roadmap.md`
- `docs/roadmap/001-development-timeline.md`
- `docs/audits/spec-adherence-audit-2026-06-17.md`
- `docs/audits/roadmap-adherence-matrix-2026-06-17.md`
