# Agent Progress

Use este arquivo para tarefas com mais de um passo. Mantenha entradas curtas e verificaveis.

## Entrada atual

Nenhuma entrada em aberto. Slice 1-IT foi concluido em 2026-06-26 (ver `## Historico`). Aguardando atribuicao do proximo slice (recomendado: **Slice 2-A — AbacatePay sandbox funcional**).

- Status do git ao fim: working tree com 11 arquivos novos/alterados do Slice 1-IT (nao commitado nesta sessao).
- Suite final: **291 testes passando** (281 baseline unitarios + 10 integracao novos), build 0 errors / 0 warnings.
- Proximos passos conhecidos (sem compromisso): `Slice 2-A` (Phase 2), `Slice 3-IT` (middleware/checkout com `WebApplicationFactory`), `Slice 7-IT` (workers inbox/outbox com banco real).

### Slice 7-A.5 — Planner contract (HTTPS/SSRF no `WebhookUrl` validator)

- Data: 2026-06-26
- Agente/superficie: OpenCode (Planner)
- Status: **Concluido em 2026-06-26** (Implementer + validacoes finais). Relatorio em `docs/audits/slice-7a5-webhook-url-ssrf-report-2026-06-26.md`.
- Slice 7-A.6 tambem concluido em 2026-06-26 (configuracao Worker/appsettings.json com placeholder de `PaymentHub:WebhookSecretEncryptionKey`).
- Resumo da implementacao: helper puro `internal static class WebhookUrlValidator` em `src/PaymentHub.Application/Tenants/Validation/WebhookUrlValidator.cs` (~200 linhas) com `public static bool IsAllowed(string? value, bool isDevelopment, out string? reason)`. `RegisterApplicationClientValidator` recebe `IRuntimeEnvironment environment` por injecao de construtor e adiciona `RuleFor(x => x.WebhookUrl).Must((req, url) => WebhookUrlValidator.IsAllowed(url, environment.IsDevelopment, out _)).When(x => !string.IsNullOrWhiteSpace(x.WebhookUrl)).WithMessage("WebhookUrl must be an absolute HTTPS URL pointing to a public endpoint.")`. `<InternalsVisibleTo Include="PaymentHub.UnitTests" />` adicionado em `PaymentHub.Application.csproj` (padrao ja existente em `PaymentHub.Worker.csproj:11`).
- Q1 respondida: `AddValidatorsFromAssemblyContaining<RegisterTenantValidator>()` em `Program.cs:81` resolve o construtor de `RegisterApplicationClientValidator` via DI automaticamente; `IRuntimeEnvironment` ja esta registrado como Singleton em `Program.cs:66`, entao nenhum fallback em `HandleAsync` foi necessario.
- Validacoes executadas: `dotnet restore`, `dotnet build` (0 errors/0 warnings em 9 projetos), `dotnet test` (**281 tests passed**, baseline 178 + 103 casos expandidos), `dotnet test --filter WebhookUrl` (69 passed), `dotnet test --filter RegisterApplicationClient` (50 passed), `dotnet test --filter ApplicationWebhook` (13 passed, sem regressao), `dotnet test --filter OutboxDispatcherWorker` (17 passed, sem regressao), `scripts/agent-architecture-check.sh` (passed), `git diff --check` (passed).
- Arquivos criados (3): `src/PaymentHub.Application/Tenants/Validation/WebhookUrlValidator.cs`, `tests/PaymentHub.UnitTests/Application/Validation/WebhookUrlValidatorTests.cs` (~30 metodos, 66+ casos), `tests/PaymentHub.UnitTests/Application/RegisterApplicationClientValidatorTests.cs` (17 testes), `docs/audits/slice-7a5-webhook-url-ssrf-report-2026-06-26.md`.
- Arquivos alterados (3): `src/PaymentHub.Application/Tenants/RegisterApplicationClientHandler.cs` (validator ctor + Must rule), `src/PaymentHub.Application/PaymentHub.Application.csproj` (InternalsVisibleTo), `docs/specs/011-security-and-compliance.md` (nova secao `### Protecao SSRF em ApplicationClient.WebhookUrl` + 6 bullets em `## Testes esperados`).
- Constraint respeitada: dispatcher (`HttpApplicationWebhookDispatcher.cs`), worker (`OutboxDispatcherWorker.cs` + `Program.cs`), outbox (`OutboxEvent`), `PostgresServiceCollectionExtensions`, `Worker/appsettings.json` e ADRs **nao foram tocados** neste slice.
- Proxima acao: implementar **7-A.6** em slice separado.
- Escopo desta entrega (unico slice, **nao dividir**): adicionar validacao HTTPS/SSRF para `WebhookUrl` no fluxo de registro de `ApplicationClient`. **Nao** altera dispatcher HTTP, **nao** altera worker/outbox, **nao** altera politica de `LastError`, **nao** implementa provider real/painel admin/mensageria externa/rotacao de secret/retry/backoff/contrato de API Key.
- Pre-condicoes verificadas:
  - Sub-slices 7-A.1, 7-A.2, 7-A.3, 7-A.4, 7-A.7, 7-A.8 ja concluidos (vide `git log --oneline | head -3`).
  - `HttpApplicationWebhookDispatcher` realocado em `src/PaymentHub.Infrastructure.Postgres/Webhooks/HttpApplicationWebhookDispatcher.cs` (post-7-A.2).
  - `IRuntimeEnvironment` (Application) ja existe em `src/PaymentHub.Application/Abstractions/Context/IRuntimeEnvironment.cs:3-6` e ja e injetado em `CreateCheckoutHandler` (`src/PaymentHub.Application/Checkouts/CreateCheckoutHandler.cs:38,51`). Implementacao `HostRuntimeEnvironment` registrada como Singleton em `src/PaymentHub.Api/Program.cs:66`.
  - FluentValidation auto-wired em `src/PaymentHub.Api/Program.cs:81` via `AddValidatorsFromAssemblyContaining<RegisterTenantValidator>()`.
  - Validator atual: `RegisterApplicationClientValidator` em `src/PaymentHub.Application/Tenants/RegisterApplicationClientHandler.cs:95-103` — apenas `MaximumLength(2000)` em `WebhookUrl`, **sem** checagem de scheme/host. **Esta e a lacuna de seguranca M3.**
