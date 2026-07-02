# Payment Hub — Painel de Status de Fases

Data de referencia: 2026-07-01

## Dashboard de status

| Phase | Nome | Status | Gaps P1 proprios | Gaps P2 | Proximo slice |
|-------|------|--------|------------------|---------|--------------|
| 0 | Produto, Arquitetura e Fronteiras | `IMPLEMENTED` | 0 | 1 (doc HMAC desatualizada) | Slice documental |
| 1 | Core Domain MVP e API | `IMPLEMENTED` | 0 proprios¹ | 2 | Aguarda Phase 6 (validacao final) |
| 2 | Primeiro Adapter de Provider | `IMPLEMENTED` | 0 | 0 | Slice 2-B CONCLUIDO 2026-06-29 — webhooks externos AbacatePay + HMAC |
| 3 | Webhooks Externos e Internos | `IMPLEMENTING` | 0 proprios² | 0 (assinatura provider resolvida para AbacatePay via Slice 2-B) | Aguarda webhook provider Stripe/MercadoPago (Phase 4) |
| 4 | Multi-Provider | `SPEC_DRAFTED` | 0 | 0 | Aguarda Phase 2 + Phase 6 + Phase 7 (Phase 2 agora `IMPLEMENTED`) |
| 5 | Painel Admin | `NOT_STARTED` | 0 | 0 | Aguarda Phase 6 |
| 6 | Seguranca e Confiabilidade | `IMPLEMENTING` | **0 proprios³** | 1 (audit log P2-3) | Aguarda P2-3 |
| 7 | Workers e Outbox | `IMPLEMENTED` | 0 proprios⁴ | 0 (multi-instancia + sweep Processing orfao RESOLVIDO via Slice 7-M1 2026-06-30; e2e API+Worker RESOLVIDO via Slice 7-IT 2026-06-30; cobertura E2E 498 testes 7-M1+7-IT) | Aguarda Phase 6 (validacao final) |
| 8 | Conciliacao Financeira | `NOT_STARTED` | 0 | 0 | Aguarda Phase 4 + 7 |
| 9 | Relatorios e Observabilidade | `IMPLEMENTING` | 0 | 0 (Slice 9-O1 entregue 2026-07-01: catalogo +63 testes + gate anti-leak) | Aguarda Phase 6 + instrumentacao ativa em Slice 9-O2+ |
| 10 | Evolucoes Futuras | `NOT_STARTED` | 0 | 0 | Backlog de produto |

Notas:

¹ Phase 1 tinha 2 gaps que se manifestavam no codigo (P1-1 resolvido em 2026-06-17, P1-2 resolvido em 2026-06-18, P1-3 resolvido em 2026-06-18). Phase 1 e considerada `IMPLEMENTED` porque entregou o dominio central. Nao e `VALIDATED` ate Phase 6 estar fechada por completo (P2-3).

> **Slice 6-A (2026-06-17):** gap P1-1 (Tenant/application inativos nao bloqueiam fluxos autenticados) foi resolvido pelo `ApiKeyAuthenticationMiddleware`, que agora consulta `Tenant.Status` e `ApplicationClient.Status` apos validar a API Key.

> **Slice 6-B (2026-06-18):** gap P1-2 (`RegisterProviderAccountHandler` usava `tenantId`/`applicationId` do body) foi resolvido. `ProviderAccount` agora e criado exclusivamente a partir de `ITenantContext`. Body do `POST /api/v1/provider-accounts` nao aceita mais `tenantId`/`applicationId`. Restavam 2 gaps P1 da Phase 6: P1-3 (politica de bootstrap) e P1-5 (`WebhookSecret` em texto claro).

> **Slice 6-D (2026-06-18):** gap P1-3 (politica de bootstrap/admin seed) foi resolvido. `IBootstrapPolicy` + `BootstrapOptions` + `IDevelopmentDataSeeder` formalizam a politica: `Production` nao cria nada automaticamente a menos que `AllowProductionBootstrap=true` (opt-in explicito); `Development`/`Test` podem rodar seed idempotente de tenant+application apenas com `Bootstrap:Enabled=true` e `Bootstrap:SeedDevelopmentData=true`; logs nao registram API Key, secrets ou credenciais.

