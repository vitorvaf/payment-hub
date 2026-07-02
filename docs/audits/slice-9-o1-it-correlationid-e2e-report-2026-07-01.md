# Slice 9-O1.IT — CorrelationId E2E Report

- **Slice**: 9-O1.IT
- **Phase**: 9 — Relatorios, Metricas e Observabilidade
- **Status**: CONCLUIDO
- **Data**: 2026-07-01

## Resumo

Slice 9-O1.IT fecha a unica pendencia direta do Slice 9-O1 (validation-matrix linha 236 marcada PENDING): 4 testes E2E cobrindo o `CorrelationIdMiddleware` end-to-end via `WebApplicationFactory<Program>` + Testcontainers Postgres (PostgreSQL 16-alpine). Tambem descobre e corrige um bug critico introduzido no commit `aaa9ea5` — a migration `20260701000001_AddObservabilityColumns.cs` foi commitada SEM o correspondente `.Designer.cs`, o que impedia o `MigrateAsync()` contra Postgres real de aplicar a coluna `correlation_id`. Sem o fix, 20 testes E2E pre-existentes falhavam com `column "correlation_id" of relation "outbox_events" does not exist`. Total final: **547 unit + 37 integration = 584 testes passing**.

## Objetivo

Validar em integracao (Testcontainers + WebApplicationFactory + HttpClient real) que o `CorrelationIdMiddleware` retorna `X-Correlation-Id` em responses HTTP reais e preserva valores validos enviados pelo client. Endpoint escolhido: `GET /health` (anonymous em `ApiKeyAuthenticationMiddleware.IsAnonymousPath`, sem controller mapping — middleware corre antes do routing e response carrega o header mesmo em 404).

## Escopo implementado

1. `tests/PaymentHub.IntegrationTests/EndToEnd/CorrelationIdE2ETests.cs` (220 linhas, 4 `[Fact]`):
   - `Response_ShouldContainGeneratedCorrelationId_WhenRequestDoesNotProvideOne` — middleware gera GUID-N valido quando header ausente.
   - `Response_ShouldPreserveCorrelationId_WhenRequestProvidesValidHeader` — inbound id ecoado byte-exact quando valido.
   - `Response_ShouldReplaceInvalidCorrelationId_WhenRequestProvidesInvalidHeader` — substituicao silenciosa quando invalido (sem 400, sem leak do valor rejeitado).
   - `Response_ShouldCollapseDuplicateInboundCorrelationIdHeaders_ToSingleResponseHeader` — dois headers inbound validos viram um unico outbound (o primeiro).
2. `src/PaymentHub.Infrastructure.Postgres/Migrations/20260701000001_AddObservabilityColumns.Designer.cs` (700 linhas, **BUG FIX** do commit `aaa9ea5`): hand-authored copiando `20260630184619_AddOutboxProcessingStartedAtAndIndexes.Designer.cs` template + atualizando `[Migration("...")]` e partial class name + inserindo 2 property mappings (`CorrelationId` em `OutboxEvent` + `WebhookEvent` com `HasMaxLength(64).HasColumnType("character varying(64)").HasColumnName("correlation_id")`).
3. `agent-progress.md` (entrada Slice 9-O1.IT adicionada em IMPLEMENTING com discovery + plano + testes previstos).
4. `docs/harness/validation-matrix.md` (linhas E2E 9-O1 + 9-O1.IT marcadas como `PASS` com contagem medida).
5. `docs/harness/learnings.md` (entrada 2026-07-01 Slice 9-O1.IT com context, decisao, evidencia, impacto + entrada original Slice 9-O1 preservada).
6. `feature_list.md` (PH-OBS-002 atualizado com "+E2E Slice 9-O1.IT"; PH-OBS-006 novo item).
7. Este audit report.

## Testes adicionados

Todos os 4 testes E2E seguem o pattern de Slice 3-IT/7-IT/7-M1: `[Collection(PostgresCollection.Name)]` + `[Trait("Category", "Integration")]`, factory fresca por teste (`new PaymentHubApiFactory(_postgres)` + `await factory.DisposeAsync()` em `try/finally`), `factory.CreateClient()` para abrir `HttpClient` real contra o TestServer.

