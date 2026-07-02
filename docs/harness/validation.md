# Validation Matrix

Use esta matriz para selecionar validações proporcionais ao escopo da tarefa.

## Validação local

```bash
dotnet restore
dotnet build
dotnet test
```

## Specs e ADRs

- Conferir se a alteração respeita a spec relacionada em `docs/specs`.
- Atualizar spec quando houver mudança de contrato.
- Atualizar ADR quando houver nova decisão arquitetural.

## Build

- `dotnet restore`: restaura dependências.
- `dotnet build`: compila a solução.
- `dotnet test`: executa testes automatizados.

## Docker

```bash
docker compose config
docker compose up -d
```

- Validar que o compose é sintaticamente correto.
- Subir dependências locais quando existirem.

## Banco

Validações futuras, quando EF Core e migrations existirem:

```bash
dotnet ef migrations list
dotnet ef database update
```

- Conferir migrations pendentes.
- Aplicar migrations em ambiente local controlado.

## API

Validações futuras:

```bash
curl http://localhost:5000/health
```

- Conferir health check.
- Conferir Swagger/OpenAPI.
- Validar autenticação server-to-server.
- Validar idempotência em endpoints de criação de pagamento.

## Worker

- Validar consumo de Inbox.
- Validar publicação por Outbox.
- Validar retry e tratamento de falhas.
- Validar logs estruturados.

## Segurança

- Verificar ausência de secrets reais no repositório.
- Verificar que `.env` real não foi commitado.
- Verificar que API Keys são armazenadas como hash.
- Verificar que dados sensíveis não aparecem em logs.
- Verificar validação de assinatura de webhooks quando suportado.

## Slice-specific (Phase 2 / Slice 2-C — AbacatePay webhook management)

Quando a slice altera rotas de webhook de provider ou endpoints de gerenciamento:

- Migration nova nao cria coluna para `webhookSecret`. Confirmar com `dotnet ef migrations list` + diff visual.
- Migration nova mantem `webhook_events` como `text` (NAO `jsonb`) em qualquer tabela. Buscar `HasColumnType("jsonb")` no commit; rejeitar se presente em coluna que armazena JSON do cliente.
- `IntegrationTestFactory.ResetDatabaseAsync` continua truncando `provider_accounts` em ordem topologica reversa; se nova coluna ganhar FK ou indice, atualizar a ordem.
- Filtros de teste cobrem os caminhos novos:
  ```bash
  dotnet test --filter "FullyQualifiedName~ProviderAccountWebhookPersistenceTests"
  dotnet test --filter "FullyQualifiedName~ConfigureProviderAccountWebhookHandlerTests"
  dotnet test --filter "FullyQualifiedName~GetProviderAccountWebhookHandlerTests"
  dotnet test --filter "FullyQualifiedName~ConfigureAbacatePayWebhookRequestValidatorTests"
  dotnet test --filter "FullyQualifiedName~ProviderAccountsWebhookControllerTests"
  ```
- Contratos novos vao em `docs/specs/009-api-contracts.md` antes do PR. Fica proibido inserir `tenantId`/`applicationId` em DTOs de request quando o endpoint for autenticado (re-asserting Slice 6-B).

## Slice-specific (Phase 2 / Slice 2-C.1 — AbacatePay webhook management HTTP client)

Quando a slice substitui o `NoOpProviderWebhookManagementClient` por um client HTTP real ou altera a pipeline de remote registration:

- **`AbacatePayWebhookManagementClient`** NUNCA loga `apiKey`, `webhookSecret`, `Authorization` header, request body, ou response body. Buscar `LogWarning|LogInformation` no commit; rejeitar qualquer log que aceite o `apiKey` ou `webhookSecret` como argumento (mesmo via interpolação).
- **`AbacatePayWebhookManagementClient`** preserva o **4-gate pipeline**: (1) provider check `providerCode == AbacatePay`, (2) feature flag `AllowWebhookRegistration`, (3) pre-flight validation (callbackUrl + events + webhookSecret nao-vazios), (4) apiKey extraction via `IProviderAccountCredentialsReader.ReadApiKey`. Remover ou reordenar qualquer gate re-abre a janela de leak. Buscar `if (providerCode != ProviderCode.AbacatePay)`, `if (!_featurePolicy.IsRemoteRegistrationEnabled(`, e a chamada a `_credentialsReader.ReadApiKey(` no commit.
- **Named HttpClient `abacatepay-webhooks`** permanece dedicado. NUNCA reusar `abacatepay` (que serve transparent-PIX) para o client de webhook management — o BaseAddress + Timeout vem do `AbacatePayOptions` mas o lifecycle operacional e' distinto. Confirmar com `services.AddHttpClient(AbacatePayWebhookManagementClient.HttpClientName, ...)` no commit.
- **`IProviderAccountCredentialsReader`** e' a unica porta publica para extrair `apiKey` de `EncryptedCredentials` cross-layer. NUNCA injetar `ICredentialProtector` diretamente em Infrastructure para fazer o unprotect no client. Buscar `ICredentialProtector` em `Infrastructure.Providers/AbacatePay/`; rejeitar.
- **`NoOpProviderWebhookManagementClient`** foi **removido** em Slice 2-C.1. Re-assertir via `git grep NoOpProviderWebhookManagementClient`; se aparecer em commit novo, rejeitar (registro unico substitui).
- **Categoria `AbacatePayErrorCategory.RegistrationDisabled = 11`** foi adicionada mas NAO e' lancada pelo client real (que prefere retornar `RegistrationFailed`). Categorias lancadas: `BadRequest`/`Unauthorized`/`NotFound`/`RateLimited`/`ServerError`/`Network`/`Timeout`/`EnvelopeFailure`. Buscar `throw new AbacatePayClientException(` no commit; o tipo da exception e o enum da categoria devem seguir o mapeamento documentado em `docs/specs/011-security-and-compliance.md` (tabela "Categorizacao de erros").
- **Migration Slice 2-C (`20260630001726_AddProviderAccountWebhookColumns`)** NAO foi alterada por esta slice. Confirmar com `dotnet ef migrations list` + diff visual que nenhuma migration nova foi criada.
- **DTOs de response do PUT/GET** NUNCA carregam `apiKey`, `webhookSecret`, `protectedWebhookSecret` ou `encryptedCredentials`. Validado por reflexao em `ProviderAccountsWebhookControllerTests.NewWebhookResponse_DoesNotExposeSensitiveMaterial` (Slice 2-C) e re-assertido em `AbacatePayWebhookManagementE2ETests` (Slice 2-C.1).
- **Filtros de teste** cobrem os caminhos novos:
  ```bash
  dotnet test PaymentHub.slnx --filter "FullyQualifiedName~AbacatePayWebhookManagementClientTests"
  dotnet test PaymentHub.slnx --filter "FullyQualifiedName~ProviderAccountsWebhookControllerTests"
  dotnet test PaymentHub.slnx --filter "FullyQualifiedName~AbacatePayWebhookManagementE2ETests"
  dotnet test PaymentHub.slnx --filter "FullyQualifiedName~AbacatePay"
  dotnet test PaymentHub.slnx --filter "FullyQualifiedName~ProviderAccount"
  dotnet test PaymentHub.slnx --filter "FullyQualifiedName~EndToEnd"
  dotnet test tests/PaymentHub.IntegrationTests/PaymentHub.IntegrationTests.csproj
  ```