- Discovery (7-A.5):
  - `RegisterApplicationClientHandler.HandleAsync` instancia `new ApplicationClient(..., request.WebhookUrl, protectedWebhookSecret)` em `RegisterApplicationClientHandler.cs:51-56`. Nenhum check antes. A entidade (`src/PaymentHub.Domain/Entities/ApplicationClient.cs:42`) apenas faz `Trim()` e armazena.
  - DTO `RegisterApplicationClientRequestDto` em `src/PaymentHub.Application/Tenants/Dtos.cs:9-14` expoe `string? WebhookUrl`.
  - Testes existentes em `tests/PaymentHub.UnitTests/Application/RegisterApplicationClientHandlerTests.cs` (209 linhas, 10 testes) cobrem apenas `WebhookSecret` (raw-nao-persistido, raw-nao-logado, raw-nao-retornado, normalizacao de whitespace, tenant-inexistente-lanca). **Nenhum teste de URL.**
  - `IRuntimeEnvironment` ja e mockado em `tests/PaymentHub.UnitTests/Application/CreateCheckoutHandlerTests.cs:26,42` — padrao a seguir.
  - `scripts/agent-architecture-check.sh` (80 linhas) ja garante: Application **nao** referencia Infrastructure/Api/Worker; Worker **nao** referencia Api; Infrastructure **nao** referencia Api/Worker. Qualquer helper novo em `Application/Tenants/Validation/` ja atende.
- Estrategia implementada (decidida pelo Planner):
  1. **Helper puro** `internal static class WebhookUrlValidator` em `src/PaymentHub.Application/Tenants/Validation/WebhookUrlValidator.cs` com assinatura `public static bool IsAllowed(string? value, bool isDevelopment, out string? reason)`. Sem dependencia de DI, sem logging, sem excecoes. Cobrir: `Uri.TryCreate(UriKind.Absolute)`; `Scheme == Uri.UriSchemeHttps` (ou `http` apenas em Development **e** apenas para hosts loopback); se `IPAddress.TryParse(uri.Host)` → bloquear loopback (`127.0.0.0/8`, `::1`), RFC1918 (`10.0.0.0/8`, `172.16.0.0/12`, `192.168.0.0/16`), link-local / IMDS (`169.254.0.0/16`, `fe80::/10`), unspecified (`0.0.0.0`, `::`), multicast, broadcast, IPv6 mapped IPv4 loopback (`::ffff:127.0.0.1` via `IPAddress.MapToIPv4()`); se host textual → bloquear `localhost` e `*.localhost`; opcionalmente bloquear `.local` / `*.local` (documentado).
  2. **Validator FluentValidation** em `src/PaymentHub.Application/Tenants/RegisterApplicationClientHandler.cs`: ctor do `RegisterApplicationClientValidator` ganha parametro `IRuntimeEnvironment` (mantem escopo estavel — mesmo tempo de vida dos demais validators do assembly). Regra nova:
     ```csharp
     RuleFor(x => x.WebhookUrl)
         .Must((req, url) => WebhookUrlValidator.IsAllowed(url, _env.IsDevelopment, out _))
         .When(x => !string.IsNullOrWhiteSpace(x.WebhookUrl))
         .WithMessage("WebhookUrl must be an absolute HTTPS URL pointing to a public endpoint.");
     ```
     Manter `MaximumLength(2000)` existente.
  3. **Q1 em aberto para Implementer/security-reviewer**: `AddValidatorsFromAssemblyContaining` resolve ctor com parametros via `IValidatorFactory`? Padrao do projeto: validators com ctor vazio em `RegisterProviderAccountValidator`, `RegisterTenantValidator`. **Mitigacao**: se DI falhar no startup do teste, fallback e chamar `WebhookUrlValidator.IsAllowed(...)` diretamente dentro do `HandleAsync` (ou em um `IWebhookUrlPolicy` injetado no handler). Esta decisao **nao muda** o helper nem os testes.
  4. **Spec 011 atualizada** com secao nova `## ApplicationClient WebhookUrl SSRF protection` contendo as 5 bullets do briefing.
- Arquivos a criar (3):
  - `src/PaymentHub.Application/Tenants/Validation/WebhookUrlValidator.cs` (~80 linhas, helper puro com `internal static`).
  - `tests/PaymentHub.UnitTests/Application/Validation/WebhookUrlValidatorTests.cs` (~20 testes cobrindo os 19+ cenarios do briefing: URLs validas, scheme invalido, localhost/loopback IPv4+IPv6, RFC1918 tres blocos, link-local/IMDS, unspecified/wildcard, excecao Development).
  - `docs/audits/slice-7a5-webhook-url-ssrf-report-2026-06-26.md` (relatorio final).
- Arquivos a alterar (3):
  - `src/PaymentHub.Application/Tenants/RegisterApplicationClientHandler.cs` — `RegisterApplicationClientValidator` ganha ctor com `IRuntimeEnvironment` + regra `Must(...)`.
  - `tests/PaymentHub.UnitTests/Application/RegisterApplicationClientHandlerTests.cs` — adicionar `Mock<IRuntimeEnvironment>` (default `IsDevelopment = false`), atualizar `CreateHandler()` para incluir validator com env, adicionar ~6 testes de handler (rejeita/receita na borda, ambiente Development aceita HTTP local).
  - `docs/specs/011-security-and-compliance.md` — adicionar secao `## ApplicationClient WebhookUrl SSRF protection` antes de `## Criterios de aceite`.
- Arquivos a **nao** alterar (constraint explicita do briefing):
  - `src/PaymentHub.Infrastructure.Postgres/Webhooks/HttpApplicationWebhookDispatcher.cs` (dispatcher intocado).
  - `src/PaymentHub.Worker/OutboxDispatcherWorker.cs` e `src/PaymentHub.Worker/Program.cs`.
  - `src/PaymentHub.Domain/Entities/OutboxEvent.cs` (politica de `LastError` preservada).
  - `src/PaymentHub.Infrastructure.Postgres/PostgresServiceCollectionExtensions.cs` (DI nao muda).
  - `src/PaymentHub.Worker/appsettings.json` (e 7-A.6).
  - ADRs (e 7-A.9).
- Casos de teste minimos (mapa briefing → testes):
  - **Validos**: `https://example.com/webhook`, `https://hooks.example.com/payment-hub`.
  - **Formato/scheme invalido**: `not-a-url`, `/relative/webhook`, `ftp://example.com/webhook`, `file:///etc/passwd`, `http://example.com/webhook` (fora de Development).
  - **Localhost/loopback**: `https://localhost/webhook`, `https://127.0.0.1/webhook`, `https://127.10.20.30/webhook`, `https://[::1]/webhook`.
  - **RFC1918**: `https://10.0.0.1/webhook`, `https://172.16.0.1/webhook`, `https://172.31.255.255/webhook`, `https://192.168.1.10/webhook`.
  - **Link-local/IMDS/wildcard**: `https://169.254.169.254/latest/meta-data`, `https://169.254.1.1/webhook`, `https://0.0.0.0/webhook`, `https://[::]/webhook`.
  - **Excecao Development**: `http://localhost:5000/webhook` aceita; `http://127.0.0.1:5000/webhook` aceita. `http://example.com` continua rejeitado mesmo em Development (sem ser host loopback).