> **Slice 6-C (2026-06-25):** gap P1-5 (`ApplicationClient.WebhookSecret` persistido em texto claro) foi resolvido. `IWebhookSecretProtector` + `AesWebhookSecretProtector` passam a cifrar o segredo antes de persistir (AES-CBC com chave em `PaymentHub:WebhookSecretEncryptionKey` e prefixo `PaymentHub.ApplicationClient.WebhookSecret.v1`). DTO `ApplicationClientResponseDto` expoe apenas `hasWebhookSecret: bool`. `HttpApplicationWebhookDispatcher` chama `Unprotect` no momento da assinatura HMAC e aborta o dispatch se a decifragem falhar. Seedor de desenvolvimento protege tambem o segredo fake opcional. Detalhes em `docs/audits/slice-6c-webhook-secret-protection-report-2026-06-25.md`. Phase 6 alcancou 0 gaps P1 proprios.

> **Slice 7-A (2026-06-26, sub-slices 7-A.1 a 7-A.9):** gap P1-4 (`NoopApplicationWebhookDispatcher` registrado no Worker host) foi resolvido. `HttpApplicationWebhookDispatcher` realocado para `src/PaymentHub.Infrastructure.Postgres/Webhooks/` com lifetime Scoped, `IHttpClientFactory` nomeado, DI centralizado em `AddPaymentHubPostgres`. `OutboxDispatcherWorker` agora usa `IOutboxRepository`, `IOutboxEventStore` e `IClock` (testavel sem `DbContext` direto). Tenant guard via `_apps.GetByTenantAndIdAsync`. `OutboxEvent.LastError` passou a armazenar apenas `WebhookDispatcherCategory` + `int?` statusCode (7 categorias enum: `HttpFailure`, `NetworkError`, `Timeout`, `UnprotectFailure`, `MissingWebhookUrl`, `MissingWebhookSecret`, `UnexpectedDispatcherError`); `ex.Message` nunca e persistido. `ApplicationClient.WebhookUrl` agora e validada por `RegisterApplicationClientValidator` (HTTPS obrigatorio + bloqueio de loopback/RFC1918/link-local/IMDS/wildcard). Worker tem fail-fast de `IWebhookSecretProtector` no startup (`Worker/Program.cs:53-56`). `appsettings.json` (production) tem placeholder vazio para `PaymentHub:WebhookSecretEncryptionKey`; `appsettings.Development.json` mantem valor fake. ADRs `ADR-0007-webhook-secret-protection.md` e `ADR-0010-real-outbox-dispatcher-location.md` consolidadas. Phase 7 alcancou 0 gaps P1 proprios. Detalhes em `docs/audits/slice-7a-real-outbox-dispatcher-report-2026-06-26.md`.

> **Slice 2-A (2026-06-27):** primeiro adapter AbacatePay funcional para Checkout Transparente PIX em sandbox/devMode. `IAbacatePayClient` + `AbacatePayClient` (HTTP via `IHttpClientFactory`, `Authorization: Bearer <api-key>`, envelope `{data, success, error}`, mapeamento de 400/401/403/404/429/5xx + network + timeout + envelope-failure + simulation-disabled para `AbacatePayErrorCategory`). `AbacatePayProviderAdapter` unprotect via `ICredentialProtector`, extrai `apiKey`, monta payload PIX (amount em centavos, customer omit-if-null, metadata tenantId/applicationId/paymentId/externalReference, expiry 3600s), sintetiza `abacatepay://pix/<id>` como `CheckoutUrl`. `PaymentStatusMapper.MapAbacatePay` estendido com `redeemed->Approved` e `under_dispute->Pending`. `CreateCheckoutProviderRequest` ganha `ProviderAccountId`/`ProviderEnvironment`/`ProtectedCredentials` opcionais (backward-compat); `CreateCheckoutHandler.ResolveProviderAsync` retorna `ResolvedProvider` record preservando o `ProviderAccount`. DI: `AddPaymentHubProviders` registra `IOptionsMonitor<AbacatePayOptions>`, `HttpClient "abacatepay"` nomeado, `IAbacatePayClient` Singleton. 57 testes novos (40 client + 17 adapter) com `ScriptedHandler` + `SingleHandlerHttpClientFactory` + `FakeCredentialProtector`. Total suite: 348 testes. Build limpo; arquitectura-check + docs-check + git diff --check verdes. Detalhes em `docs/audits/slice-2a-abacatepay-sandbox-report-2026-06-26.md`. Phase 2 atinge o primeiro marco de adapter real. Webhook HMAC + normalizacao de eventos seguem em Slice 2-B.

