# Slice 1-IT — Postgres Integration Tests Foundation Report

Data: 2026-06-26
Phase: 1 + 3 + 7 (base para Bloco B — Testes de Integracao)
Specs relacionadas: `docs/specs/013-testing-strategy.md`, `docs/specs/007-inbox-outbox-workers.md`, `docs/specs/011-security-and-compliance.md`
ADRs consultadas: `ADR-0002-use-postgres-inbox-outbox-in-mvp.md`, `ADR-0007-webhook-secret-protection.md`, `ADR-0010-real-outbox-dispatcher-location.md`
Gap enderecado: **P2-2** do `docs/roadmap/002-phase-status-board.md` (projeto de testes de integracao sem testes descobertos).

## Resumo

O projeto `tests/PaymentHub.IntegrationTests/` existia como casca vazia (`PaymentHub.IntegrationTests.csproj` + artefatos de uma tentativa anterior sem codigo). Este slice preencheu a casca com:

- Fixture xUnit compartilhando um container `postgres:16-alpine` real por run via **Testcontainers.PostgreSql 4.12.0**.
- DI minimo manual em `IntegrationTestFactory` (sem `AddPaymentHubPostgres`) com `PaymentHubDbContext`, repositorios principais, `IOutboxPublisher`/`IOutboxRepository`/`IOutboxEventStore`, `IWebhookSecretProtector`/`ICredentialProtector`/`IApiKeyHasher`/`IWebhookSigner`/`IIdempotencyRequestHasher`/`IClock`.
- 10 testes cobrindo migrations, persistence de Tenant/ApplicationClient/ProviderAccount, ciclo de vida do OutboxEvent (Pending -> Processing -> Sent + retry seguro) e filtro de `GetPendingForDispatchAsync`.

Suite previa: 281 testes unitarios. Suite nova: **291 testes** (+10 de integracao). Build limpo (0 errors / 0 warnings em 9 projetos). `scripts/agent-architecture-check.sh` e `scripts/agent-docs-check.sh` passaram. `git diff --check` limpo.

## Questoes decididas (Q1-Q5 do planner)

| # | Questao | Decisao | Justificativa |
|---|---------|---------|---------------|
| **Q1** | Ferramenta | **Testcontainers.PostgreSql 4.12.0** | Compat .NET 10 (target `net8.0` mas compativel com versao superior); maduro; reusa `Docker.Engine` ja presente no ambiente; sem dependencia de Docker Compose ou pipeline customizado. |
| **Q2** | Isolamento entre testes | **Collection fixture compartilhando 1 container por run + `TRUNCATE ... CASCADE` em ordem topologica reversa entre testes** | Schema/migrations preservados entre testes; ~10ms de `TRUNCATE` por teste; muito mais rapido que database-per-test (+~10s) ou schema-per-test (+complexidade). `CASCADE` cobre FKs sem erro de dependencia (ex.: `payments -> tenants`, `payment_attempts -> payments`). |
| **Q3** | Suite principal vs opt-in | **Opt-in via `[Trait("Category", "Integration")]`** | `dotnet test PaymentHub.slnx` nao roda integracao por padrao. CI sem Docker nao quebra. Suite unitaria continua rapida (281 testes em ~11s). Integracao roda com `dotnet test tests/PaymentHub.IntegrationTests/PaymentHub.IntegrationTests.csproj` (~8-32s incluindo container startup). |
| **Q4** | DI na fixture | **Manual minimo** (`ServiceCollection` + registros escopados) | `AddPaymentHubPostgres` puxa `IHttpClientFactory`/`HttpApplicationWebhookDispatcher`/`HmacApiKeyHasher` que nao sao exercitados; reduzir superficie de bootstrap reduz tempo de teste e elimina a necessidade de configurar `IHttpClientFactory` no host de testes. |
| **Q5** | API `appsettings.json` parity | **NAO corrigir** (deferred) | Briefing limita escopo a fixture/migrations. Mesmo gap residual existe desde o Slice 7-A.6; paridade API/Worker continua pendente para slice proprio. Documentado como gap residual. |

## Arquivos criados

