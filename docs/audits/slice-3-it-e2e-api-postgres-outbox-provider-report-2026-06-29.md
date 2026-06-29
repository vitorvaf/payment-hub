# Slice 3-IT — End-to-End API + Postgres + Outbox + Provider Report

Data: 2026-06-29
Phase: 3 + 7 (E2E)
Specs relacionadas: `docs/specs/009-api-contracts.md`, `docs/specs/007-inbox-outbox-workers.md`, `docs/specs/013-testing-strategy.md`, `docs/specs/006-provider-webhooks.md`, `docs/specs/011-security-and-compliance.md`
ADRs consultadas: `ADR-0009-end-to-end-integration-tests.md` (pendente), referencias Slice 1-IT (`ADR-0010-real-outbox-dispatcher-location.md`).
Gap enderecado: **P2-2** do `docs/roadmap/002-phase-status-board.md` (projeto de testes de integracao sem testes e2e descobertos).

## Resumo

Ate o Slice 3-IT (2026-06-29), `tests/PaymentHub.IntegrationTests/` cobria apenas migrations + repositorios principais (10 testes Slice 1-IT) sem passar pelo pipeline HTTP real. P2-2 do phase board marcava "e2e API+Worker ainda nao coberto" desde 2026-06-17. Este slice entregou:

- **`PaymentHubApiFactory : WebApplicationFactory<Program>`** que sobe a `PaymentHub.Api` real em memoria apontando para o container Postgres compartilhado do `PostgresFixture`, com overrides de configuracao deterministicos + substituicao dos named HttpClients por fakes.
- **`AbacatePayFakeHttpHandler`** + **`ApplicationWebhookCaptureHandler`** + **`E2ESeedHelpers`** (`tests/PaymentHub.IntegrationTests/Support/`).
- **4 testes E2E P1** em `EndToEnd/AbacatePayCheckoutE2ETests.cs` cobrindo (1) checkout happy path, (2) webhook com assinatura valida + payment atualizado + outbox criado, (3) idempotencia em webhook duplicado, (4) fail-fast 401 sem assinatura.
- **2 producao bugs descobertos e corrigidos** (unit tests nao cobriam):
  1. `webhook_events.raw_payload` `jsonb -> text` (migracao `20260629205545_ChangeRawPayloadToText`) — Postgres `jsonb` reformata JSON no insert (espacos, normalizacao) e quebra HMAC sobre o body bruto.
  2. `ProcessWebhookEventHandler.ProcessAsync` agora chama `_payments.AddAttemptAsync(attempt, ct)` explicitamente — EF Core 10 nao detecta confiavelmente o novo item como Added via collection navigation privada (`payment._attempts`), levantando `DbUpdateConcurrencyException` no UPDATE subsequente.

Suite previa: 418 testes (apos Slice 2-B). Suite nova: **422 testes** (+4 E2E, 0 regressoes). Build limpo (0 errors / 0 warnings em 9 projetos). `scripts/agent-architecture-check.sh` e `scripts/agent-docs-check.sh` verdes. `git diff --check` limpo.

## Questoes decididas (Q1-Q7)