² Phase 3 originou o gap P1-4 (`NoopApplicationWebhookDispatcher`), mas a correcao e escopo da Phase 7. A coluna "Gaps P1 proprios" reflete gaps cuja correcao e responsabilidade desta phase, nao onde o sintoma aparece. O gap residual sobre assinatura de webhooks externos foi fechado pelo Slice 2-B (2026-06-29) para o provider AbacatePay; webhooks de Stripe/MercadoPago ainda dependem de Phase 4.

³ Phase 6 esta com 0 gaps P1 proprios apos o Slice 6-C. Os 5 gaps P1 originais da auditoria de 2026-06-17 foram resolvidos pelos Slices 6-A, 6-B, 6-C e 6-D. A fase continua `IMPLEMENTING` ate que P2-3 (AuditLog em handlers administrativos) seja fechado.

⁴ Phase 7 alcancou 0 gaps P1 proprios apos o Slice 7-A (2026-06-26), a entrega da base de integracao via Slice 1-IT (2026-06-26: migrations + repositorios principais + Outbox via Testcontainers, 10 testes passando), a suite E2E do dispatcher via Slice 7-IT (2026-06-30: 7 testes em `tests/PaymentHub.IntegrationTests/EndToEnd/OutboxDispatcherE2ETests.cs` cobrindo Sent, HMAC, retry 500/429, UnprotectFailure, fluxo AbacatePay e no-redispatch de Sent) e a multi-instancia + sweep de `Processing` orfao via Slice 7-M1 (2026-06-30: 7 testes em `OutboxDispatcherConcurrencyTests` + `OutboxProcessingSweepTests`; cobertura E2E total de Phase 7 = 498 testes). Phase 7 promovida a `IMPLEMENTED` em 2026-06-30; a fase NAO e `VALIDATED` ate Phase 6 estar fechada por completo (P2-3 audit log).

⁵ Phase 9 sai de `SPEC_DRAFTED` para `IMPLEMENTING` apos o Slice 9-O1 (2026-07-01). Entrega da slice 9-O1: header HTTP `X-Correlation-Id` inbound/outbound, coluna `correlation_id VARCHAR(64) NULL` em `webhook_events` + `outbox_events` (migration `20260701000001_AddObservabilityColumns`), `CorrelationIdMiddleware` registrado ANTES de `ApiKeyAuthenticationMiddleware` em `Program.cs`, 13 counters + 3 histograms no `Meter` "PaymentHub" com tag whitelist (7 chaves), catalogo `PaymentHubLogEvents` com 31 eventos canonicos, helpers `SafeLog` (`Id`/`Length`/`Flag`/`Category`), gate regex anti-leak em `scripts/agent-docs-check.sh` cobrindo 6 tokens sensitive, suite 547 testes (522 baseline + 25 novos). Worker host continua sem `HttpContext` (usa `NullCorrelationIdAccessor` Singleton). E2E `CorrelationIdE2ETests` adicionado mas NAO EXECUTADO nesta slice (sem Docker); sera validado na proxima sessao. Restam para 9-O2+: instrumentacao ativa nos handlers/workers + distributed tracing via `Activity`/OpenTelemetry (ambos fora de escopo MVP). Detalhes em `docs/audits/slice-9-o1-observability-minimal-report-2026-07-01.md`.

---

## Estado atual do MVP (2026-07-01)

### O que esta funcionando