| Arquivo | Linhas | Proposito |
|---------|--------|-----------|
| `tests/PaymentHub.IntegrationTests/Infrastructure/PostgresCollection.cs` | 13 | `[CollectionDefinition("Postgres")]` + `ICollectionFixture<PostgresFixture>` para compartilhar 1 container por run. |
| `tests/PaymentHub.IntegrationTests/Infrastructure/PostgresFixture.cs` | 63 | `IAsyncLifetime` que sobe `PostgresBuilder("postgres:16-alpine")`, aplica migrations via `MigrateAsync()`, expoe `ConnectionString` + `BuildContext()`. |
| `tests/PaymentHub.IntegrationTests/Infrastructure/IntegrationTestFactory.cs` | 240 | DI minimo manual; helpers `SeedTenantAsync`/`SeedApplicationClientAsync`/`SeedProviderAccountAsync`/`EnqueueOutboxAsync`/`ResetDatabaseAsync` (TRUNCATE CASCADE). |
| `tests/PaymentHub.IntegrationTests/Migrations/MigrationSmokeTests.cs` | 65 | `Migrations_ShouldApplySuccessfully_OnEmptyPostgresDatabase`: `MigrateAsync` idempotente + `INFORMATION_SCHEMA` valida 10 tabelas. |
| `tests/PaymentHub.IntegrationTests/Persistence/TenantApplicationClientPersistenceTests.cs` | 95 | 2 testes: roundtrip Tenant/ApplicationClient + unique index `tenants.slug`. |
| `tests/PaymentHub.IntegrationTests/Persistence/ApplicationClientWebhookSecretTests.cs` | 99 | 2 testes: `Protect`+reload+`Unprotect` roundtrip + `HasWebhookSecret=false`. |
| `tests/PaymentHub.IntegrationTests/Persistence/ProviderAccountPersistenceTests.cs` | 108 | 2 testes: `GetDefaultAsync`/`GetByCodeAsync` + escopo tenant. |
| `tests/PaymentHub.IntegrationTests/Persistence/OutboxPersistenceTests.cs` | 117 | 2 testes: transicoes `Pending -> Processing -> Sent` + retry seguro `MarkRetryWithCategory`. |
| `tests/PaymentHub.IntegrationTests/Persistence/OutboxPendingQueryTests.cs` | 137 | 1 teste (5 estados): `Pending + NextRetryAt null` (devolvido), `Pending + NextRetryAt past` (devolvido), `Pending + NextRetryAt future` (NAO), `Processing` (NAO), `Sent` (NAO), `Failed` (NAO). Documenta gap de `Processing` orfao. |
| `docs/audits/slice-1-it-postgres-integration-tests-report-2026-06-26.md` | este | Relatorio final. |

## Arquivos alterados

| Arquivo | Mudanca |
|---------|---------|
| `tests/PaymentHub.IntegrationTests/PaymentHub.IntegrationTests.csproj` | Adicionado `Testcontainers.PostgreSql 4.12.0`, `Npgsql 10.0.0`, `Microsoft.EntityFrameworkCore.Relational 10.0.0`, `Microsoft.Extensions.DependencyInjection/Logging/Options 10.0.0`. Removido `Microsoft.AspNetCore.Mvc.Testing 10.0.0` (nao usado). Adicionado `<IsTestProject>true</IsTestProject>`. |
| `docs/harness/validation-matrix.md` | Adicionadas 11 linhas Phase 7 Slice 1-IT + Phase 6 + Phase 1 (migrations + repos + outbox). |
| `docs/roadmap/001-development-timeline.md` | Slice 1-IT marcado `[CONCLUIDO 2026-06-26]` no Bloco B; linha de Phase 7 referencia a entrega da base de integracao. |
| `docs/roadmap/002-phase-status-board.md` | P2-2 marcado `[PARCIALMENTE RESOLVIDO 2026-06-26]`; Phase 7 com 0 gaps P1 proprios + base de integracao entregue; indicador "Testes de integracao" sai de 0 para 10; Bloco B mostra Slice 1-IT `[CONCLUIDO 2026-06-26]`; arquivo de relatorio novo referenciado. |
| `docs/harness/learnings.md` | Entrada nova (1-IT) com 10 decisoes reaproveitaveis (collection fixture + TRUNCATE, opt-in trait, `IAsyncDisposable` gotcha, DI manual, migrations-assembly explicito, TRUNCATE CASCADE ordem, chaves de 32+ bytes, sem `AddPaymentHubPostgres`, scopes explicitos, gap Processing documentado). |
| `agent-progress.md` | Entrada Slice 1-IT movida para `## Historico` como sub-seccao datada 2026-06-26. |