- Validacoes planejadas (comandos exatos do briefing):
  - `git status --short` (working tree limpo antes de comecar; depois listar alteracoes).
  - `dotnet restore PaymentHub.slnx`.
  - `dotnet build PaymentHub.slnx` (0 erros / 0 warnings).
  - `dotnet test PaymentHub.slnx` (verificar total >= 133 — baseline Slice 6-C).
  - `dotnet test PaymentHub.slnx --filter "FullyQualifiedName~RegisterApplicationClient"` (>= 16 testes).
  - `dotnet test PaymentHub.slnx --filter "FullyQualifiedName~WebhookUrl"` (>= 20 testes novos).
  - `dotnet test PaymentHub.slnx --filter "FullyQualifiedName~ApplicationWebhook"` (sem regressao).
  - `dotnet test PaymentHub.slnx --filter "FullyQualifiedName~OutboxDispatcherWorker"` (sem regressao).
  - `scripts/agent-architecture-check.sh` (Application continua sem dependencia de Infrastructure/Api/Worker).
  - `git diff --check`.
- Criterios de aceite (do briefing, todos cobertos):
  1. `WebhookUrl` invalida rejeitada (formato/scheme/host).
  2. Nao-HTTPS rejeitado fora de Development.
  3. localhost/loopback bloqueado fora de Development.
  4. RFC1918 bloqueado.
  5. Link-local/IMDS bloqueado.
  6. Wildcard/unspecified bloqueado.
  7. URLs publicas HTTPS continuam aceitas.
  8. >= 19 testes do helper + >= 6 testes de handler.
  9. `docs/specs/011-security-and-compliance.md` atualizado.
  10. Build/test verde, `agent-architecture-check.sh` verde, dispatcher/worker/outbox intactos, sem avancar para 7-A.6 ou 7-A.9.
- Riscos ja mapeados:
  - **R1 (alto)**: `AddValidatorsFromAssemblyContaining` pode nao resolver `IRuntimeEnvironment` no ctor do validator. Mitigacao: implementar `IValidatorFactory` simples ou fallback no handler (decisao em Q1).
  - **R2 (medio)**: IPv6 mapped IPv4 (`::ffff:127.0.0.1`) pode escapar do bloqueio de loopback se nao houver `IPAddress.MapToIPv4()` quando `IsIPv4MappedToIPv6 == true`. Mitigacao: explicito no helper.
  - **R3 (baixo)**: hosts com Unicode/IDN (`xn--`, `localhost.com`) — `Uri.TryCreate` ja normaliza via `IdnHost` em .NET 10. Documentar que `.localhost`/`*.localhost` ja sao bloqueados; `.com.br` ou `xn--` nao sao (continuam publicos).
  - **R4 (baixo)**: dispatcher existente (`HttpApplicationWebhookDispatcher.cs:111`) continua a usar `app.WebhookUrl` sem revalidacao. Spec do briefing diz "dispatcher must never use a WebhookUrl that bypasses this validation" — isto sera verdade **apos** este slice porque toda escrita passa pelo validator. Nao ha caminho de escrita de `WebhookUrl` alem do construtor da entidade exposto via `RegisterApplicationClientHandler`.
- Proxima acao (implementer):
  1. Implementar `WebhookUrlValidator` puro primeiro (TDD-friendly, sem DI).
  2. Cobrir helper com `WebhookUrlValidatorTests.cs` (rodar `dotnet test --filter WebhookUrl` ate passar).
  3. Integrar no `RegisterApplicationClientValidator` (resolver Q1 primeiro; documentar decisao no relatorio).
  4. Atualizar `RegisterApplicationClientHandlerTests.cs` com cenario end-to-end via validator.
  5. Atualizar `docs/specs/011-security-and-compliance.md`.
  6. Criar `docs/audits/slice-7a5-webhook-url-ssrf-report-2026-06-26.md` com decisoes, Q1 respondida, validacoes executadas, gaps remanescentes.
  7. Acionar `security-reviewer` + `qa-reviewer` em paralelo (apos implementer sinalizar fim), depois `architect-reviewer`.
  8. Apos todos verdes: mover esta sub-seccao para `## Historico` (como `### 2026-06-26 - Slice 7-A.5 HTTPS/SSRF no WebhookUrl validator`) e seguir para 7-A.6 em slice separado.
- Proximo sub-slice recomendado (sem implementar): **7-A.6** — Configuracao Worker/appsettings.json com placeholder `WebhookSecretEncryptionKey`.

## Historico

Registre entradas concluídas abaixo quando fizer sentido manter rastreabilidade no repositorio.

### 2026-06-26 - Slice 1-IT — Base de testes de integracao Postgres (Testcontainers + TRUNCATE CASCADE + DI manual)

- Data: 2026-06-26
- Agente/superficie: OpenCode (Implementer) com continuidade Planner
- Objetivo: preencher `tests/PaymentHub.IntegrationTests/` (casca vazia ate entao) com fixture Testcontainers + 10 testes cobrindo migrations, repositorios principais (Tenant/ApplicationClient/ProviderAccount), protecao de `WebhookSecret` e ciclo de vida de `OutboxEvent`.
- Questoes decididas: **Q1** Testcontainers.PostgreSql 4.12.0; **Q2** collection fixture + `TRUNCATE ... RESTART IDENTITY CASCADE` em ordem topologica reversa entre testes; **Q3** opt-in via `[Trait("Category", "Integration")]` (suite unitaria continua rapida sem Docker); **Q4** DI manual minimo em `IntegrationTestFactory` (sem `AddPaymentHubPostgres`, evita `IHttpClientFactory`); **Q5** NAO corrigir API `appsettings.json` neste slice (gap residual documentado).
- Specs/ADRs consultadas: `013-testing-strategy.md`, `007-inbox-outbox-workers.md`, `011-security-and-compliance.md`, `ADR-0002`, `ADR-0007`, `ADR-0010`; template de relatorio em `slice-7a-real-outbox-dispatcher-report-2026-06-26.md`.
- Validacoes executadas: `dotnet restore` (passed); `dotnet build PaymentHub.slnx` (**0 errors / 0 warnings** em 9 projetos); `dotnet test PaymentHub.slnx --no-build` (**291 passed**, 281 baseline + 10 integration, **zero regressao**); `dotnet test tests/PaymentHub.IntegrationTests/PaymentHub.IntegrationTests.csproj --no-build` (**10/10 passed** em 7.6-32s, inclui startup de `postgres:16-alpine`); `scripts/agent-architecture-check.sh` (passed); `scripts/agent-docs-check.sh` (passed); `git diff --check` (clean).
- 10 testes novos (lista nominal via TRX):
  1. `Migrations/MigrationSmokeTests.Migrations_ShouldApplySuccessfully_OnEmptyPostgresDatabase` (Postgres 16-alpine via Testcontainers + `INFORMATION_SCHEMA` validando 10 tabelas).
  2. `Persistence/TenantApplicationClientPersistenceTests.DbContext_ShouldPersistTenantAndApplicationClient_AndReloadCorrectly`.
  3. `Persistence/TenantApplicationClientPersistenceTests.Tenant_AndApplication_UniqueIndex_ShouldPreventDuplicateSlug` (unique index `tenants.slug`).
  4. `Persistence/ApplicationClientWebhookSecretTests.ApplicationClient_ShouldPersistProtectedWebhookSecret_AndAllowInternalUnprotect` (plaintext NAO persistido + `Unprotect` recupera).
  5. `Persistence/ApplicationClientWebhookSecretTests.ApplicationClient_WithoutWebhookSecret_ShouldReportHasWebhookSecretFalse`.
  6. `Persistence/ProviderAccountPersistenceTests.ProviderAccountRepository_ShouldPersistAndLoadByTenantAndApplication` (`GetDefaultAsync` + `GetByCodeAsync`).
  7. `Persistence/ProviderAccountPersistenceTests.ProviderAccountRepository_ShouldRespectsTenantScope` (cross-tenant vazio retorna null).
  8. `Persistence/OutboxPersistenceTests.OutboxEvent_ShouldPersistPendingProcessingAndSentStates` (transicoes via `IOutboxEventStore`).
  9. `Persistence/OutboxPersistenceTests.OutboxEvent_SafeRetry_ShouldPersistCategoryWithoutExceptionMessage` (apenas categoria enum em `LastError`).
  10. `Persistence/OutboxPendingQueryTests.OutboxRepository_ShouldReturnOnlyDispatchablePendingEvents` (5 estados: `Pending + null`/`Pending + past` retornados; `Pending + future`/`Processing`/`Sent`/`Failed` NAO retornados).
