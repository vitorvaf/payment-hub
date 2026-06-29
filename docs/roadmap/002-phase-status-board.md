# Payment Hub — Painel de Status de Fases

Data de referencia: 2026-06-26

## Dashboard de status

| Phase | Nome | Status | Gaps P1 proprios | Gaps P2 | Proximo slice |
|-------|------|--------|------------------|---------|--------------|
| 0 | Produto, Arquitetura e Fronteiras | `IMPLEMENTED` | 0 | 1 (doc HMAC desatualizada) | Slice documental |
| 1 | Core Domain MVP e API | `IMPLEMENTED` | 0 proprios¹ | 2 | Aguarda Phase 6 (validacao final) |
| 2 | Primeiro Adapter de Provider | `IMPLEMENTING` | 0 | 1 (assinatura webhook) | Slice 2-A CONCLUIDO 2026-06-27 — Checkout Transparente PIX sandbox |
| 3 | Webhooks Externos e Internos | `IMPLEMENTING` | 0 proprios² | 1 (assinatura provider) | Slice 2-A (provider real para validar) |
| 4 | Multi-Provider | `SPEC_DRAFTED` | 0 | 0 | Aguarda Phase 2 + Phase 6 + Phase 7 |
| 5 | Painel Admin | `NOT_STARTED` | 0 | 0 | Aguarda Phase 6 |
| 6 | Seguranca e Confiabilidade | `IMPLEMENTING` | **0 proprios³** | 1 (audit log P2-3) | Aguarda P2-3 |
| 7 | Workers e Outbox | `IMPLEMENTING` | **0 proprios⁴** | 1 (end-to-end API+Worker, sweep Processing, multi-instancia) | Aguarda Phase 9 / Slice 7-IT (multi-instancia) |
| 8 | Conciliacao Financeira | `NOT_STARTED` | 0 | 0 | Aguarda Phase 4 + 7 |
| 9 | Relatorios e Observabilidade | `SPEC_DRAFTED` | 0 | 0 | Aguarda Phase 6 + 7 |
| 10 | Evolucoes Futuras | `NOT_STARTED` | 0 | 0 | Backlog de produto |

Notas:

¹ Phase 1 tinha 2 gaps que se manifestavam no codigo (P1-1 resolvido em 2026-06-17, P1-2 resolvido em 2026-06-18, P1-3 resolvido em 2026-06-18). Phase 1 e considerada `IMPLEMENTED` porque entregou o dominio central. Nao e `VALIDATED` ate Phase 6 estar fechada por completo (P2-3).

> **Slice 6-A (2026-06-17):** gap P1-1 (Tenant/application inativos nao bloqueiam fluxos autenticados) foi resolvido pelo `ApiKeyAuthenticationMiddleware`, que agora consulta `Tenant.Status` e `ApplicationClient.Status` apos validar a API Key.

> **Slice 6-B (2026-06-18):** gap P1-2 (`RegisterProviderAccountHandler` usava `tenantId`/`applicationId` do body) foi resolvido. `ProviderAccount` agora e criado exclusivamente a partir de `ITenantContext`. Body do `POST /api/v1/provider-accounts` nao aceita mais `tenantId`/`applicationId`. Restavam 2 gaps P1 da Phase 6: P1-3 (politica de bootstrap) e P1-5 (`WebhookSecret` em texto claro).

> **Slice 6-D (2026-06-18):** gap P1-3 (politica de bootstrap/admin seed) foi resolvido. `IBootstrapPolicy` + `BootstrapOptions` + `IDevelopmentDataSeeder` formalizam a politica: `Production` nao cria nada automaticamente a menos que `AllowProductionBootstrap=true` (opt-in explicito); `Development`/`Test` podem rodar seed idempotente de tenant+application apenas com `Bootstrap:Enabled=true` e `Bootstrap:SeedDevelopmentData=true`; logs nao registram API Key, secrets ou credenciais.