- Criacao de checkout hospedado com provider Fake.
- Idempotencia de checkout por `Idempotency-Key`.
- Adapter AbacatePay funcional em sandbox/devMode para Checkout Transparente PIX (Slice 2-A).
- Webhooks externos AbacatePay com HMAC-SHA256(Base64) + normalizacao de eventos `transparent.*` + fail-fast 401 no controller + roteamento por metadata no handler (Slice 2-B).
- Endpoints `PUT`/`GET /api/v1/provider-accounts/{id}/webhook` para gerenciar inscricao de webhook AbacatePay via API com feature flag opt-in (Slice 2-C; cliente HTTP real deferred em 2-C.1).
- Recebimento de webhook externo persistido como Inbox.
- Processamento assincrono de webhooks e atualizacao de status canonico.
- Outbox de eventos internos com dispatcher HTTP real assinado via HMAC (Slice 7-A).
- Suite E2E do ciclo Outbox → ApplicationClient webhook (dispatcher real + Postgres real + API real) com 7 testes P1+P2 (Slice 7-IT).
- Autenticacao por API Key com hash HMAC.
- Credenciais de providers protegidas por AES.
- `WebhookSecret` protegido em repouso via AES-CBC reversivel (Slice 6-C).
- `WebhookUrl` validada por HTTPS + protecao SSRF (Slice 7-A.5).
- `OutboxEvent.LastError` seguro por categoria enum (Slice 7-A.7).
- Worker com fail-fast de chave criptografica (Slice 7-A.3 + 7-A.6).
- Tenant/application enforcement no middleware (Slice 6-A).
- Status canonico independente de provider.
- 467 testes unitarios + 24 testes de integracao com Postgres (Testcontainers) passando; build limpo.

### Slices concluidos apos a geracao inicial (2026-06-30)

| # | Gap / Marco | Phase | Slice | Data |
|---|------------|-------|-------|------|
| P2-1 | Assinatura de webhooks externos validada no adapter AbacatePay | Phase 2, 3 | **Slice 2-B `[RESOLVIDO 2026-06-29]`** | 2026-06-29 |
| P2-2 | Suite E2E do dispatcher Outbox + AbacatePay flow ate delivery interno (7 testes P1+P2) | Phase 7 | **Slice 7-IT `[RESOLVIDO 2026-06-30]`** | 2026-06-30 |
| P2-2 (parcial) | Endpoints `PUT`/`GET` para gerenciar inscricao de webhook AbacatePay via API | Phase 2 | **Slice 2-C `[RESOLVIDO 2026-06-30]`** | 2026-06-30 |

Detalhes:
- Slice 2-B em `docs/audits/slice-2b-abacatepay-webhooks-report-2026-06-29.md`. Phase 2 passa a `IMPLEMENTED`. Phase 3 mantem status `IMPLEMENTING` ate webhooks de Stripe/MercadoPago serem cobertos (Phase 4).
- Slice 7-IT em `docs/audits/slice-7-it-outbox-dispatcher-e2e-report-2026-06-30.md`. Phase 7 alcancou `IMPLEMENTED` apos a Slice 7-M1 (2026-06-30).
- Slice 2-C em `docs/audits/slice-2c-abacatepay-webhook-management-report-2026-06-30.md`. Cliente HTTP real para `POST /v2/webhooks/create` continua deferred em `Slice 2-C.1`.

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
| P2-2 | Projeto de testes de integracao sem testes descobertos | Phase 1, 3, 7 | Slice 1-IT `[RESOLVIDO 2026-06-26 — 10 testes passam]`, Slice 3-IT `[RESOLVIDO 2026-06-29 — 4 testes e2e API+Postgres+AbacatePay+webhook]`, Slice 7-IT `[RESOLVIDO 2026-06-30 — 7 testes e2e do dispatcher Outbox ate delivery interno]` |
| P2-3 | Acoes administrativas sensiveis nao gravam `AuditLog` | Phase 6 | Proximo slice de Phase 6 |
| P2-4 | Integridade referencial no banco e parcial (poucas FKs) | Phase 1 | ADR-0009 (proposto) |
| P2-5 | Documentacao de arquitetura usa formato antigo de assinatura HMAC | Phase 0 | Slice documental |
| M1-security (novo) | Sweep automatico de eventos `Processing` orfaos | Phase 7 | **Slice 7-M1 `[RESOLVIDO 2026-06-30]`** |
| C.3-qa (novo) | `FOR UPDATE SKIP LOCKED` em `OutboxRepository` para multi-instancia | Phase 7 | **Slice 7-M1 `[RESOLVIDO 2026-06-30]`** |
| B4-security (novo) | Headers `X-PaymentHub-Tenant`/`X-PaymentHub-Application` nao validados | Phase 3, 7 | Deferred (HMAC ja garante autenticidade) |