- Arquivos criados (10 novos):
  - `tests/PaymentHub.IntegrationTests/Infrastructure/PostgresCollection.cs`
  - `tests/PaymentHub.IntegrationTests/Infrastructure/PostgresFixture.cs`
  - `tests/PaymentHub.IntegrationTests/Infrastructure/IntegrationTestFactory.cs`
  - `tests/PaymentHub.IntegrationTests/Migrations/MigrationSmokeTests.cs`
  - `tests/PaymentHub.IntegrationTests/Persistence/TenantApplicationClientPersistenceTests.cs`
  - `tests/PaymentHub.IntegrationTests/Persistence/ApplicationClientWebhookSecretTests.cs`
  - `tests/PaymentHub.IntegrationTests/Persistence/ProviderAccountPersistenceTests.cs`
  - `tests/PaymentHub.IntegrationTests/Persistence/OutboxPersistenceTests.cs`
  - `tests/PaymentHub.IntegrationTests/Persistence/OutboxPendingQueryTests.cs`
  - `docs/audits/slice-1-it-postgres-integration-tests-report-2026-06-26.md`
- Arquivos alterados (5): `tests/PaymentHub.IntegrationTests/PaymentHub.IntegrationTests.csproj` (Testcontainers/Npgsql/EF.Relational/Extensions.DI/Logging/Options 10.0.0; remove `Microsoft.AspNetCore.Mvc.Testing`); `docs/harness/validation-matrix.md` (11 entradas Phase 7 Slice 1-IT); `docs/roadmap/001-development-timeline.md` (Slice 1-IT `[CONCLUIDO 2026-06-26]`); `docs/roadmap/002-phase-status-board.md` (P2-2 `[PARCIALMENTE RESOLVIDO]`, indicador testes de integracao 0->10, Bloco B mostra Slice 1-IT concluido); `docs/harness/learnings.md` (entrada nova com 10 decisoes reaproveitaveis: collection fixture, opt-in trait, DI manual, TRUNCATE CASCADE, MigrationsAssembly explicito, chaves 32+ bytes, gotcha `IServiceScope`/`IAsyncDisposable`, gap Processing orfao).
- Nenhum codigo produtivo alterado fora dos arquivos do slice; nenhuma alteracao em `PaymentHub.Api`/`PaymentHub.Worker`/`PaymentHub.Infrastructure.Providers`/`PaymentHub.Domain`/`PaymentHub.Application`. Constraint do briefing (sem provider real, sem AbacatePay) respeitada.
- Gaps remanescentes documentados no relatorio: (a) API `appsettings.json` sem `PaymentHub` placeholder (paridade com Worker, Q5 deferred); (b) CI sem Docker obrigatorio (R2); (c) sweep de `Processing` orfao + `FOR UPDATE SKIP LOCKED` (multi-instancia, Phase 7 multi-instancia); (d) testes end-to-end API+Worker+Postgres (Slices 3-IT e 7-IT futuros).
- Aprendizado consolidado: ver entrada em `docs/harness/learnings.md` (2026-06-26 - Slice 1-IT). Padroes reaproveitaveis: xUnit `[Collection]` + `ICollectionFixture` para compartilhar container real; `TRUNCATE ... CASCADE` para isolamento sem destruir schema; DI manual minimo para reduzir tempo de bootstrap; trait `[Category, Integration]` para opt-in.
- Proximo slice recomendado (sem implementar): **Slice 2-A — AbacatePay sandbox funcional** (Phase 2). Slices 3-IT (middleware/checkout com `WebApplicationFactory`) e 7-IT (workers inbox/outbox com banco real) seguem em paralelo.

### 2026-06-26 - Slice 7-A.9 — Documentacao final, ADRs, roadmap e relatorio consolidado (Slice 7-A fechado)