> **Slice 6-C (2026-06-25):** gap P1-5 (`ApplicationClient.WebhookSecret` persistido em texto claro) foi resolvido. `IWebhookSecretProtector` + `AesWebhookSecretProtector` passam a cifrar o segredo antes de persistir (AES-CBC com chave em `PaymentHub:WebhookSecretEncryptionKey` e prefixo `PaymentHub.ApplicationClient.WebhookSecret.v1`). DTO `ApplicationClientResponseDto` expoe apenas `hasWebhookSecret: bool`. `HttpApplicationWebhookDispatcher` chama `Unprotect` no momento da assinatura HMAC e aborta o dispatch se a decifragem falhar. Seedor de desenvolvimento protege tambem o segredo fake opcional. Detalhes em `docs/audits/slice-6c-webhook-secret-protection-report-2026-06-25.md`. Phase 6 alcancou 0 gaps P1 proprios.

> **Slice 7-A (2026-06-26, sub-slices 7-A.1 a 7-A.9):** gap P1-4 (`NoopApplicationWebhookDispatcher` registrado no Worker host) foi resolvido. `HttpApplicationWebhookDispatcher` realocado para `src/PaymentHub.Infrastructure.Postgres/Webhooks/` com lifetime Scoped, `IHttpClientFactory` nomeado, DI centralizado em `AddPaymentHubPostgres`. `OutboxDispatcherWorker` agora usa `IOutboxRepository`, `IOutboxEventStore` e `IClock` (testavel sem `DbContext` direto). Tenant guard via `_apps.GetByTenantAndIdAsync`. `OutboxEvent.LastError` passou a armazenar apenas `WebhookDispatcherCategory` + `int?` statusCode (7 categorias enum: `HttpFailure`, `NetworkError`, `Timeout`, `UnprotectFailure`, `MissingWebhookUrl`, `MissingWebhookSecret`, `UnexpectedDispatcherError`); `ex.Message` nunca e persistido. `ApplicationClient.WebhookUrl` agora e validada por `RegisterApplicationClientValidator` (HTTPS obrigatorio + bloqueio de loopback/RFC1918/link-local/IMDS/wildcard). Worker tem fail-fast de `IWebhookSecretProtector` no startup (`Worker/Program.cs:53-56`). `appsettings.json` (production) tem placeholder vazio para `PaymentHub:WebhookSecretEncryptionKey`; `appsettings.Development.json` mantem valor fake. ADRs `ADR-0007-webhook-secret-protection.md` e `ADR-0010-real-outbox-dispatcher-location.md` consolidadas. Phase 7 alcancou 0 gaps P1 proprios. Detalhes em `docs/audits/slice-7a-real-outbox-dispatcher-report-2026-06-26.md`.

> **Slice 2-A (2026-06-27):** primeiro adapter AbacatePay funcional para Checkout Transparente PIX em sandbox/devMode. `IAbacatePayClient` + `AbacatePayClient` (HTTP via `IHttpClientFactory`, `Authorization: Bearer <api-key>`, envelope `{data, success, error}`, mapeamento de 400/401/403/404/429/5xx + network + timeout + envelope-failure + simulation-disabled para `AbacatePayErrorCategory`). `AbacatePayProviderAdapter` unprotect via `ICredentialProtector`, extrai `apiKey`, monta payload PIX (amount em centavos, customer omit-if-null, metadata tenantId/applicationId/paymentId/externalReference, expiry 3600s), sintetiza `abacatepay://pix/<id>` como `CheckoutUrl`. `PaymentStatusMapper.MapAbacatePay` estendido com `redeemed->Approved` e `under_dispute->Pending`. `CreateCheckoutProviderRequest` ganha `ProviderAccountId`/`ProviderEnvironment`/`ProtectedCredentials` opcionais (backward-compat); `CreateCheckoutHandler.ResolveProviderAsync` retorna `ResolvedProvider` record preservando o `ProviderAccount`. DI: `AddPaymentHubProviders` registra `IOptionsMonitor<AbacatePayOptions>`, `HttpClient "abacatepay"` nomeado, `IAbacatePayClient` Singleton. 57 testes novos (40 client + 17 adapter) com `ScriptedHandler` + `SingleHandlerHttpClientFactory` + `FakeCredentialProtector`. Total suite: 348 testes. Build limpo; arquitectura-check + docs-check + git diff --check verdes. Detalhes em `docs/audits/slice-2a-abacatepay-sandbox-report-2026-06-26.md`. Phase 2 atinge o primeiro marco de adapter real. Webhook HMAC + normalizacao de eventos seguem em Slice 2-B.

² Phase 3 originou o gap P1-4 (`NoopApplicationWebhookDispatcher`), mas a correcao e escopo da Phase 7. A coluna "Gaps P1 proprios" reflete gaps cuja correcao e responsabilidade desta phase, nao onde o sintoma aparece.

