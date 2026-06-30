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