- Data: 2026-06-26
- Agente/superficie: OpenCode (Implementer)
- Objetivo: Fechar formalmente o Slice 7-A (sub-slices 7-A.1 a 7-A.8 ja estavam implementados e validados): consolidar `ADR-0007-webhook-secret-protection.md` e criar `ADR-0010-real-outbox-dispatcher-location.md` (ambas `ACCEPTED`); atualizar `docs/adr/000-adr-index.md`, `docs/specs/007-inbox-outbox-workers.md`, `docs/specs/011-security-and-compliance.md`, `docs/roadmap/000-payment-hub-roadmap.md`, `docs/roadmap/001-development-timeline.md`, `docs/roadmap/002-phase-status-board.md`, `feature_list.md`, `docs/harness/learnings.md` e `docs/harness/validation-matrix.md`; criar relatorio consolidado `docs/audits/slice-7a-real-outbox-dispatcher-report-2026-06-26.md`.
- Fora de escopo: provider real, painel admin, conciliacao, testes de integracao novos, mudancas no dispatcher/worker/SSRF/secret, rotacao, mensageria externa, contratos HTTP. Documentacao + metadados apenas.
- Specs/ADRs/docs lidas: `AGENTS.md`, `agent-progress.md`, briefing do Slice 7-A.9, `docs/roadmap/000-payment-hub-roadmap.md`, `docs/roadmap/001-development-timeline.md`, `docs/roadmap/002-phase-status-board.md`, `docs/specs/007-inbox-outbox-workers.md`, `docs/specs/011-security-and-compliance.md`, `docs/adr/000-adr-index.md`, `docs/adr/ADR-0001`, reports dos sub-slices (`slice-7a5-webhook-url-ssrf-report-2026-06-26.md`, `slice-7a6-worker-appsettings-webhook-secret-key-report-2026-06-26.md`, `slice-6c-webhook-secret-protection-report-2026-06-25.md`), arquivos de implementacao apenas para confirmar nomes/decisoes (`HttpApplicationWebhookDispatcher.cs`, `OutboxDispatcherWorker.cs`, `IOutboxEventStore.cs`, `WebhookDispatcherException.cs`, `WebhookDispatcherCategory.cs`, `WebhookUrlValidator.cs`, `PaymentHubOptions.cs`, `IWebhookSecretProtector.cs`).
- Decisoes: (1) ADR-0007 (webhook secret protection) consolidada como `ACCEPTED` em 2026-06-25 pelo Slice 6-C; o arquivo de ADR foi criado neste slice para tornar a decisao pesquisavel. (2) ADR-0010 (real outbox dispatcher location) criada como `ACCEPTED` em 2026-06-26, capturando 11 decisoes arquiteturais: localizacao em `Infrastructure.Postgres.Webhooks`, lifetime Scoped, `IHttpClientFactory` nomeado, DI centralizado em `AddPaymentHubPostgres`, `IOutboxEventStore`/`IClock` para testabilidade, tenant guard via `GetByTenantAndIdAsync`, `LastError` seguro por categoria enum (7 valores), validacao HTTPS/SSRF no `WebhookUrl`, fail-fast de `IWebhookSecretProtector` no startup, remocao completa do `NoopApplicationWebhookDispatcher`. (3) Spec 007 reescrita com politica `LastError` por categoria enum, dispatcher HTTP real com tenant guard e `UnprotectFailure` sem HTTP, validacao `WebhookUrl` no validator, gaps conhecidos documentados (sweep `Processing`, multi-instancia, integracao real). (4) Spec 011 complementada com secao `### Dispatcher HTTP real do Outbox (Slice 7-A)` detalhando localizacao, tenant guard, `LastError` seguro, `UnprotectFailure`/`MissingWebhookUrl`, validacao `WebhookUrl`, fail-fast, seguranca consolidada e gaps conhecidos. (5) Roadmap 000/001/002 atualizados: P1-4 marcado como resolvido, Phase 7 com 0 gaps P1 proprios, Phase 6 mantida com 0 gaps P1 proprios, Slice 7-A adicionado na secao de slices recomendados (todos os 5 gaps P1 da auditoria de 2026-06-17 resolvidos). (6) `feature_list.md`: `PH-WORKER-001` e `PH-SEC-001` -> `Concluido`. (7) Learnings atualizadas com entrada nova cobrindo o padrao de dispatcher HTTP real em Infrastructure (com tenant guard, `IOutboxEventStore`, `IClock`, `LastError` seguro por categoria enum, fail-fast no startup, validacao HTTPS/SSRF). (8) Validation matrix atualizada com todos os checks do Slice 7-A marcados como `PASS`. (9) Relatorio consolidado criado em `docs/audits/slice-7a-real-outbox-dispatcher-report-2026-06-26.md` com a estrutura completa exigida pelo briefing (Resumo, Objetivo, Sub-slices 7-A.1 a 7-A.9, Arquivos principais, Comportamento anterior/novo, Decisoes arquiteturais, Decisoes de seguranca, Contrato webhook interno, Assinatura HMAC, Worker e Outbox, Politica LastError, Protecao WebhookSecret, Validacao WebhookUrl, Testes adicionados, Validacoes executadas, Evidencias, Gaps remanescentes, Proximos passos).
- Plano: 14 arquivos alterados (3 criados: ADR-0007, ADR-0010, relatorio consolidado; 11 alterados: index ADR, spec 007, spec 011, roadmap 000/001/002, feature_list, learnings, validation-matrix, agent-progress). Sem codigo produtivo. Sem migrations. Sem mudanca em contratos.
- Validacoes executadas (esperadas): `git status --short` (lista apenas arquivos de doc); `dotnet restore PaymentHub.slnx`; `dotnet build PaymentHub.slnx` (0 errors / 0 warnings); `dotnet test PaymentHub.slnx` (281 passed); filtros `~ApplicationWebhook` (13), `~OutboxDispatcherWorker` (17), `~WebhookSecret` (26), `~RegisterApplicationClient` (50), `~WebhookUrl` (69); `docker compose config`; `scripts/agent-verify.sh`; `RUN_DOTNET_VALIDATION=1 scripts/agent-verify.sh`; `scripts/agent-architecture-check.sh` (passed); `scripts/agent-docs-check.sh` (passed); `git diff --check` (clean).
- Evidencias: relatorio consolidado em `docs/audits/slice-7a-real-outbox-dispatcher-report-2026-06-26.md`; ADRs `docs/adr/ADR-0007-webhook-secret-protection.md` e `docs/adr/ADR-0010-real-outbox-dispatcher-location.md`; indice de ADRs em `docs/adr/000-adr-index.md` (data 2026-06-26); specs 007/011 atualizadas; roadmap 000/001/002 atualizados; `feature_list.md` com `PH-WORKER-001` e `PH-SEC-001` -> `Concluido`; learnings com entrada nova; validation matrix com Slice 7-A preenchido.
- Riscos residuais (nao resolvidos neste slice, fora de escopo): API `appsettings.json` ainda sem placeholder `PaymentHub` (paridade com Worker); testes de integracao com Postgres/migrations (P2-2 / Slice 1-IT); sweep automatico de `Processing` orfaos (M1-security); `FOR UPDATE SKIP LOCKED` em `OutboxRepository` (C.3-qa) para multi-instancia; headers adicionais B4-security deferred; AuditLog em handlers administrativos (P2-3); provider real (Slice 2-A).
- Proximo slice recomendado (sem implementar): **Slice 1-IT** — Base inicial de testes de integracao com Postgres/migrations; ou **Slice 2-A** — AbacatePay sandbox funcional (Phase 2). Ordem depende de decisao de produto. **Slice 5-A** (ADR-0008 autenticacao do painel admin) so faz sentido apos Phase 6 estar totalmente `VALIDATED` (P2-3 fechado).

### 2026-06-26 - Slice 7-A.6 Worker appsettings placeholder for WebhookSecretEncryptionKey