---

## Proximo bloco de trabalho recomendado

### Bloco A — Seguranca e Confiabilidade (Phase 6 + Phase 7) — `CONCLUIDO 2026-06-30`

Phase 6 e Phase 7 estao `IMPLEMENTED`. Phase 6 mantem `IMPLEMENTING` ate P2-3 (AuditLog em handlers administrativos) ser fechado (escopo proprio da Phase 6; documentado em `Bloco D`). Phase 7 atingiu `IMPLEMENTED` em 2026-06-30 apos a Slice 7-M1 fechar os 2 gaps P2 proprios remanescentes (sweep automatico de `Processing` orfao e `FOR UPDATE SKIP LOCKED`). A fase NAO e `VALIDATED` enquanto Phase 6 aguarda P2-3.

```
Slice 6-A  Enforcement de TenantStatus.Active + ApplicationStatus.Active   [CONCLUIDO 2026-06-17]
Slice 7-A  Substituir NoopApplicationWebhookDispatcher por HTTP real       [CONCLUIDO 2026-06-26]
Slice 6-B  RegisterProviderAccountHandler via ITenantContext                [CONCLUIDO 2026-06-18]
Slice 6-C  Protecao de ApplicationClient.WebhookSecret em repouso          [CONCLUIDO 2026-06-25]
Slice 6-D  Politica de bootstrap/admin + AuditLog em handlers administrativos  [CONCLUIDO 2026-06-18 — politica de bootstrap; P2-3 pendente]
Slice 7-IT Suite E2E do dispatcher real ate delivery interno                 [CONCLUIDO 2026-06-30]
Slice 7-M1 Outbox multi-instancia: SKIP LOCKED + sweep de Processing orfao [CONCLUIDO 2026-06-30 — fecha M1-security + C.3-qa; Phase 7 atinge IMPLEMENTED]
```

### Bloco B — Testes de Integracao (Phase 1 + 3 + 7)

Criar primeira fixture de integracao com Testcontainers ou Docker Compose.

```
Slice 1-IT  Fixture Postgres + migrations + indices criticos + repositorios principais   [CONCLUIDO 2026-06-26]
Slice 3-IT  Testes E2E da API + Postgres + adapter AbacatePay + fakes de transporte  [CONCLUIDO 2026-06-29 — 4 testes P1 cobrindo checkout + webhook valido + idempotencia + fail-fast 401; 2 producao bugs encontrados e corrigidos (jsonb->text em webhook_events.raw_payload; _payments.AddAttemptAsync explicito no ProcessWebhookEventHandler); detalhes em docs/audits/slice-3-it-e2e-api-postgres-outbox-provider-report-2026-06-29.md]
Slice 7-IT  Suite E2E do ciclo Outbox → ApplicationClient webhook  [CONCLUIDO 2026-06-30 — 7 testes P1+P2 cobrindo Sent, HMAC, retry 500/429, UnprotectFailure, fluxo AbacatePay completo e no-redispatch de Sent; adicionou InternalsVisibleTo("PaymentHub.IntegrationTests") em PaymentHub.Worker.csproj; ApplicationWebhookCaptureHandler evoluido com EnqueueResponse + InternalWebhookHmac helper; detalhes em docs/audits/slice-7-it-outbox-dispatcher-e2e-report-2026-06-30.md]
Slice 7-M1 Outbox multi-instancia: SKIP LOCKED + sweep de Processing orfao  [CONCLUIDO 2026-06-30 — 7 testes E2E (2 OutboxDispatcherConcurrencyTests + 5 OutboxProcessingSweepTests); ClaimPendingForDispatchAsync em transacao unica com FOR UPDATE SKIP LOCKED + UPDATE atomico (nao double-dispatch entre workers); SweepOrphanedProcessingAsync via single ExecuteSqlRawAsync + ProcessingStartedAt; sane-check anti-regressao no Worker; migration 20260630184619_AddOutboxProcessingStartedAtAndIndexes; PaymentHubOptions.OutboxProcessingTimeoutSeconds=900 (default, configuravel); OutboxEvent.ProcessingStartedAt (timestamptz NULL) + WebhookDispatcherCategory.ProcessingOrphaned (8); Worker NAO chama MarkProcessing separado; suite E2E total de Phase 7 = 498 testes; detalhes em docs/audits/slice-7-m1-outbox-multi-instance-report-2026-06-30.md]  
Slice 2-C.1 Client HTTP real AbacatePay substituindo NoOp para gerenciamento de webhooks  [CONCLUIDO 2026-06-30 — 23 testes (+20 AbacatePayWebhookManagementClientTests unit + 2 ProviderAccountsWebhookControllerTests + 1 AbacatePayWebhookManagementE2ETests); 4-gate pipeline no client (provider check + feature flag + pre-flight validation + apiKey extraction via IProviderAccountCredentialsReader); named HttpClient dedicado `abacatepay-webhooks` (distinto de `abacatepay` que serve transparent-PIX); public IProviderAccountCredentialsReader em Application/Abstractions/Security/ + adapter em Infrastructure.Postgres + `<InternalsVisibleTo Include="PaymentHub.Infrastructure.Postgres" />` no PaymentHub.Application.csproj; NoOpProviderWebhookManagementClient removido; nova categoria AbacatePayErrorCategory.RegistrationDisabled = 11 (nao usada pelo client real; so placeholder); AbacatePayFakeHttpHandler estendido para rotear `/webhooks/create` e `/webhooks/list`; PaymentHubApiFactory wires named client `abacatepay-webhooks`; ProtectAbacatePayCredentials aceita `string?` para webhookSecret; suite E2E total de Phase 2 = 522 testes; detalhes em docs/audits/slice-2c1-abacatepay-webhook-management-client-report-2026-06-30.md]  
```