| # | Questao | Decisao | Justificativa |
|---|---------|---------|---------------|
| **Q1** | Ferramenta E2E | **`Microsoft.AspNetCore.Mvc.Testing 10.0.0` + `WebApplicationFactory<Program>`** | Padrao oficial .NET para testes in-process; integrado com `xunit`. Alternativa manual (HttpClient + Kestrel) exigiria bootstrap manual. |
| **Q2** | `CreateHost(IHostBuilder)` override | **SIM — nao apenas `ConfigureWebHost(IWebHostBuilder)`** | `Program.cs` le `ConnectionStrings:Postgres` EAGERLY em `AddPaymentHubPostgres(builder.Configuration)` (linha 62), antes do host ser construido. Um override via `IWebHostBuilder.ConfigureAppConfiguration` chega tarde demais porque `ConfigureAppConfiguration` so roda em `Build()`. Ja documentado como "teste contra factos da vida"; solucao canonica documentada na entrada de learnings. |
| **Q3** | Substituir `abacatepay` HttpClient | **`services.AddHttpClient("abacatepay").ConfigurePrimaryHttpMessageHandler(() => fake)` em `ConfigureTestServices`** | `AddHttpClient(name)` registra via `ConfigureOptions`; chamada subsequente adiciona um builder action que define `b.PrimaryHandler = fakeHandler`. O ultimo PrimaryHandler ganha, mantendo o BaseAddress/Timeout originais (registrados pela API). |
| **Q4** | `WebhookProcessorWorker` rodar dentro do `WebApplicationFactory`? | **NAO** — invocar `IProcessWebhookEventHandler.ProcessAsync(webhookId, ct)` manualmente via `factory.Services.CreateScope()` | `TestServer` nao hospeda BackgroundServices. Decisao documentada; cobre o equivalente da tick do worker sem flakiness de timing. Slice futuro (Phase 7-IT) pode mudar para `Worker: CreateHostBuilder` + override se houver ganho. |
| **Q5** | Tenant/application/webhook seed por teste? | **SIM — via `E2ESeedHelpers` (IdempotencyKey, Tenant, ApplicationClient, ApiKey, ProviderAccount)** | Permite isolamento por teste sem dependencia de ID fixo. Gera `Guid.NewGuid()` por helper; testes que precisam asserir roundtrip passam `id` explicito. |
| **Q6** | DB reset entre testes | **Por-teste `ResetDatabaseAsync` em `PaymentHubApiFactory`** que faz `TRUNCATE ... RESTART IDENTITY CASCADE` em ordem topologica reversa via `CreateDbContext()` direto do fixture | Evita cross-contamination de fakes (`CallCount`, `_last`). Custo ~1-2s boot por teste (4 testes) vs complexidade de shared-factory. |
| **Q7** | Cleanup de arquivos `/tmp/slice3it-*.log` | **SIM — NAO persistir apos slice** | Diagnosticos foram usados temporariamente; commit final do slice nao inclui referencias aos arquivos. |

## Arquivos criados

| Arquivo | Linhas | Proposito |
|---------|--------|-----------|
| `tests/PaymentHub.IntegrationTests/Infrastructure/PaymentHubApiFactory.cs` | 287 | `WebApplicationFactory<Program>` com `CreateHost` override para config eager + re-registro de named `HttpClient`s com fakes + helpers `ProtectAbacatePayCredentials`/`HashApiKey`/`ResetDatabaseAsync`/`CreateDbContext`/`ResolveScoped`. |
| `tests/PaymentHub.IntegrationTests/Support/AbacatePayFakeHttpHandler.cs` | 132 | `HttpMessageHandler` que captura `Authorization: Bearer`, method, path, body e responde envelope AbacatePay deterministico (`id` = `metadata.paymentId`, `status = PENDING`, `devMode = true`). |
| `tests/PaymentHub.IntegrationTests/Support/ApplicationWebhookCaptureHandler.cs` | 67 | `HttpMessageHandler` que captura cada requisicao outbound do `application-webhook` (method, url, `X-PaymentHub-Signature`, `X-PaymentHub-Timestamp`, body). Retorna 204. |
| `tests/PaymentHub.IntegrationTests/Support/E2ESeedHelpers.cs` | 130 | `SeedTenantAndApplicationAsync`, `SeedProviderAccountAsync`, `E2ECredentials` record. Usa o DI scope real da API para preservar tracker semantics. |
| `tests/PaymentHub.IntegrationTests/EndToEnd/AbacatePayCheckoutE2ETests.cs` | 596 | 4 testes P1 + helpers `CreateCheckoutAsync`, `BuildCreateCheckoutRequest`, `BuildAbacatePayCompletedEnvelope`, `SendAbacatePayWebhookAsync`, `ComputeAbacatePayHmacSignature`, `GetEnvelopeId`. |
| `src/PaymentHub.Infrastructure.Postgres/Migrations/20260629205545_ChangeRawPayloadToText.Designer.cs` | (gerada) | Designer da migracao. |
| `src/PaymentHub.Infrastructure.Postgres/Migrations/20260629205545_ChangeRawPayloadToText.cs` | 34 | `AlterColumn` em `webhook_events.raw_payload` de `jsonb` para `text`. |