- Data: 2026-06-26
- Agente/superficie: OpenCode (Implementer)
- Objetivo: Garantir que o Worker tenha configuracao explicita da chave `PaymentHub:WebhookSecretEncryptionKey`. O `appsettings.json` (production) nao continha a secao `PaymentHub`, deixando o operador sem nome canonico da chave a ser fornecida por canal externo.
- Fora de escopo: 7-A.9 (ADRs/roadmap), dispatcher HTTP, Outbox worker, validador SSRF, provider real, painel admin, algoritmo de protecao, fail-fast, testes fortes do 7-A.8.
- Specs/ADRs/docs lidas: `AGENTS.md`, `docs/specs/011-security-and-compliance.md`, briefing do proprio slice, `docs/audits/slice-7a5-webhook-url-ssrf-report-2026-06-26.md`, planner contract do Slice 7-A pai.
- Discovery: `src/PaymentHub.Worker/appsettings.json` (linhas 1-26) nao continha `PaymentHub` nem `WebhookSecretEncryptionKey`. `src/PaymentHub.Worker/appsettings.Development.json` ja trazia `"WebhookSecretEncryptionKey": "dev-webhook-secret-key-change-me-32bytes"` (39 chars, compativel com o protector que faz `PadRight(32, '0')`). `PaymentHubOptions.WebhookSecretEncryptionKey` em `src/PaymentHub.Infrastructure.Postgres/Options/PaymentHubOptions.cs:10` confirma o nome canonico. `AesWebhookSecretProtector` em `src/PaymentHub.Infrastructure.Postgres/Security/CryptoServices.cs:87-91` lanca `InvalidOperationException("PaymentHub:WebhookSecretEncryptionKey is required.")` quando ausente. Fail-fast em `src/PaymentHub.Worker/Program.cs:53-56` ja estava intacto.
- Decisao: adicionar placeholder vazio explicito em `Worker/appsettings.json` (production), preservando o nome canonico da chave. **Nenhum valor real commitado**. `appsettings.Development.json` permanece com valor fake de 39 caracteres. API nao foi tocada (mesmo gap existe la mas o briefing deste slice limita escopo ao Worker).
- Plano: 3 arquivos alterados (Worker/appsettings.json + spec 011 + agent-progress.md). Sem codigo, sem testes, sem migration.
- Validacoes executadas: `git status --short`; `dotnet restore PaymentHub.slnx`; `dotnet build PaymentHub.slnx` (**0 errors / 0 warnings** em 9 projetos); `dotnet test PaymentHub.slnx` (**281 passed**, sem regressao); `--filter ~WebhookSecret` (passing); `--filter ~ApplicationWebhook` (13 passed, sem regressao); `--filter ~OutboxDispatcherWorker` (17 passed, sem regressao); `scripts/agent-architecture-check.sh` (passed); `git diff --check` (passed).
- Evidencias: `src/PaymentHub.Worker/appsettings.json` agora contem `"PaymentHub": { "WebhookSecretEncryptionKey": "" }` como placeholder; `src/PaymentHub.Worker/appsettings.Development.json` mantem o valor dev; `docs/specs/011-security-and-compliance.md` ganhou subsecao `#### Configuracao da chave por ambiente (Worker e API)` com regras de production/dev/variavel de ambiente; agent-progress.md atualizado.
- Riscos residuais: API `appsettings.json` ainda nao tem a secao `PaymentHub` (mesmo gap, mesmo risco). **Nao tratado** neste slice por constraint de escopo. Recomendacao: aplicar o mesmo placeholder em `src/PaymentHub.Api/appsettings.json` em slice proprio ou como parte de 7-A.9.
- Proximo sub-slice (sem implementar): **7-A.9** — Documentacao final (ADR-0007, ADR-0010, indice, feature_list `PH-WORKER-001` → Concluido, roadmap 002-phase-status-board P1-4 resolvido, learnings.md) + relatorio consolidado do Slice 7-A.

### 2026-06-26 - Slice 7-A.5 WebhookUrl HTTPS/SSRF protection

- Data: 2026-06-26
- Agente/superficie: OpenCode (Implementer)
- Objetivo: Enderecar gap M3 do par de revisores do Slice 7-A. Validar `ApplicationClient.WebhookUrl` no `RegisterApplicationClientValidator` para bloquear SSRF (loopback, RFC1918, link-local/IMDS, wildcard, unspecified, multicast, broadcast, `localhost`/`*.localhost`/`*.local`).
- Fora de escopo: dispatcher HTTP, worker/outbox, politica de `LastError`, provider real, painel admin, mensageria externa, rotacao de secret, retry/backoff, contrato de API Key, `Worker/appsettings.json`, ADRs.
- Specs/ADRs/docs lidas: `AGENTS.md`, `docs/specs/011-security-and-compliance.md`, `docs/specs/002-multitenancy-and-authentication.md`, `docs/harness/security.md`, `docs/audits/payment-hub-current-state-audit-2026-06-17.md`, planner contract do proprio slice (linhas 38-126 deste arquivo).
- Discovery: `RegisterApplicationClientValidator` tinha apenas `MaximumLength(2000)` em `WebhookUrl`. Sem checagem de scheme/host. `IRuntimeEnvironment` ja existia e ja era injetado em `CreateCheckoutHandler`, portanto o padrao estava pronto para ser replicado no validator.
- Decisoes: (1) Helper puro `internal static class WebhookUrlValidator` em `src/PaymentHub.Application/Tenants/Validation/` com `public static bool IsAllowed(string? value, bool isDevelopment, out string? reason)` — sem DI, sem logging, sem exceptions; totalmente unit-testable. (2) `internal` + `<InternalsVisibleTo Include="PaymentHub.UnitTests" />` em `PaymentHub.Application.csproj` (padrao ja existente em `Worker.csproj:11`). (3) `RegisterApplicationClientValidator` recebe `IRuntimeEnvironment` no ctor e adiciona `RuleFor(x => x.WebhookUrl).MaximumLength(2000).Must(...).When(...).WithMessage("WebhookUrl must be an absolute HTTPS URL pointing to a public endpoint.")` — mensagem unificada anti-enumeration. (4) Politica de Development exception: HTTP aceito **somente** para hosts loopback (`localhost`, `127.0.0.0/8`, `::1`). Em Development, HTTPS+publico continua ok; em Production, HTTP sempre rejeitado. (5) IPv6-mapped IPv4 loopback (`::ffff:127.0.0.1`) normalizado via `IPAddress.IsIPv4MappedToIPv6` + `MapToIPv4()`. (6) Boundary RFC1918 correta: `172.15.x.x` e `172.32.x.x` permanecem publicos.
- Q1 respondida (FluentValidation + DI): `AddValidatorsFromAssemblyContaining<RegisterTenantValidator>()` resolve o ctor do validator via DI automaticamente. `IRuntimeEnvironment` ja registrado como Singleton em `Program.cs:66`. **Nenhum fallback em `HandleAsync` foi necessario**.
- Plano: 6 arquivos (3 criados + 3 alterados). Sem refatoracao ampla. Sem migration. Sem alteracao em dispatcher/worker/outbox.
- Validacoes executadas: `dotnet restore PaymentHub.slnx` (passed); `dotnet build PaymentHub.slnx` (**0 errors / 0 warnings** em 9 projetos); `dotnet test PaymentHub.slnx` (**281 passed**, baseline 178 + 103); filtros `~WebhookUrl` (69 passed), `~RegisterApplicationClient` (50 passed), `~ApplicationWebhook` (13 passed, sem regressao), `~OutboxDispatcherWorker` (17 passed, sem regressao); `scripts/agent-architecture-check.sh` (passed); `git diff --check` (passed).
- Evidencias: `WebhookUrlValidator` em `src/PaymentHub.Application/Tenants/Validation/WebhookUrlValidator.cs`; validator ctor injection em `src/PaymentHub.Application/Tenants/RegisterApplicationClientHandler.cs:99`; InternalsVisibleTo em `src/PaymentHub.Application/PaymentHub.Application.csproj:14`; nova secao de spec em `docs/specs/011-security-and-compliance.md`; relatorio completo em `docs/audits/slice-7a5-webhook-url-ssrf-report-2026-06-26.md`.
- Riscos residuais: B4-security (headers `X-PaymentHub-Event`/`X-PaymentHub-Tenant`/`X-PaymentHub-Application` nao validados — deferred). `ApplicationClient.UpdateWebhook(...)` nao foi tocado porque nao existe endpoint de update na codebase atual; quando existir (Phase 5 painel admin), o mesmo helper deve ser reaproveitado. Cobertura de integracao continua zero (P2-2).
- Aprendizados (a serem consolidados em `docs/harness/learnings.md`): helpers `internal static` + `InternalsVisibleTo` evitam inflar a API publica; FluentValidation resolve ctor via DI sem factory custom; mensagem de erro unificada e anti-enumeration; IPv6-mapped IPv4 exige normalizacao explicita antes do bloqueio de loopback.
- Proximo sub-slice: **7-A.6** — `src/PaymentHub.Worker/appsettings.json` recebe placeholder documentado para `PaymentHub:WebhookSecretEncryptionKey`.