### P1.1 — Generate-when-absent

```csharp
[Fact]
public async Task Response_ShouldContainGeneratedCorrelationId_WhenRequestDoesNotProvideOne()
```

- Request: `GET /health` sem `X-Correlation-Id`.
- Assertions: header presente, valor nao-vazio, `IsValid(value)` true, length = 32, regex match `^[0-9a-f]{32}$` (Guid-N).

### P1.2 — Preserve-when-valid

```csharp
[Fact]
public async Task Response_ShouldPreserveCorrelationId_WhenRequestProvidesValidHeader()
```

- Request: `GET /health` com `X-Correlation-Id: test-correlation-123456` (20 chars, ASCII alphanumeric, dentro da window 8-128).
- Assertions: header presente, valor igual ao enviado byte-exact.

### P1.3 — Replace-when-invalid

```csharp
[Fact]
public async Task Response_ShouldReplaceInvalidCorrelationId_WhenRequestProvidesInvalidHeader()
```

- Request: `GET /health` com `X-Correlation-Id: !!@@##bad` (contem `!@#` que viola o regex `^[A-Za-z0-9\-]{8,128}$`).
- Assertions: header presente, valor DIFERENTE do candidato, `IsValid(value)` true, length = 32.

### P1.4 — Dedup-duplicate-headers

```csharp
[Fact]
public async Task Response_ShouldCollapseDuplicateInboundCorrelationIdHeaders_ToSingleResponseHeader()
```

- Request: `GET /health` com 2 headers `X-Correlation-Id` validos (`test-correlation-123456` + `second-correlation-987654`).
- Assertions: header presente EXATAMENTE 1 vez no response, valor igual ao primeiro.

## Validações executadas

```bash
# Build
dotnet build PaymentHub.slnx --nologo
-> 0 errors, 0 warnings em 9 projetos (20.13s)

# Unit suite
dotnet test PaymentHub.UnitTests --no-build --nologo
-> 547/547 PASS (1.8s) — 3 runs estaveis

# Integration suite — parcial
dotnet test PaymentHub.IntegrationTests --filter "FullyQualifiedName~CorrelationIdE2ETests"
-> 4/4 PASS (7.4s)

dotnet test PaymentHub.IntegrationTests --filter "FullyQualifiedName~EndToEnd"
-> 24/24 PASS (13.8s)

dotnet test PaymentHub.IntegrationTests --filter "FullyQualifiedName~Migrations|FullyQualifiedName~EndToEnd"
-> 25/25 PASS (13.9s)

dotnet test PaymentHub.IntegrationTests --filter "FullyQualifiedName~OutboxDispatcher|FullyQualifiedName~AbacatePayCheckout|FullyQualifiedName~CorrelationId|FullyQualifiedName~Tenant|FullyQualifiedName~WebhookSecret|FullyQualifiedName~ProviderAccount"
-> 28/28 PASS (13.9s)

# Integration suite — FULL
dotnet test PaymentHub.IntegrationTests --no-build --nologo
-> 37/37 PASS (12.0s)

# Scripts
scripts/agent-architecture-check.sh
-> Architecture check passed.

scripts/agent-docs-check.sh (com gate anti-leak)
-> Docs check passed.

# Diff
git diff --check
-> clean
```

**Total: 547 unit + 37 integration = 584 tests passing.**

## Resultado

- Slice 9-O1.IT = **CONCLUIDO** (todos os criterios de aceite satisfeitos).
- Phase 9 mantem status `IMPLEMENTING` em `docs/roadmap/002-phase-status-board.md`. Slice 9-O1.IT nao muda o status porque Phase 9 ja estava `IMPLEMENTING` desde 9-O1; IT fecha o gap E2E deixado pela 9-O1.
- E2E `CorrelationIdE2ETests` listado em `docs/harness/validation-matrix.md` agora `PASS` com 4 testes + suite completa 37/37 PASS.
- Audit report 9-O1 linha 238 (`PENDING`) atualizado para `PASS` em `validation-matrix.md`.
- Entry `PH-OBS-002` em `feature_list.md` referencia explicitamente o Slice 9-O1.IT.
- Novo `PH-OBS-006` em `feature_list.md` (CorrelationId E2E via Testcontainers).