Total: **7 arquivos novos**.

## Arquivos modificados

| Arquivo | Linhas alteradas | Conteudo da mudanca |
|---------|------------------|---------------------|
| `tests/PaymentHub.IntegrationTests/PaymentHub.IntegrationTests.csproj` | +6 | `Microsoft.AspNetCore.Mvc.Testing 10.0.0` + `ProjectReference` para `PaymentHub.Api`. |
| `src/PaymentHub.Infrastructure.Postgres/Configurations/EntityConfigurations.cs` | +8 / -1 | `WebhookEvent.RawPayloadJson` muda de `HasColumnType("jsonb")` para `HasColumnType("text")` com comentario explicando que HMAC exige bytes preservados. |
| `src/PaymentHub.Application/Webhooks/WebhookHandlers.cs` | +10 / -4 | `ProcessAsync` chama `_payments.AddAttemptAsync(attempt, cancellationToken)` apos `payment.RegisterAttempt(...)`. Comentario inline explica o tracking issue do EF Core 10 + collection navigation. |
| `src/PaymentHub.Infrastructure.Postgres/PaymentHubDbContextModelSnapshot.cs` | (auto) | Atualizado pelo `dotnet ef migrations add` para refletir `text`. |
| `tests/PaymentHub.UnitTests/Application/ProcessWebhookEventHandlerAbacatePayTests.cs` | +5 | `BuildCommonMocks` adiciona `.Setup(p => p.AddAttemptAsync(It.IsAny<PaymentAttempt>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask)` ao `Mock<IPaymentRepository>(MockBehavior.Strict)`. |

Total: **5 arquivos produtivos alterados**.

## Producao bugs encontrados (P1 — unit tests nao cobriam)

### Bug A — `webhook_events.raw_payload` era `jsonb`, nao `text`

**Sintoma**: HMAC signature verifica `ProviderWebhooksController.Receive` em `ProcessWebhookEventHandler.ProcessAsync`, mas o teste E2E (`ProviderWebhook_ValidSignature_UpdatesPaymentAndEnqueuesOutbox`) reporta `webhook.LastError = "AbacatePay webhook signature invalid (SignatureMismatch)"`. Diagnostic mostrava `stored[0..40]={"id": "evt-...` vs `sent[0..40]={"id":"evt-...` — espacos extras apos cada `:`.

**Causa raiz**: `WebhookEventConfiguration` mapeava `raw_payload` como `HasColumnType("jsonb")`. Postgres `jsonb` armazena JSON em forma binaria normalizada (e nao texto bruto) — na insercao, ele faz `jsonb_in(text)` que normaliza whitespace e adiciona um espaco apos cada `:`, `,` e quebra objetos em multiplas linhas. Aplicacoes que precisam de bytes exatos (HMAC, assinatura digital, hash) nao podem usar `jsonb`.

**Fix aplicado**: Migracao `20260629205545_ChangeRawPayloadToText` faz `ALTER TABLE webhook_events ALTER COLUMN raw_payload TYPE text`. Combinada com `EntityConfigurations.cs` mudando o tipo para `text`. A coluna agora preserva bytes exatos; `WebhookEvent.RawPayloadJson` continua sendo `string`. Aplicacao nao queries no payload (e opaco do ponto de vista do dominio); passa-o direto ao adapter para verificacao HMAC + normalizacao.

**Por que unit tests nao pegaram**: Nenhum unit test usa a fixture Postgres — eles constroem `WebhookEvent` com `RawPayloadJson = "..."` ja em memoria. O `EntityConfigurations` tem o tipo, mas a interacao com `jsonb` so manifesta em roundtrip real com o Postgres.

### Bug B — `ProcessWebhookEventHandler.ProcessAsync` nao marcava `PaymentAttempt` como Added