### 2026-06-25 - Slice 6-C Webhook secret protection

- Data: 2026-06-25
- Agente/superficie: OpenCode
- Objetivo: Proteger `ApplicationClient.WebhookSecret` em repouso via `IWebhookSecretProtector` + `AesWebhookSecretProtector` (AES-CBC com chave em `PaymentHub:WebhookSecretEncryptionKey`); DTO de resposta expoe apenas `hasWebhookSecret: bool`; dispatcher HTTP desprotege internamente antes de assinar HMAC; sem migration estrutural.
- Fora de escopo: Dispatcher HTTP real no Worker (Slice 7-A), assinatura HMAC de webhook interno em producao, rotação completa de segredo via API, painel admin, provider real, migrations estruturais grandes, sistema externo de secrets.
- Specs/ADRs/docs lidas: `AGENTS.md`, `docs/harness/payment-hub-execution-guide.md`, `docs/harness/definition-of-ready.md`, `docs/harness/definition-of-done.md`, `docs/specs/002-multitenancy-and-authentication.md`, `docs/specs/007-inbox-outbox-workers.md`, `docs/specs/011-security-and-compliance.md`, `docs/audits/payment-hub-current-state-audit-2026-06-17.md`, `docs/audits/spec-adherence-audit-2026-06-17.md`, `docs/audits/slice-6a-active-status-enforcement-report-2026-06-17.md`, `docs/audits/slice-6b-provider-account-authenticated-context-report-2026-06-18.md`, `docs/audits/slice-6d-bootstrap-admin-seed-policy-report-2026-06-18.md`, `docs/roadmap/000-payment-hub-roadmap.md`, `docs/roadmap/001-development-timeline.md`, `docs/roadmap/002-phase-status-board.md`.
- Discovery: `ApplicationClient.WebhookSecret` era persistido em texto claro. DTO `ApplicationClientResponseDto` nao continha `WebhookSecret`, mas o handler tampouco protegia — quem persistia era o proprio construtor da entidade aceitando o raw. `HttpApplicationWebhookDispatcher` lia o raw para assinar HMAC. Nao havia mecanismo de protecao equivalente a `ICredentialProtector` (que ja cuida de credenciais de provider).
- Decisoes: (1) Interface `IWebhookSecretProtector` em `PaymentHub.Application/Abstractions/Security/ICrypto.cs` com `Protect(string)`/`Unprotect(string)`. (2) Implementacao `AesWebhookSecretProtector` em `PaymentHub.Infrastructure.Postgres/Security/CryptoServices.cs` usando AES-CBC com IV randomico e prefixo de proposito `PaymentHub.ApplicationClient.WebhookSecret.v1` (verificacao em tempo constante via `CryptographicOperations.FixedTimeEquals`). (3) Entidade `ApplicationClient` agora aceita `protectedWebhookSecret` no construtor (parametro nomeado explicito), nao `webhookSecret` raw. (4) `HttpApplicationWebhookDispatcher` chama `Unprotect` imediatamente antes de assinar e aborta o dispatch se falhar. (5) DTO de resposta expoe apenas `hasWebhookSecret: bool`. (6) `BootstrapOptions.DevelopmentWebhookSecret` opcional; se preenchido em `appsettings.Development.json`, o seedor protege antes de persistir. (7) Sem migration: nome e shape da coluna preservados; conteudo passa a ser blob cifrado.
- Plano: Pequeno, centralizado, sem refactor amplo. 8 arquivos de codigo modificados; 4 arquivos de teste novos; 6 arquivos de doc atualizados; 1 relatorio novo.
- Validacoes executadas: todas passaram — 9 projetos compilam com 0 erros e 0 warnings; 133 testes unitarios passando (27 novos: 11 protector + 10 handler + 3 seeder + 3 dispatcher); filtros WebhookSecret 25, Bootstrap 15, ApiKeyAuthenticationMiddlewareTests 11, ProviderAccount 15 sem regressao; docker compose config valido.
- Evidencias: `IWebhookSecretProtector` registrado como singleton em `PostgresServiceCollectionExtensions`; `ApplicationClient.WebhookSecret` agora armazena blob cifrado (nao raw); `ApplicationClientResponseDto.HasWebhookSecret` substitui qualquer exposicao de secret; `HttpApplicationWebhookDispatcher.DispatchAsync` chama `Unprotect` antes de `Sign`; mensagem de log do dispatcher usa `applicationId`+`tenantId`+operacao, nunca o segredo; chave de protecao em `PaymentHub:WebhookSecretEncryptionKey` falha claramente quando ausente; testes cobrem raw-nao-persistido, raw-nao-logado, raw-nao-retornado, desprotegivel-internamente, idempotencia do seeder, e falha de Unprotect no dispatcher.
- Riscos residuais: Slice 6-C nao introduziu migration porque nao ha dados produtivos ainda — qualquer primeiro deploy em producao precisa da chave configurada antes de criar ApplicationClients com webhook secret, caso contrario `AesWebhookSecretProtector` lanca `InvalidOperationException` no startup (registrado como singleton). Cobertura de integracao continua zero (P2-2); este slice nao implementa testes com Postgres real. P1-4 (dispatcher no-op no Worker) continua pendente → endereçado pelo Slice 7-A.
- Aprendizados: entrada nova em `docs/harness/learnings.md` "Webhook secret protection" cobrindo o padrao de parametrizacao explicita (parametro `protectedWebhookSecret`), uso de `CryptographicOperations.FixedTimeEquals` para verificar prefixo de proposito, e a estrategia de usar a chave de `ICredentialProtector` como modelo sem reusar a mesma chave. Report completo em `docs/audits/slice-6c-webhook-secret-protection-report-2026-06-25.md`.