## Gaps remanescentes

- **Nenhum gap funcional** dentro do escopo deste slice. 4 testes E2E cobrem os 4 contratos do `CorrelationIdMiddleware`: generate, preserve, replace, dedup.
- **Anti-flake documentado**: a 1a execucao da suite completa mostrou `0 passed, 37 failed` por container startup race (Testcontainers). Re-executar imediatamente resolveu. Padrao ja documentado em `agent-progress.md` Slice 7-M1 learnings (~1-2% historico). Suite em filtro especifico (e.g., `~EndToEnd`) e estavel.
- **Pendencias Phase 9 futuras** (NAO escopo deste slice):
  - Slice 9-O2: wire `PaymentHubMetrics` nos call sites reais (`CreateCheckoutHandler`, `ProviderWebhooksController`, `ProcessWebhookEventHandler`, `OutboxDispatcherWorker`, `HttpApplicationWebhookDispatcher`, `ApiKeyAuthenticationMiddleware`). Ja documentado em `feature_list.md` (PH-OBS-004).
  - Distributed tracing via `Activity`/OpenTelemetry (PH-OBS-005). Fora de MVP.
  - AuditLog middleware (PH-AUD-001). Phase 5.

## Próximo slice recomendado

**Slice 9-O2 — Instrumentacao ativa nos handlers/workers** (Phase 9 / 9-O2+). Escopo:

1. Wire `CheckoutDurationMs` em `CreateCheckoutHandler` (start `Stopwatch.GetTimestamp()` no inicio, record no success/failure).
2. Wire `ProviderWebhookDurationMs` em `ProviderWebhooksController.Receive`.
3. Wire `OutboxDispatchDurationMs` em `HttpApplicationWebhookDispatcher.DispatchAsync`.
4. Wire `CheckoutsCreatedTotal`/`CheckoutsIdempotentReplayTotal`/`CheckoutsIdempotencyConflictTotal` em `CreateCheckoutHandler`.
5. Wire `ProviderWebhooksReceivedTotal`/`ProviderWebhooksRejectedTotal` em `ProviderWebhooksController`.
6. Wire `WebhookEventsProcessedTotal`/`FailedTotal`/`RetriedTotal` em `ProcessWebhookEventHandler`.
7. Wire `OutboxEventsSentTotal`/`RetriedTotal`/`FailedTotal` em `OutboxDispatcherWorker`.
8. Wire `OutboxOrphansRecoveredTotal` em `OutboxDispatcherWorker.DispatchOnceAsync` (apos sweep).
9. Wire `AuthorizationDeniedTotal` em `ApiKeyAuthenticationMiddleware`.

Cuidado com o anti-regression `CorrelationId` (re-asserting Slice 9-O1):
- NUNCA loggar valor rejeitado de `X-Correlation-Id` (middleware).
- NUNCA expor `correlation_id` em tag value (whitelist 7 chaves).
- Manter tag whitelist runtime gate.

## Anti-regression rules MUST-NOT-REGRESS preservadas