- **Suite esperada apos Slice 2-C.1: 522 testes (489 unit + 33 integration).** Regressao de teste count abaixo de 518 indica quebra em alguma das suites. Regressao acima de 525 indica teste novo nao contabilizado.
- **Configuracao:** `Providers:AbacatePay:AllowWebhookRegistration` (default `false`). Documentar override em `appsettings.json` apenas em Development. Suite E2E passa `["Providers:AbacatePay:AllowWebhookRegistration"] = "true"` na `PaymentHubApiFactory` para exercitar o caminho real.
- **Anti-flaky em testes E2E:** se a suite flake-ar (1-2% historico), confirmar que o `AbacatePayFakeHttpHandler` esta' registrado para AMBOS os named clients (`abacatepay` E `abacatepay-webhooks`) em `PaymentHubApiFactory.ConfigureTestServices`. Sem isso, o client real faz TCP connect para `abacatepay.fake` que NAO existe e o teste vira 401/connection-refused em vez do 200 esperado.

## Slice-specific (Phase 7 / Slice 7-IT — OutboxDispatcherWorker E2E)

Quando a slice cobre o despacho real do outbox para o webhook interno do `ApplicationClient`:

- NUNCA hospedar `OutboxDispatcherWorker`/`WebhookProcessorWorker` dentro do `WebApplicationFactory`. A Slice 3-IT fixou essa decisao por testabilidade; testes E2E invocam `OutboxDispatcherWorker.DispatchOnceAsync(CancellationToken)` diretamente via `factory.Services` (precisa `InternalsVisibleTo("PaymentHub.IntegrationTests")` em `PaymentHub.Worker.csproj`) e `IProcessWebhookEventHandler.ProcessAsync(webhookId, ct)` para o lado Inbox.
- `WebApplicationFactory<Program>` continua exigindo `CreateHost(IHostBuilder)` override com `ConfigureHostConfiguration` (NAO apenas `ConfigureWebHost`) para sobrescrever `ConnectionStrings:Postgres` antes de `Program.cs` le-lo eagerly. Sem isso, a API conecta no `docker-compose` e nao no Testcontainer.
- `ApplicationWebhookCaptureHandler` deve capturar **todos** os headers `X-PaymentHub-*` (event-id, event-type, timestamp, signature) alem do body bruto. Default de response `204 No Content`; filas programaveis via `EnqueueResponse(HttpStatusCode, reason)` para exercitar 5xx/4xx sem chamada externa real.
- Recomputar `X-PaymentHub-Signature` exige `sha256_hex_lowercase(secret, "{timestamp}.{rawBody}")` — testes E2E devem ter um helper puro (`InternalWebhookHmac.Compute/Matches`) para evitar copiar a logica do `HmacWebhookSigner` em cada classe de teste.
- `LastError` do `OutboxEvent` NAO deve vazar URL, segredo, blob protegido, signature ou body da response. Para `HttpFailure` o formato canonico e `"HttpFailure: status={code}"`; para `UnprotectFailure` apenas o nome do enum (`"UnprotectFailure"`, <= 64 chars). `RetryPolicy.NextRetryAt(retryCount, now)` deve produzir `NextRetryAt` no futuro, e `RetryCount` incrementa exatamente 1 por iteracao com falha.
- `UnprotectFailure` deve abortar ANTES de qualquer HTTP POST — o fake receiver tem que registrar `CallCount == 0` nesse caminho.
- `OutboxDispatcherWorker.DispatchOnceAsync()` filtra apenas `Pending`. Eventos `Sent`/`Processing`/`Failed` NAO sao reenviados; isso vira assercao explicita no teste P2.2.
- `OutboxEvent` persiste `payload` como `jsonb` (`EntityConfigurations.cs:217`) e isso e' proposital: o conteudo do payload e' controlado pelo PaymentHub e indexado em queries internas; NAO trocar para `text` nesta slice (a regra `jsonb -> text` da Slice 2-C vale **somente** para colunas que armazenam corpo bruto de webhook de provider).
- Filtros de teste E2E cobrem os caminhos novos:
  ```bash
  dotnet test PaymentHub.slnx --filter "FullyQualifiedName~OutboxDispatcherE2ETests"
  dotnet test PaymentHub.slnx --filter "FullyQualifiedName~EndToEnd"
  dotnet test tests/PaymentHub.IntegrationTests/PaymentHub.IntegrationTests.csproj
  ```