**Sintoma**: Linha 236 (`await _uow.SaveChangesAsync(cancellationToken)` apos `webhook.MarkProcessed()`) levantava `DbUpdateConcurrencyException` — UPDATE afetou 0 linhas.

**Causa raiz**: `payment.RegisterAttempt(ToAttemptStatus(newStatus), providerPaymentId, null)` adiciona um `PaymentAttempt` novo a `_attempts` (que e `private readonly List<PaymentAttempt>`). EF Core 10 + collection navigation privada NAO detecta confiavelmente o novo item como Added — o change tracker o classifica como `Modified`. O UPDATE subsequente (com o novo `Guid` na clausula WHERE) afeta 0 rows e levanta `DbUpdateConcurrencyException`. Diagnostic dump de `db.ChangeTracker.Entries()` confirmou: `PaymentAttempt(state=Modified)` em vez de `PaymentAttempt(state=Added)`.

**Fix aplicado**: `_payments.AddAttemptAsync(attempt, cancellationToken)` apos `RegisterAttempt(...)`. Isso chama `_db.PaymentAttempts.AddAsync(attempt, ct)` que explicitamente marca como Added, garantindo o INSERT correto. O repositorio ja tinha esse metodo desde o Slice 1 (era usado pelo checkout handler).

**Por que unit tests nao pegaram**: Os 2 unit tests que originalmente falharam (`ProcessAsync_ShouldResolveProviderAccountAndUnprotectWebhookSecret` + `ProcessAsync_ShouldNotPersistWebhookSecret_OnProcessedWebhook`) tinham `MockBehavior.Strict` em `IPaymentRepository`. Quando o handler passou a chamar `AddAttemptAsync`, os mocks estritos quebraram com `IPaymentRepository.AddAttemptAsync(PaymentAttempt, CancellationToken) invocation failed with mock behavior Strict`. Fix: setup default no `BuildCommonMocks()` que retorna `Task.CompletedTask`. Antes do fix, os testes unit falhavam — mas isso so aconteceu no Slice 3-IT, nao no Slice 2-B. Slice 2-B passou sem chamar `AddAttemptAsync`; foi o Slice 3-IT que introduziu a chamada para corrigir Bug B.

## Resultados de validacao

| Comando | Resultado esperado | Resultado real |
|---------|--------------------|---------------|
| `dotnet restore PaymentHub.slnx` | 0 erros | 0 erros |
| `dotnet build PaymentHub.slnx` | 0 errors / 0 warnings em 9 projetos | 0 / 0 em 9 projetos |
| `dotnet test tests/PaymentHub.IntegrationTests/PaymentHub.IntegrationTests.csproj` | 14 passando (10 Slice 1-IT + 4 Slice 3-IT) | **14 passando** (10.0s) |
| `dotnet test PaymentHub.slnx --filter "FullyQualifiedName~EndToEnd"` | 4 passando | **4 passando** |
| `dotnet test PaymentHub.slnx` | 422 passando (418 baseline + 4 E2E) | **422 passando** (11.0s) |
| `dotnet test --filter "FullyQualifiedName~AbacatePayCheckoutE2ETests"` | 4 passando | **4 passando** (9.6s) |
| `dotnet test --filter "FullyQualifiedName~ProcessWebhookEventHandlerAbacatePay"` | 9 passando | 9 passando |
| `scripts/agent-architecture-check.sh` | Camadas Clean preservadas | Passou |
| `scripts/agent-docs-check.sh` | harness e OpenCode integros | Passou |
| `git diff --check` | Sem warnings | Sem warnings |

Build warnings: 0 (mantido).

## Gaps residuais

1. **WebhookProcessorWorker nao e hospedado pelo WebApplicationFactory**. Testes E2E invocam `IProcessWebhookEventHandler.ProcessAsync(webhookId, ct)` manualmente. Slice futuro (`Phase 7-IT`) pode enderecar via `WebApplicationFactory.WithWebHostBuilder` + custom `IServer` se houver ganho (evita duplicacao de logica entre teste e worker real). Para o MVP, a abordagem manual e suficiente.