## Validacoes executadas

| Comando | Resultado |
|---------|-----------|
| `git status --short` antes de comecar | Limpo (HEAD = `9059f1f`). |
| `git status --short` ao terminar | Apenas arquivos novos/alterados do slice. |
| `dotnet restore PaymentHub.slnx` | OK. |
| `dotnet build PaymentHub.slnx` | **0 errors / 0 warnings** em 9 projetos. |
| `dotnet test PaymentHub.slnx --no-build` | **291 testes passaram** (281 baseline + 10 novos) em ~11s; sem regressao. |
| `dotnet test tests/PaymentHub.IntegrationTests/PaymentHub.IntegrationTests.csproj --no-build` | **10/10 passaram** em 7.6-32s (inclui startup do container). |
| `scripts/agent-architecture-check.sh` | Passed (IntegrationTests continua respeitando camadas; Worker continua sem depender de Api). |
| `scripts/agent-docs-check.sh` | Passed. |
| `git diff --check` | Limpo. |

## Lista nominal de testes de integracao

TRX gerado por `dotnet test --logger 'trx;LogFileName=...'`:

1. `PaymentHub.IntegrationTests.Migrations.MigrationSmokeTests.Migrations_ShouldApplySuccessfully_OnEmptyPostgresDatabase`
2. `PaymentHub.IntegrationTests.Persistence.TenantApplicationClientPersistenceTests.DbContext_ShouldPersistTenantAndApplicationClient_AndReloadCorrectly`
3. `PaymentHub.IntegrationTests.Persistence.TenantApplicationClientPersistenceTests.Tenant_AndApplication_UniqueIndex_ShouldPreventDuplicateSlug`
4. `PaymentHub.IntegrationTests.Persistence.ApplicationClientWebhookSecretTests.ApplicationClient_ShouldPersistProtectedWebhookSecret_AndAllowInternalUnprotect`
5. `PaymentHub.IntegrationTests.Persistence.ApplicationClientWebhookSecretTests.ApplicationClient_WithoutWebhookSecret_ShouldReportHasWebhookSecretFalse`
6. `PaymentHub.IntegrationTests.Persistence.ProviderAccountPersistenceTests.ProviderAccountRepository_ShouldPersistAndLoadByTenantAndApplication`
7. `PaymentHub.IntegrationTests.Persistence.ProviderAccountRepositoryTests.ProviderAccountRepository_ShouldRespectsTenantScope`
8. `PaymentHub.IntegrationTests.Persistence.OutboxPersistenceTests.OutboxEvent_ShouldPersistPendingProcessingAndSentStates`
9. `PaymentHub.IntegrationTests.Persistence.OutboxPersistenceTests.OutboxEvent_SafeRetry_ShouldPersistCategoryWithoutExceptionMessage`
10. `PaymentHub.IntegrationTests.Persistence.OutboxPendingQueryTests.OutboxRepository_ShouldReturnOnlyDispatchablePendingEvents`

Todos trait `[Category, Integration]` (opt-in via `--filter Category=Integration` ou executando direto o projeto).

## Criterios de aceite (briefing original)