- Migration nova NAO e' necessaria para esta slice (todo o storage ja existe: `outbox_events.payload`/`status`/`last_error`/`retry_count`/`next_retry_at`/`sent_at`). Mudancas de storage ficam para o Fase 7 multi-instancia (`FOR UPDATE SKIP LOCKED`, sweep de `Processing` orfao).

## Slice-specific (Phase 7 / Slice 7-M1 — Outbox multi-instancia: SKIP LOCKED + sweep de Processing orfao)

Quando a slice cobre claim transacional com `FOR UPDATE SKIP LOCKED`, sweep automatico de `Processing` orfao, ou qualquer mudanca em `OutboxRepository`/`OutboxDispatcherWorker`:

- **`OutboxRepository.ClaimPendingForDispatchAsync` DEVE ser implementado com `NpgsqlConnection` raw + `BeginTransactionAsync(IsolationLevel.ReadCommitted)` + `SELECT ... FOR UPDATE SKIP LOCKED LIMIT @batchSize` + `UPDATE outbox_events SET status='Processing', processing_started_at=@now` em uma unica transacao.** Nao usar `EF Core LINQ` puro: nao traduz `SKIP LOCKED`. A conexao e' obtida via `_db.Database.GetDbConnection()` (EF Core owns the connection) e NAO deve ser disposed. Fechar a conexao apenas se ela estava fechada antes do claim (`connectionWasClosed` flag).
- **`SweepOrphanedProcessingAsync` DEVE usar `ExecuteSqlRawAsync` com parametros nomeados (`@now`, `@cutoff`)** e template SQL estavel para `EXPLAIN`. NAO usar `ExecuteSqlInterpolated` (risco de SQL injection). Filtro obrigatorio: `WHERE status='Processing' AND processing_started_at IS NOT NULL AND processing_started_at < @cutoff`.
- **`last_error` do sweep DEVE ser exatamente o literal `'ProcessingOrphaned'`** (hardcoded no template). NAO usar `ex.Message`, URL, blob, stack trace ou qualquer dado sensivel. Esta politica e' enforced pela propria SQL query; testes confirmam via `LastError.Should().Be("ProcessingOrphaned")`.
- **`next_retry_at` no sweep DEVE ser `NULL` (NAO `@now`)** para garantir que a row e' imediatamente re-disparavel na mesma iteracao do Worker. Se gravarmos `@now`, a comparacao `next_retry_at <= @now` no claim pode falhar por microsegundos, atrasando a entrega em um tick.
- **Worker NAO chama `MarkProcessing` separado.** O claim ja entrega rows em `Processing`. `OutboxDispatcherWorker.DispatchOnceAsync` removeu o `outbox.MarkProcessing()` + `eventStore.SaveAsync()` separado. Qualquer regressao que reintroduza esse padrao re-abre o race window do SKIP LOCKED.
- **Worker faz sanity-check no claim:** se `Status != Processing` ou `ProcessingStartedAt == null`, loga `Error` e pula o dispatch (anti-regressao contra remocao futura acidental do `UPDATE` no claim path).
- **`OutboxEvent.ProcessingStartedAt` e' `timestamptz NULL`, NAO `jsonb`.** E' um timestamp, nao JSON. Nao confunda com a regra `payload` continua `jsonb`.
- **`outbox_events.payload` continua `jsonb`** (decisao Slice 7-IT, NAO regressao). A Slice 7-M1 NAO toca nesta coluna.
- **`webhook_events.raw_payload` continua `text`** (decisao Slice 3-IT, NAO regressao). Idem `provider_accounts.webhook_events` (decisao Slice 2-C).
- **Migration `20260630184619_AddOutboxProcessingStartedAtAndIndexes` e' obrigatoria.** `processing_started_at` e' nullable, NAO tem default. Indices: `(status, next_retry_at, created_at)` substitui `(status, next_retry_at)`; partial `(status, processing_started_at) WHERE status='Processing'` para o sweep. Confirmar com `dotnet ef migrations list` + diff visual que nao ha coluna `jsonb` acidental.
- **`OutboxDispatcherWorker` continua NAO hospedado dentro do `WebApplicationFactory`** (re-asserting Slice 7-IT). Testes invocam `DispatchOnceAsync` via `factory.Services` (`InternalsVisibleTo("PaymentHub.IntegrationTests")` em `PaymentHub.Worker.csproj`). Cada worker usa seu proprio `IServiceScope` + `PaymentHubDbContext` (EF Core DbContext nao e' thread-safe; compartilhar mascara bugs reais).
- **Filtros de teste E2E cobrem os caminhos novos:**
  ```bash
  dotnet test PaymentHub.slnx --filter "FullyQualifiedName~OutboxDispatcherConcurrency"
  dotnet test PaymentHub.slnx --filter "FullyQualifiedName~OutboxProcessingSweep"
  dotnet test PaymentHub.slnx --filter "FullyQualifiedName~OutboxDispatcher"
  dotnet test PaymentHub.slnx --filter "FullyQualifiedName~EndToEnd"
  dotnet test tests/PaymentHub.IntegrationTests/PaymentHub.IntegrationTests.csproj
  ```
- **Suite esperada apos Slice 7-M1: 498 testes (467 unit + 31 integration).** Regressao de teste count abaixo de 495 indica quebra em alguma das suites. Regressao acima de 500 indica teste novo nao contabilizado.
- **Configuracao:** `PaymentHubOptions.OutboxProcessingTimeoutSeconds` (default 900). Documentar override em `appsettings.json` se o ambiente tiver SLO de entrega apertado. Suite E2E passa `Options.Create(new PaymentHubOptions { OutboxProcessingTimeoutSeconds = 1 })` para exercitar o sweep em escala de teste.
- **Anti-flaky em testes de concorrencia:** se P1.1 (`ShouldNotDoubleDispatch_WhenTwoInstancesRunConcurrently`) flake-ar, adicionar `await Task.Delay(10)` entre os dois `DispatchOnceAsync` para que o primeiro worker tenha chance de claimar primeiro. Ainda exercita SKIP LOCKED (segundo worker ve a lock) sem a corrida rara de microsegundo.

## Slice-specific (Phase 9 / Slice 9-O1 — Observabilidade minima: CorrelationId, metrics, logs estruturados)

Quando a slice introduz o catalogo `PaymentHubLogEvents`, instruments `PaymentHubMetrics`, helper `CorrelationIdGenerator`, middleware `CorrelationIdMiddleware`, ou qualquer mudanca em `webhook_events.correlation_id` / `outbox_events.correlation_id`:

- **`CorrelationIdMiddleware` DEVE ser registrado ANTES de `ApiKeyAuthenticationMiddleware`** em `Program.cs`. A ordem garante que logs de 401/403 carreguem `CorrelationId` no `LogContext` e que o response de rejeicao sempre inclua o header `X-Correlation-Id`. Confirmar visualmente em `src/PaymentHub.Api/Program.cs`.
- **Header `X-Correlation-Id` inbound invalido e' substituído silenciosamente** (NAO retorna `400`). `CorrelationIdMiddleware` loga `observability.correlation_id_generated` APENAS com `Request.Path` — NUNCA com o valor recebido (anti-log-injection). Confirmar com `NoLeakLogTests` + `CorrelationIdMiddlewareTests.InvokeAsync_ShouldNotLogTheRejectedValue_WhenSubstituting`.
- **`CorrelationId` em si NAO e' dado sensivel** (e' um GUID-N opaco gerado pelo gateway). Pode ser persistido em `correlation_id VARCHAR(64) NULL` e propagado no header outbound. NAO confunda com `apiKey`/`tenantId`/`applicationId` (esses continuam off-limits em logs e headers outbound).
- **Coluna `correlation_id` permanece `character varying(64) NULL`** em `webhook_events` e `outbox_events`. Cap em Domain via `NormalizeCorrelationId` (constante local `MaxLength = 64`). Migration `20260701000001_AddObservabilityColumns` NAO deve ser alterada por slices subsequentes.
- **`outbox_events.payload` continua `jsonb`** (decisao Slice 7-IT). **`webhook_events.raw_payload` continua `text`** (decisao Slice 3-IT). **`provider_accounts.webhook_events` continua `text`** (decisao Slice 2-C). CorrelationId NAO substitui payload — vai em coluna propria.
- **`PaymentHubMetrics.Tag(...)` rejeita em runtime chaves fora de `AllowedTagKeys`**: `provider`, `operation`, `status`, `error_category`, `event_type`, `environment`, `worker`. Adicionar nova chave exige edicao explicita da whitelist. `apiKey`/`webhookSecret`/`rawPayload`/`signature`/`body`/`Authorization` NUNCA serao tag values.
- **Anti-leak regex gate** em `scripts/agent-docs-check.sh` falha o build quando o regex encontra `Log(Warning|Information|Error|Debug|Critical|Trace)\(...` interpolando os tokens `apiKey`, `webhookSecret`, `rawPayload`, `signature`, `Authorization`, `body`. Atualizar o regex gate E o array `ForbiddenTokens` em `NoLeakLogTests.cs` ao adicionar nova categoria sensivel.
- **`PaymentHubLogEvents` e' a unica fonte canonica** de event names. Chamadas `_logger.LogInformation("checkout.accepted...")` com strings ad-hoc sao proibidas — use `PaymentHubLogEvents.CheckoutAccepted` como template ou componha com `$"{PaymentHubLogEvents.CheckoutAccepted} paymentId={SafeLog.Id(payment.Id)}"`.
- **Worker NAO tem `HttpContext`**, portanto registra `NullCorrelationIdAccessor` (Singleton, retorna null). O fluxo do worker deriva `CorrelationId` da coluna `webhook_events.correlation_id` / `outbox_events.correlation_id` via accessor passado para o handler. NAO injetar `IHttpContextAccessor` no Worker (quebra o `scripts/agent-architecture-check.sh`).
- **Filtros de teste cobrem os caminhos novos:**
  ```bash
  dotnet test PaymentHub.slnx --filter "FullyQualifiedName~CorrelationId"
  dotnet test PaymentHub.slnx --filter "FullyQualifiedName~SafeLog"
  dotnet test PaymentHub.slnx --filter "FullyQualifiedName~PaymentHubMetrics"
  dotnet test PaymentHub.slnx --filter "FullyQualifiedName~NoLeak"
  dotnet test PaymentHub.slnx --filter "FullyQualifiedName~ProviderWebhooks"
  ```