³ Phase 6 esta com 0 gaps P1 proprios apos o Slice 6-C. Os 5 gaps P1 originais da auditoria de 2026-06-17 foram resolvidos pelos Slices 6-A, 6-B, 6-C e 6-D. A fase continua `IMPLEMENTING` ate que P2-3 (AuditLog em handlers administrativos) seja fechado.

⁴ Phase 7 esta com 0 gaps P1 proprios apos o Slice 7-A (2026-06-26) e a entrega da base de integracao via Slice 1-IT (2026-06-26: migrations + repositorios principais + Outbox via Testcontainers, 10 testes passando). A fase continua `IMPLEMENTING` ate que gaps P2 remanescentes (sweep de eventos `Processing` orfaos, `FOR UPDATE SKIP LOCKED` para multi-instancia, end-to-end API+Worker com banco real) sejam fechados em slices proprios.

---

## Estado atual do MVP (2026-06-26)

### O que esta funcionando

- Criacao de checkout hospedado com provider Fake.
- Idempotencia de checkout por `Idempotency-Key`.
- Recebimento de webhook externo persistido como Inbox.
- Processamento assincrono de webhooks e atualizacao de status canonico.
- Outbox de eventos internos com dispatcher HTTP real assinado via HMAC (Slice 7-A).
- Autenticacao por API Key com hash HMAC.
- Credenciais de providers protegidas por AES.
- `WebhookSecret` protegido em repouso via AES-CBC reversivel (Slice 6-C).
- `WebhookUrl` validada por HTTPS + protecao SSRF (Slice 7-A.5).
- `OutboxEvent.LastError` seguro por categoria enum (Slice 7-A.7).
- Worker com fail-fast de chave criptografica (Slice 7-A.3 + 7-A.6).
- Tenant/application enforcement no middleware (Slice 6-A).
- Status canonico independente de provider.
- 281 testes unitarios + 10 testes de integracao com Postgres (Testcontainers) passando; build limpo.

### Gaps P1 resolvidos (auditoria de 2026-06-17)

Fonte: `docs/audits/spec-adherence-audit-2026-06-17.md`

| # | Gap | Phase afetada | Slice |
|---|-----|--------------|-------|
| P1-1 | Tenant/application inativos nao bloqueiam fluxos autenticados | Phase 1, 6 | Slice 6-A `[RESOLVIDO 2026-06-17]` |
| P1-2 | `RegisterProviderAccountHandler` usa tenant/application do body, nao do contexto autenticado | Phase 1, 6 | Slice 6-B `[RESOLVIDO 2026-06-18]` |
| P1-3 | Endpoints de tenant/application divergem entre spec e middleware quanto a autenticacao | Phase 1, 6 | Slice 6-D (bootstrap policy) `[RESOLVIDO 2026-06-18]` |
| P1-4 | Worker dedicado de outbox usa `NoopApplicationWebhookDispatcher` | Phase 3, 7 | Slice 7-A `[RESOLVIDO 2026-06-26]` |
| P1-5 | `ApplicationClient.WebhookSecret` persistido em texto claro | Phase 6 | Slice 6-C `[RESOLVIDO 2026-06-25]` |

### Gaps P2 relevantes (auditoria de 2026-06-17 + gaps novos)

| # | Gap | Phase afetada | Slice sugerido |
|---|-----|---------------|----------------|
| P2-1 | Assinatura de webhooks externos nao e validada nos adapters reais | Phase 2, 4 | Slice 2-A (AbacatePay) |
| P2-2 | Projeto de testes de integracao sem testes descobertos | Phase 1, 3, 7 | Slice 1-IT `[PARCIALMENTE RESOLVIDO 2026-06-26 — 10 testes passam; e2e API+Worker ainda nao coberto]` |
| P2-3 | Acoes administrativas sensiveis nao gravam `AuditLog` | Phase 6 | Proximo slice de Phase 6 |
| P2-4 | Integridade referencial no banco e parcial (poucas FKs) | Phase 1 | ADR-0009 (proposto) |
| P2-5 | Documentacao de arquitetura usa formato antigo de assinatura HMAC | Phase 0 | Slice documental |
| M1-security (novo) | Sweep automatico de eventos `Processing` orfaos | Phase 7 | Slice 7-M1 (multi-instancia) |
| C.3-qa (novo) | `FOR UPDATE SKIP LOCKED` em `OutboxRepository` para multi-instancia | Phase 7 | Slice 7-IT (multi-instancia) |
| B4-security (novo) | Headers `X-PaymentHub-Tenant`/`X-PaymentHub-Application` nao validados | Phase 3, 7 | Deferred (HMAC ja garante autenticidade) |