| # | Criterio | Status |
|---|---------|--------|
| 1 | Base real de testes de integracao com Postgres (Testcontainers PostgreSql sobe container real por run) | ✅ `PostgresFixture` sobe `postgres:16-alpine` real por run. |
| 2 | Migrations aplicadas em banco real via `MigrateAsync()` (sem exceptions) | ✅ `MigrationSmokeTests` chama `MigrateAsync` (no-op na segunda chamada) + `INFORMATION_SCHEMA`. |
| 3 | >= 4 testes de integracao uteis | ✅ 10 testes. |
| 4 | Tenant/ApplicationClient persistidos e recarregados com ids/status corretos | ✅ `TenantApplicationClientPersistenceTests`. |
| 5 | WebhookSecret protegido validado em integracao | ✅ `ApplicationClientWebhookSecretTests`. |
| 6 | ProviderAccount **e** Outbox repository validados em integracao | ✅ `ProviderAccountPersistenceTests` + `OutboxPersistenceTests` + `OutboxPendingQueryTests`. |
| 7 | Outbox pending/final state validado | ✅ `OutboxPersistenceTests` (Pending/Processing/Sent + retry) + `OutboxPendingQueryTests` (5 estados). |
| 8 | 281 testes unitarios existentes continuam passando | ✅ 291 = 281 + 10 (zero regressao). |
| 9 | Build limpo (0 errors / 0 warnings) | ✅ `dotnet build PaymentHub.slnx` 0/0. |
| 10 | `scripts/agent-architecture-check.sh` passa | ✅ Passed. |
| 11 | Documentacao minima atualizada (validation-matrix, roadmap 001, roadmap 002, agent-progress) | ✅ Todos atualizados. |
| 12 | Relatorio `docs/audits/slice-1-it-postgres-integration-tests-report-2026-06-26.md` criado | ✅ Este arquivo. |
| 13 | Nenhum provider real implementado | ✅ Slice nao toca `PaymentHub.Infrastructure.Providers`/`AbacatePay*`. |

## Decisoes tecnicas detalhadas

### 1. `ICollectionFixture<PostgresFixture>` em vez de `IClassFixture`

Compartilhar 1 container por run inteira (e nao 1 container por classe de teste) reduz o tempo de suite em ~10-30s. Cada teste TRUNCA as tabelas entre execucoes (~10ms cada) preservando o schema/migrations. `IClassFixture` foi descartado por multiplicar containers sem necessidade.

### 2. `TRUNCATE ... RESTART IDENTITY CASCADE`

Ordem topologica reversa (filhos antes de pais): `payment_attempts, payments, webhook_events, outbox_events, idempotency_keys, api_keys, provider_accounts, audit_logs, application_clients, tenants`. `CASCADE` cobre FKs mesmo que a ordem nao seja estrita; `RESTART IDENTITY` zera sequences (postgres nao usa IDENTITY para PKs Guid, mas e defensivo).

### 3. DI manual minimo

`AddPaymentHubPostgres` registra:
- `IHttpClientFactory` (nao exercitado pelos testes do slice);
- `HttpApplicationWebhookDispatcher` (idem);
- `HmacApiKeyHasher` (exigido por ter `IOptions<PaymentHubOptions>` mas usado apenas se algo chamar — registrado manualmente na fixture).

A fixture monta apenas: `PaymentHubDbContext` (Scoped), repos (Scoped), `IOutboxPublisher`/`IOutboxEventStore` (Scoped), crypto/services (Singleton). Reduz superficie e tempo de bootstrap do provider.

### 4. `MigrationsAssembly(typeof(PaymentHubDbContext).Assembly.GetName().Name)`

Garantia explicita de que EF Core sabe que as migrations vivem em `PaymentHub.Infrastructure.Postgres` (mesma assembly do `PaymentHubDbContext`). Sem isso, o padrao do EF Core procura uma pasta `Migrations/` no assembly de chamada.

### 5. Chaves de cifra com >= 32 bytes

`AesWebhookSecretProtector` e `AesCredentialProtector` fazem `PadRight(32, '0')` se a chave for curta, mas a fixture usa valores explicitos >= 32 bytes para evitar qualquer dependencia de padding deterministico:
- `WebhookSecretEncryptionKey = "integration-test-webhook-secret-key-32+chars!"` (44 chars).
- `CredentialEncryptionKey = "integration-test-credential-encryption-key-32+"` (44 chars).
- `ApiKeyHashSecret = "integration-test-api-key-hash-secret-32+chars!"` (44 chars).

Nenhum valor real. Nenhum secret produtivo em risco.

### 6. Trait `[Trait("Category", "Integration")]` em todas as classes de teste