### Bloco C — Provider AbacatePay (Phase 2) — `CONCLUIDO 2026-06-29`

Ativar primeiro provider real apos seguranca e confiabilidade estarem solidas.

```
Slice 2-A  Adapter AbacatePay funcional + validacao de assinatura webhook   [CONCLUIDO 2026-06-27 — Checkout Transparente PIX sandbox; webhooks externos/HMAC em Slice 2-B]
Slice 2-B  Webhooks externos AbacatePay + normalizacao de eventos          [CONCLUIDO 2026-06-29 — HMAC-SHA256 Base64, fail-fast no controller, roteamento por metadata, 78 testes novos; detalhes em docs/audits/slice-2b-abacatepay-webhooks-report-2026-06-29.md]
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
| Testes unitarios passando | 489 | >= 64 |
| Testes de integracao (Postgres real) | 33 (10 Slice 1-IT + 4 Slice 3-IT + 3 Slice 2-C + 1 Slice 2-C.1 E2E + 7 Slice 7-IT + 2 Slice 7-M1 concurrency + 5 Slice 7-M1 sweep + 1 vazio Slice 2-C.1 test stub) | >= 5 |
| Gaps P0 abertos | 0 | 0 |
| Gaps P1 abertos | **0** | 0 |
| Gaps P2 abertos | 4 (P2-3 audit log pendente Phase 6; B4-security headers deferred; P2-4 FKs pendente; P2-5 doc HMAC desatualizada pendente; M1-security + C.3-qa RESOLVIDO 2026-06-30 via Slice 7-M1; P2-2 RESOLVIDO 2026-06-30 via Slice 7-IT; P2-1 RESOLVIDO 2026-06-30 via Slice 2-C.1) | <= 2 |
| Build status | Limpo | Limpo |
| Providers reais funcionais | 1 (AbacatePay PIX sandbox + webhooks externos + remote registration) | >= 1 (AbacatePay) |

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
- `docs/audits/slice-2b-abacatepay-webhooks-report-2026-06-29.md`
- `docs/audits/slice-2c-abacatepay-webhook-management-report-2026-06-30.md`
- `docs/audits/slice-7-it-outbox-dispatcher-e2e-report-2026-06-30.md`
- `docs/audits/slice-7-m1-outbox-multi-instance-report-2026-06-30.md`