---

## Proximo bloco de trabalho recomendado

### Bloco A — Seguranca e Confiabilidade (Phase 6 + Phase 7) — `CONCLUIDO 2026-06-26`

Os 5 gaps P1 da auditoria de 2026-06-17 foram resolvidos. Phase 6 e Phase 7 estao com 0 gaps P1 proprios.

```
Slice 6-A  Enforcement de TenantStatus.Active + ApplicationStatus.Active   [CONCLUIDO 2026-06-17]
Slice 7-A  Substituir NoopApplicationWebhookDispatcher por HTTP real       [CONCLUIDO 2026-06-26]
Slice 6-B  RegisterProviderAccountHandler via ITenantContext                [CONCLUIDO 2026-06-18]
Slice 6-C  Protecao de ApplicationClient.WebhookSecret em repouso          [CONCLUIDO 2026-06-25]
Slice 6-D  Politica de bootstrap/admin + AuditLog em handlers administrativos  [CONCLUIDO 2026-06-18 — politica de bootstrap; P2-3 pendente]
```

### Bloco B — Testes de Integracao (Phase 1 + 3 + 7)

Criar primeira fixture de integracao com Testcontainers ou Docker Compose.

```
Slice 1-IT  Fixture Postgres + migrations + indices criticos + repositorios principais   [CONCLUIDO 2026-06-26]
Slice 3-IT  Testes de middleware, checkout autenticado e idempotencia
Slice 7-IT  Testes de workers (inbox/outbox) com banco real
```

### Bloco C — Provider AbacatePay (Phase 2)

Ativar primeiro provider real apos seguranca e confiabilidade estarem solidas.

```
Slice 2-A  Adapter AbacatePay funcional + validacao de assinatura webhook   [CONCLUIDO 2026-06-27 — Checkout Transparente PIX sandbox; webhooks externos/HMAC em Slice 2-B]
Slice 2-B  Webhooks externos AbacatePay + normalizacao de eventos          [PENDENTE — depende de decisao de produto (HMAC obrigatorio + tenant routing)]
Slice 2-T  Testes do adapter e documentacao adicional                       [CONCLUIDO 2026-06-27 dentro do Slice 2-A — 57 testes novos; cobertura adicional pode ir em micro-slices]
```

### Bloco D — Phase 5 Painel Admin (apos Phase 6 fechada)

```
Slice 5-A  ADR-0008 autenticacao do painel admin
Slice 5-B  Endpoints admin autenticados
Slice 5-C  UI minima de gestao de tenants/applications/provider accounts
```

---

## Indicadores de saude

| Indicador | Valor atual | Meta |
|-----------|------------|------|
| Testes unitarios passando | 348 | >= 64 |
| Testes de integracao (Postgres real) | 10 | >= 5 |
| Gaps P0 abertos | 0 | 0 |
| Gaps P1 abertos | **0** | 0 |
| Gaps P2 abertos | 8 | <= 2 |
| Build status | Limpo | Limpo |
| Providers reais funcionais | 1 (AbacatePay PIX sandbox) | >= 1 (AbacatePay) |

---

## Arquivos relacionados

- `docs/roadmap/000-payment-hub-roadmap.md`
- `docs/roadmap/001-development-timeline.md`
- `docs/audits/spec-adherence-audit-2026-06-17.md`
- `docs/audits/roadmap-adherence-matrix-2026-06-17.md`
- `docs/audits/slice-6c-webhook-secret-protection-report-2026-06-25.md`
- `docs/audits/slice-7a-real-outbox-dispatcher-report-2026-06-26.md`
- `docs/audits/slice-1-it-postgres-integration-tests-report-2026-06-26.md`
- `docs/audits/slice-7a5-webhook-url-ssrf-report-2026-06-26.md`
- `docs/audits/slice-7a6-worker-appsettings-webhook-secret-key-report-2026-06-26.md`
- `docs/audits/slice-2a-abacatepay-sandbox-report-2026-06-26.md`