- **Suite esperada apos Slice 9-O1: 547 testes (523 unit + 24 integration).** Regressao abaixo de 540 indica quebra em alguma das suites. Regressao acima de 555 indica teste novo nao contabilizado (esperado: instrumentacao ativa em 9-O2+).
- **Migration nova NAO cria coluna para `webhookSecret`** (re-asserting Slice 2-C). Confirmar via `dotnet ef migrations list` + diff visual.
- **`OutboxEvent.LastError` continua persistindo apenas categoria enum + status code** (decisao Slice 7-A.7). O novo `correlation_id` NAO substitui `LastError` — sao colunas ortogonais.
- **`HttpApplicationWebhookDispatcher` adiciona `X-Correlation-Id` no request outbound** derivado de `outboxEvent.CorrelationId`. NUNCA adicione `Authorization`/`X-Api-Key` no request ao consumidor (esse header nao existe — o consumidor assina o body com o segredo armazenado em `ApplicationClient.WebhookSecret`).
- **Anti-flaky em testes E2E de correlation:** se o teste `CorrelationIdE2ETests.ShouldPropagateInboundCorrelationId_ToOutboxRow` flake-ar, confirmar que `CorrelationIdMiddleware` esta' registrado na ordem correta em `Program.cs`. Sem isso o accessor retorna null e o teste falha com `Should().Be(seededId)`.