### 2026-06-25 - OpenCode harness v2

- Data: 2026-06-25
- Agente/superficie: OpenCode
- Objetivo: Evoluir harness OpenCode para separar config estrutural (`.opencode/opencode.json`) de comportamento/metadados por agente (`.opencode/agents/*.md`), mover fluxos para docs de harness, criar skills locais, ajustar permissoes e fortalecer scripts de verificacao.
- Fora de escopo: Domínio, API, Worker, providers, contratos de pagamento, regras de negocio, secrets, migrations, dependencias pesadas e CI/CD.
- Discovery: `opencode.json` ainda duplicava metadados/permissoes dos agentes que ja existem em `.opencode/agents/*.md`; `implementer` tinha `edit: '*': allow`; reviewers tinham `edit: deny`, mas nao havia bloqueio explicito de chamada de subagents; scripts ainda nao detectavam essas ambiguidades.
- Decisoes: `.opencode/agents/*.md` sera a fonte de verdade de comportamento, metadados e permissoes por agente; `.opencode/opencode.json` ficara estrutural e global. `implementer` usara `edit: '*': ask`; planner/implementer poderao chamar somente reviewers; reviewers terao `task: deny` e `edit: deny`.
- Plano: Reduzir JSON; ajustar frontmatter dos agentes; atualizar README/docs de harness com fonte de verdade; fortalecer `agent-docs-check.sh`; rodar validacoes obrigatorias e registrar evidencias.
- Validacoes executadas: `scripts/agent-init.sh` passou; `scripts/agent-docs-check.sh` passou; `scripts/agent-architecture-check.sh` passou; `scripts/agent-smoke.sh` passou com restore/build e `docker compose config`; `scripts/agent-verify.sh` passou; `/usr/bin/dotnet restore` passou; `/usr/bin/dotnet build` passou com 0 erros/0 warnings; `/usr/bin/dotnet test` passou com 106 testes unitarios e projeto de integracao sem testes descobertos; `git diff --check` passou; `opencode debug config >/dev/null` passou.
- Evidencias: `opencode.json` nao contem `agent`, `agents`, `notes` ou `prompt`; `.opencode/agents/*.md` contem metadados/permissoes por agente; `implementer` usa `edit: '*': ask`; reviewers usam `edit: deny` e `task: deny`; `planner`/`implementer` podem acionar apenas os tres reviewers; `agent-docs-check.sh` valida essas regras.
- Riscos residuais: OpenCode precisa ser reiniciado para carregar config/agentes alterados; testes de integracao continuam sem testes descobertos; permissao `task` foi validada pelo schema/CLI, mas a granularidade exata de matching depende da implementacao do OpenCode; scripts continuam heurísticos e nao substituem revisao humana.
- Aprendizados: atualizado aprendizado de 2026-06-24 em `docs/harness/learnings.md` para fixar `.opencode/agents/*.md` como fonte de verdade e impedir duplicacao no JSON.

### 2026-06-23 - CI basico

- Objetivo: Criar CI basico com verificacao de harness, restore, build e test.
- Fora de escopo: E2E, publicacao de artefatos, deploy e validacoes com banco real.
- Arquivos alterados: `.github/workflows/ci.yml`, `feature_list.md`, `docs/ai/agent-readiness-audit.md`, `agent-progress.md`.
- Validacoes planejadas: `scripts/agent-verify.sh`, `dotnet restore`, `dotnet build`, `dotnet test`.
- Validacoes executadas: `scripts/agent-verify.sh` passou; `dotnet restore` passou; `dotnet build --no-restore` passou com 0 warnings/0 errors; `dotnet test --no-build` passou com 106 testes unitarios e projeto de integracao sem testes descobertos.
- Riscos residuais: CI ainda nao cobre E2E, publicacao de artefatos, deploy ou validacoes com banco real.

### 2026-06-24 - Configuracoes topico 3 em diante

- Objetivo: Aprimorar CI, alinhar OpenCode, documentar uso diario, adicionar gate simples de secrets e preparar roteiro de auditoria specs versus codigo.
- Fora de escopo: Implementar testes de integracao/E2E, alterar dominio de pagamento, adicionar provider real, deploy ou validacao com banco real no CI.
- Arquivos alterados: `.github/workflows/ci.yml`, `scripts/agent-verify.sh`, `.opencode/README.md`, `.opencode/agents/*`, `README.md`, `docs/ai/harness-engineering.md`, `docs/ai/validation-checklist.md`, `docs/ai/agent-readiness-audit.md`, `docs/ai/spec-adherence-next-audit.md`, `docs/audits/spec-adherence-refresh-2026-06-24.md`, `feature_list.md`, `agent-progress.md`.
- Validacoes planejadas: `scripts/agent-verify.sh`, `dotnet restore`, `dotnet build --no-restore`, `dotnet test --no-build --logger "trx;LogFilePrefix=test-results" --results-directory TestResults`.
- Validacoes executadas: `scripts/agent-verify.sh` passou; `dotnet restore` passou; `dotnet build --no-restore` passou com 0 warnings/0 errors; `dotnet test --no-build --logger "trx;LogFilePrefix=test-results" --results-directory TestResults` passou com 106 testes unitarios e gerou arquivos `.trx`.
- Riscos residuais: CI ainda nao executa testes de integracao reais, E2E, deploy ou validacao com banco real; o scan de secrets e simples e nao substitui ferramenta dedicada.