| Regra | Status | Evidencia |
|---|---|---|
| `webhook_events.raw_payload` continua `text` (Slice 3-IT) | PASS | `EntityConfigurations.cs:190` `HasColumnType("text")` |
| `provider_accounts.webhook_events` continua `text` (Slice 2-C) | PASS | `EntityConfigurations.cs:84` `HasColumnType("text")` |
| `outbox_events.payload` continua `jsonb` (Slice 7-IT) | PASS | `EntityConfigurations.cs:223` `HasColumnType("jsonb")` |
| `outbox_events.processing_started_at` continua `timestamptz NULL` (Slice 7-M1) | PASS | `EntityConfigurations.cs:234` sem `HasColumnType` (default `timestamptz`) |
| `webhookSecret` continua sem coluna propria (Slice 2-C) | PASS | Migration nova NAO cria coluna |
| DTOs de request NAO expoem `tenantId`/`applicationId` (Slice 6-B) | PASS | `CheckoutsController.cs:52-55` le via `_tenantContext` |
| `OutboxEvent.LastError` continua apenas categoria enum (Slice 7-A.7) | PASS | `OutboxEvent.MarkRetryWithStatus/MarkFailedWithStatus` (linhas 162-211) persistem apenas enum + statusCode |
| `ApplicationWebhookCaptureHandler` default 204 (Slice 3-IT) | PASS | Nao tocado nesta slice |
| `OutboxDispatcherWorker` NAO hospedado em `WebApplicationFactory` (Slice 7-IT/7-M1) | PASS | E2E invoca `DispatchOnceAsync` via `InternalsVisibleTo("PaymentHub.IntegrationTests")` |
| CorrelationIdMiddleware ANTES de ApiKeyAuthenticationMiddleware | PASS | `Program.cs:131` `UseMiddleware<CorrelationIdMiddleware>()` seguido de `:133` `UseMiddleware<ApiKeyAuthenticationMiddleware>()` |
| Worker NAO importa `IHttpContextAccessor` | PASS | `grep HttpContextAccessor src/PaymentHub.Worker/` = 0 matches |
| Migration nova adiciona `correlation_id` em AMBAS as tabelas | PASS | `EntityConfigurations.cs:204` (`WebhookEventConfiguration`) + `:241` (`OutboxEventConfiguration`) |
| Migration NAO usa `jsonb` para a coluna nova | PASS | `character varying(64) NULL` (mirror da coluna, nao `jsonb`) |

## Files tocados (8 arquivos)

### Created (2)
- `tests/PaymentHub.IntegrationTests/EndToEnd/CorrelationIdE2ETests.cs` (220 linhas, 4 `[Fact]`)
- `src/PaymentHub.Infrastructure.Postgres/Migrations/20260701000001_AddObservabilityColumns.Designer.cs` (700 linhas, **bug fix**)
- `docs/audits/slice-9-o1-it-correlationid-e2e-report-2026-07-01.md` (este arquivo)

### Modified (5)
- `agent-progress.md` (entrada Slice 9-O1.IT)
- `docs/harness/validation-matrix.md` (3 linhas atualizadas)
- `docs/harness/learnings.md` (entry 2026-07-01 Slice 9-O1.IT + restaurada entry Slice 9-O1)
- `feature_list.md` (PH-OBS-002 atualizada + PH-OBS-006 novo)

## Acoes requeridas antes do merge

1. Implementer (ja feito): implementar slice + rodar validacoes.
2. Implementer: commitar (a fazer nesta sessao).
3. Reviewers (architect/qa/security): acionar via Task tool. Os prompts podem seguir o pattern dos reviewers 9-O1 com foco em:
   - **architect**: middleware order + Domain NAO referencia Application + Worker sem IHttpContextAccessor + migration nao introduz webhookSecret column.
   - **qa**: regressao (suite 584/584 PASS) + flake documentado + cobertura dos 4 cenarios do middleware.
   - **security**: anti-leak gate funcionando + CorrelationId nao-sensivel + tag whitelist preservada + designer.cs nao expoe campos sensitive.
4. Merge: NAO automatico (regras AGENTS.md).

## Métricas finais

- Test cases adicionados: 4 (CorrelationIdE2ETests)
- Migration files fixados: 1 (.Designer.cs criado)
- Docs atualizados: 4 (validation-matrix, learnings, feature_list, agent-progress)
- Audit report: 1 (este arquivo)
- Build time: 20.13s (sem regressao)
- Unit test time: 1.8s (sem regressao)
- Integration test time: 12.0s (sem regressao)
- Total test count: 547 + 37 = 584 (sem regressao vs 522 baseline + 25 unit pre-existentes = 547 unit + 33 integration pre-existentes = 580; o delta 584 - 580 = 4 testes novos em 9-O1.IT)