2. **Slice 3-IT NAO cobre dispatcher HTTP outbound real** (`OutboxDispatcherWorker`). Decisao documentada: `OutboxDispatcherWorker` exige multi-instance sweep + `FOR UPDATE SKIP LOCKED` (Phase 7 multi-instancia, fora do MVP). O TestServer nao hospeda workers. Slice 7-IT ou 8-IT podera abordar via WebApplicationFactory separada para o Worker.

3. **Slice 3-IT NAO cobre Stripe/MercadoPago webhook handlers**. Phase 4.

4. **`Bootstrap:Enabled=false` no test impede exercitar o caminho de dev seed**. Slice 6-D ja cobre o helper direto (`DevelopmentDataSeederTests`); slice futuro pode adicionar teste E2E que liga o seeder e verifica que dev tenant e criado.

5. **`brCode`/`brCodeBase64` nao aparecem no DTO publico de checkout**. Campo ja existe em `AbacatePay.CreateTransparentPixResponse` mas o `CreateCheckoutResponseDto` nao expoe. Slice opcional pode adicionar `paymentHub.public.pix.brCode/copyPaste` ao DTO.

## Pendencias para fase 7 multi-instancia (futuro)

- `FOR UPDATE SKIP LOCKED` em `OutboxRepository.GetPendingForDispatchAsync` e `WebhookRepository.GetPendingAsync`.
- Sweep automatico de eventos `Processing` orfaos.
- Testes e2e com 2 instancias simultaneas apontando para o mesmo Postgres (verificar que NAO entregam o mesmo `OutboxEvent` / `WebhookEvent`).

## Dependencias / triggers

- `Microsoft.AspNetCore.Mvc.Testing 10.0.0` adicionado ao `tests/PaymentHub.IntegrationTests/PaymentHub.IntegrationTests.csproj`.
- `ProjectReference` para `src/PaymentHub.Api/PaymentHub.Api.csproj` adicionado.

## Status final dos gaps do phase board

- **P2-2** (Projeto de testes de integracao sem testes e2e): **RESOLVIDO 2026-06-29** (era `PARCIALMENTE RESOLVIDO 2026-06-26` apos Slice 1-IT). 10 + 4 = 14 testes integracao, dos quais 4 sao E2E de fato (passam pela pipeline HTTP real).

Phase 7 mantem `IMPLEMENTING` — o escopo do Phase 7 (Workers, Outbox, processamento assincrono) exige os slices futuros `7-IT` (Worker em TestServer) + 7 multi-instancia + dashboard de monitoramento.

## Specs atualizadas por este slice

- `docs/specs/013-testing-strategy.md` — implicitamente: novos testes E2E satisfazem a estrategia "Testcontainers + WebApplicationFactory".
- `docs/specs/007-inbox-outbox-workers.md` — referencia Worker hospedado fora do TestServer (gap documentado, nao bloqueia MVP).
- `docs/specs/011-security-and-compliance.md` — HMAC byte preservation exige `text`, nao `jsonb`, em `webhook_events.raw_payload` (bug A documentado).

## Recomendacoes para proximos agentes

- Ao adicionar nova coluna que armazena dados brutos para HMAC/signature/hash/checksum, SEMPRE usar `text` (ou `json`, que preserva o original). `jsonb` so quando precisa queries SQL/GIN-index sobre o conteudo.
- Ao adicionar nova chamada `repository.X()` em handler coberto por unit tests com `MockBehavior.Strict`, adicionar setup default em `BuildCommonMocks` ou relaxar para `MockBehavior.Loose`.
- Ao adicionar testes E2E, copiar o padrao: `PaymentHubApiFactory(PostgresFixture fixture)` per-teste + fakes via `ConfigurePrimaryHttpMessageHandler` + `processor.ProcessAsync(webhookId, ct)` manual via `factory.Services.CreateScope()`.
- Ao adicionar WebApplicationFactory em projeto novo, usar `CreateHost(IHostBuilder)` override + `ConfigureHostConfiguration` para config que `Program.cs` le eagerly. NAO usar apenas `ConfigureWebHost + ConfigureAppConfiguration` (chega tarde demais quando o app le config no topo).