Permite rodar a suite unitaria completa sem `dotnet test PaymentHub.slnx` tentar subir container. Para rodar somente integracao: `dotnet test tests/PaymentHub.IntegrationTests/PaymentHub.IntegrationTests.csproj`. Para rodar tudo (unit + integration): `dotnet test PaymentHub.slnx` (CI com Docker) ou usar `--filter "Category!=Integration"` para suite rapida.

### 7. `await using` em DbContext/DbCommand/DbDataReader (nao em IServiceScope)

Pequeno gotcha do .NET: `IServiceScope` nao implementa `IAsyncDisposable`. `DbContext`, `DbCommand` e `DbDataReader` implementam. Fixture e testes usam `await using` para os primeiros (dispose async + rollback) e `using` (sincrono) para `IServiceScope`. Documentado em `learnings.md`.

### 8. Documentacao explicita do gap de `Processing` orfao

`OutboxPendingQueryTests` valida que `Processing` NAO e devolvido por `GetPendingForDispatchAsync`. O teste documenta inline o gap (sweep automatico + `FOR UPDATE SKIP LOCKED` para multi-instancia) que sera enderecado em slice proprio quando Phase 7 multi-instancia for iniciada. Fonte: `ADR-0010-real-outbox-dispatcher-location.md` e `docs/specs/007-inbox-outbox-workers.md`.

## Riscos residuais (deferidos, fora deste slice)

- **R1 (medio)** — API `appsettings.json` ainda sem `PaymentHub` placeholder (paridade com Worker). Q5 do planner decidiu NAO corrigir aqui. Risco: container da API pode falhar no startup se algum codigo da API consumir `IOptions<PaymentHubOptions>` sem valor default. Mitigacao: registrar slice proprio.
- **R2 (medio)** — CI atual nao tem Docker obrigatorio. Mitigacao: trait `Category=Integration` + filtro explicito.
- **R3 (baixo)** — Suite dependente de Docker daemon ativo (~6-10s para subir `postgres:16-alpine`). Documentado em `docs/harness/validation.md`.
- **R4 (baixo)** — Sweep automatico de `Processing` orfao (M1-security) nao coberto. Sera necessario para Phase 7 multi-instancia.
- **R5 (baixo)** — `FOR UPDATE SKIP LOCKED` em `OutboxRepository` (C.3-qa) nao coberto. Sera necessario para multi-instancia.
- **R6 (baixo)** — Testes end-to-end API + Worker + Postgres (ex.: `WebApplicationFactory` + `OutboxDispatcherWorker` consumindo eventos reais) nao cobertos. Slice 3-IT (middleware/checkout/idempotencia) e Slice 7-IT (workers inbox/outbox) cobrem.

## Aprendizados consolidados

Ver `docs/harness/learnings.md`, entrada `2026-06-26 - Slice 1-IT Testcontainers + collection fixture + TRUNCATE CASCADE`. Decisoes reaproveitaveis em futuros slices de integracao ou em outros projetos .NET 10.

## Proximo slice recomendado

**Slice 2-A — AbacatePay sandbox funcional** (Phase 2). Slice 1-IT entregou a base para integracao, mas o proximo passo natural do roadmap continua sendo provider real. Slice 3-IT (middleware/checkout/idempotencia com WebApplicationFactory) e Slice 7-IT (workers inbox/outbox com banco real) podem ser planejados em sprints subsequentes.

## Arquivos relacionados

- `tests/PaymentHub.IntegrationTests/`
- `docs/harness/validation-matrix.md` (entradas Slice 1-IT)
- `docs/roadmap/001-development-timeline.md` (Bloco B)
- `docs/roadmap/002-phase-status-board.md` (P2-2)
- `docs/specs/013-testing-strategy.md`
- `docs/specs/007-inbox-outbox-workers.md`
- `docs/adr/ADR-0002-use-postgres-inbox-outbox-in-mvp.md`
- `docs/adr/ADR-0007-webhook-secret-protection.md`
- `docs/adr/ADR-0010-real-outbox-dispatcher-location.md`
- `docs/harness/learnings.md` (entrada 2026-06-26 Slice 1-IT)
- `agent-progress.md` (entrada movida para `## Historico`)
