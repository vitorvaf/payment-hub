# Agent Progress

Use este arquivo para tarefas com mais de um passo. Mantenha entradas curtas e verificaveis.

## Historico

### 2026-06-29 - Slice 3-IT fechado (conclusao + 2 producao bugs descobertos por E2E)

- Data: 2026-06-29
- Agente/superficie: OpenCode (Implementer)
- Sub-slices entregues: 3-IT.1 (PaymentHubApiFactory + AbacatePayFakeHttpHandler + ApplicationWebhookCaptureHandler + E2ESeedHelpers), 3-IT.2 (4 testes P1 E2E), 3-IT.3 (jsonb->text em `webhook_events.raw_payload` + migracao `20260629205545_ChangeRawPayloadToText`), 3-IT.4 (`_payments.AddAttemptAsync(attempt, ct)` explicito no `ProcessWebhookEventHandler.ProcessAsync`), 3-IT.5 (mock setup em `ProcessWebhookEventHandlerAbacatePayTests.BuildCommonMocks`).
- Q1-Q7 respondidas em `docs/audits/slice-3-it-e2e-api-postgres-outbox-provider-report-2026-06-29.md`.
- 2 producao bugs descobertos por E2E testing (unit tests nao cobriam o roundtrip via Postgres ou o change detector do EF Core 10):
  - **(a)** `webhook_events.raw_payload` era `jsonb`, que reformata JSON (insere espacos, normaliza chaves) no insert e quebra HMAC sobre o body bruto. Migracao `jsonb -> text` aplicada; coluna agora preserva bytes exatos.
  - **(b)** `ProcessWebhookEventHandler.ProcessAsync` chamava apenas `payment.RegisterAttempt(...)` (que faz `_attempts.Add(attempt)` na collection navigation privada) — o EF Core 10 nao detecta confiavelmente o novo item como Added em testes, classificando-o como Modified e levantando `DbUpdateConcurrencyException` no UPDATE subsequente (0 rows afetadas, Guid novo sem row). Solucao: chamar `_payments.AddAttemptAsync(attempt, ct)` apos `RegisterAttempt(...)`, garantindo o tracking correto.
- Validacao final: `dotnet build PaymentHub.slnx` (0/0 em 9 projetos); `dotnet test PaymentHub.slnx` (422 passed: 418 baseline + 4 E2E); `dotnet test PaymentHub.IntegrationTests.csproj` (14 passed: 10 Slice 1-IT + 4 Slice 3-IT); `scripts/agent-architecture-check.sh` e `scripts/agent-docs-check.sh` verdes; `git diff --check` limpo.
- Phase 7 mantem `IMPLEMENTING` (Slice 3-IT enderecou e2e API+Postgres+Provider; ainda faltam slices para OutboxDispatcherWorker hospedado em TestServer e multi-instancia em Phase 7-IT).
- P2-2 do phase board (`Projeto de testes de integracao sem testes e2e`) passa de `PARCIALMENTE RESOLVIDO` para `RESOLVIDO 2026-06-29`.
- Docs atualizadas: `docs/harness/validation-matrix.md`, `docs/roadmap/001-development-timeline.md`, `docs/roadmap/002-phase-status-board.md`, `docs/harness/learnings.md`, `feature_list.md`.

### 2026-06-29 - Slice 2-B fechado (conclusao + commit)

- Data: 2026-06-29
- Agente/superficie: OpenCode (Implementer)
- Sub-slices entregues: 2-B.1, 2-B.2, 2-B.3, 2-B.4, 2-B.5, 2-B.6, 2-B.7.
- Q1-Q5 respondidas em `docs/audits/slice-2b-abacatepay-webhooks-report-2026-06-29.md`.
- Validacao final: `dotnet build` (0/0); `dotnet test` (418 passed); `scripts/agent-architecture-check.sh`, `scripts/agent-docs-check.sh`, `scripts/agent-smoke.sh` todos verdes; `git diff --check` limpo.
- Phase 2 passa a `IMPLEMENTED`. Phase 3 mantem `IMPLEMENTING` ate webhooks de Stripe/MercadoPago (Phase 4).
- Gap P2-1 (assinatura de webhooks externos em adapter real) resolvido para AbacatePay.
- Commit: `feat(provider): process AbacatePay external webhooks (Slice 2-B)`.
- Push: `origin dev`.

## Entrada atual

### Slice 4-H — Proximo slice apos Slice 3-IT

Status: PLANEJADO

## Objetivo

Validar o fluxo completo do Payment Hub em integracao usando API/TestServer, Postgres real e providers fakeados, sem chamadas externas reais.

## Discovery

### Estado atual dos testes de integracao

- `tests/PaymentHub.IntegrationTests` ja possui base do Slice 1-IT com `PostgresFixture`, `PostgresCollection`, `IntegrationTestFactory`, migrations via Testcontainers (`postgres:16-alpine`) e reset por `TRUNCATE ... RESTART IDENTITY CASCADE`.
- A fixture atual usa DI manual minimo e referencia `PaymentHub.Domain`, `PaymentHub.Application` e `PaymentHub.Infrastructure.Postgres`; ainda nao referencia `PaymentHub.Api`, `PaymentHub.Infrastructure.Providers` nem `Microsoft.AspNetCore.Mvc.Testing`.
- Testes existentes cobrem migrations, repositorios principais, protecao de `WebhookSecret`, `ProviderAccount`, Outbox persistence e query de pendentes. Ainda nao ha teste HTTP/API end-to-end.

### Estado atual da API/TestServer

- `src/PaymentHub.Api/Program.cs` termina com `public partial class Program { }`, entao `WebApplicationFactory<Program>` e viavel.
- A API registra `AddPaymentHubPostgres`, `AddPaymentHubProviders`, controllers, validators, middleware de API Key, handlers de checkout/webhook/pagamentos e seeder de desenvolvimento.
- O factory de teste deve sobrescrever `ConnectionStrings:Postgres`, `PaymentHub:*`, `Providers:AbacatePay:*` e `Bootstrap:*` via configuracao in-memory, com `Bootstrap:Enabled=false` para evitar seed automatico.
- Para nao chamar internet, o teste deve substituir os named HttpClients `abacatepay` e, se usado, `application-webhook` por handlers fake/capturados.

### Fluxo create checkout

- `POST /api/v1/checkouts` exige `Authorization: Bearer <api_key>`, `X-Tenant-Id`, `X-Application-Id` e `Idempotency-Key`.
- `ApiKeyAuthenticationMiddleware` valida hash da API key, tenant/application ativos e popula `ITenantContext`.
- `CreateCheckoutHandler` resolve `ProviderAccount` por `X-Provider` explicito ou default da application, passa `ProviderAccountId`, `ProviderEnvironment` e `EncryptedCredentials` para `CreateCheckoutProviderRequest`.
- `AbacatePayProviderAdapter` desprotege credenciais, extrai `apiKey`, chama `IAbacatePayClient.CreateTransparentPixAsync`, persiste `Payment`, `PaymentAttempt`, `IdempotencyKey` e cria `OutboxEvent` `payment.checkout.created`.
- A resposta publica atual de checkout contem `paymentId`, `status`, `provider` e `checkoutUrl`; `brCode`/`brCodeBase64` ficam em `RawResponseJson` do adapter e nao fazem parte do DTO publico atual. O teste deve validar o contrato atual e registrar o gap se campos PIX publicos forem exigidos.

### Fluxo webhook externo

- Endpoint real: `POST /api/v1/webhooks/{providerCode}`; o middleware libera `/api/v1/webhooks/` sem API Key.
- `ProviderWebhooksController` le raw body, exige `X-Webhook-Signature` para AbacatePay antes de persistir e chama `IReceiveProviderWebhookHandler`.
- `ReceiveProviderWebhookHandler` apenas persiste ou deduplica `WebhookEvent`; ele nao processa o evento nem aciona worker dentro do request HTTP.
- Para E2E deste slice, o teste deve enviar o POST real para a API e depois executar `IProcessWebhookEventHandler.ProcessAsync(webhookId)` em scope do factory para simular a iteracao do worker de Inbox, sem alterar comportamento de producao.

### Fluxo Inbox/Outbox

- Deduplicacao de Inbox usa `providerCode + providerEventId` quando `X-Provider-Event-Id` esta presente. Para AbacatePay, os testes devem enviar o id do envelope tambem nesse header para exercitar a politica existente.
- `ProcessWebhookEventHandler` marca `WebhookEvent` como `Processing`, resolve `ProviderAccount` AbacatePay por metadata `data.metadata.{tenantId,applicationId,paymentId}`, desprotege `webhookSecret`, chama o adapter, aplica status canonico no `Payment`, registra `PaymentAttempt` e cria `OutboxEvent` apenas quando ha mudanca de status.
- Webhook duplicado deve retornar o mesmo `webhookId` e nao gerar novo processamento efetivo nem novo `OutboxEvent`.
- Webhook sem assinatura AbacatePay deve retornar `401` antes de qualquer gravacao em `webhook_events`.

### Estrategia de fake provider

- Usar o `AbacatePayProviderAdapter` real e o `AbacatePayClient` real no principal teste E2E.
- Fakear somente o transporte HTTP externo com `HttpMessageHandler` associado ao named client `abacatepay`.
- O fake deve capturar request, validar `POST /transparents/create`, `Authorization: Bearer test-abacatepay-api-key`, metadata e amount, e responder envelope `success=true` com `id`, `status`, `brCode`, `brCodeBase64`, `expiresAt` e `devMode`.
- Credenciais do `ProviderAccount` devem usar JSON fake protegido por `ICredentialProtector`, por exemplo `{ "apiKey": "test-abacatepay-api-key", "webhookSecret": "test-abacatepay-webhook-secret" }`.

### Estrategia de fake ApplicationClient webhook receiver

- P2 neste slice: se couber sem refactor amplo, substituir o named client `application-webhook` por handler fake/capturado e exercitar `IApplicationWebhookDispatcher` real ou uma iteracao testavel do dispatcher.
- `OutboxDispatcherWorker.DispatchOnceAsync` e `internal`; se `InternalsVisibleTo("PaymentHub.IntegrationTests")` for pequeno e seguro, pode ser avaliado. Caso contrario, testar o dispatcher real diretamente via DI e registrar Worker completo para Slice 7-IT.
- Se o dispatcher interno ficar fora do slice, o P1 continua sendo provar que o `OutboxEvent` interno foi criado com payload seguro e correto.

### Testes previstos

- P1: `CreateCheckout_ShouldCreateTransparentPix_WithAbacatePayFake_AndPersistAttempt`.
- P1: `ProviderWebhook_ShouldValidateAbacatePaySignature_UpdatePayment_AndCreateOutbox`.
- P1: `ProviderWebhook_ShouldBeIdempotent_WhenSameAbacatePayEventIsReceivedTwice`.
- P1: `ProviderWebhook_ShouldRejectAbacatePayWithoutSignature_BeforePersisting`.
- P2: `OutboxDispatcher_ShouldSendInternalWebhook_ToApplicationClientReceiver`, se viavel sem refactor amplo.
- P2: `ProviderWebhook_ShouldHandleUnknownPaymentSafely`, se couber apos os quatro P1.

### Riscos e gaps

- TestServer nao roda o `WebhookProcessorWorker`; a execucao explicita de `IProcessWebhookEventHandler.ProcessAsync` no teste sera registrada como decisao de testabilidade do Slice 3-IT.
- `OutboxDispatcherWorker.DispatchOnceAsync` nao e publico para integration tests. Evitar refactor amplo; se necessario, documentar dispatcher completo para Slice 7-IT.
- A resposta publica de checkout ainda nao expoe `brCode`/`brCodeBase64`; o teste deve validar ausencia de credential/apiKey e o contrato atual, registrando gap de API se campos PIX forem exigidos.
- `src/PaymentHub.Api/appsettings.json` ainda nao tem placeholder production de `PaymentHub`; o factory de teste deve fornecer valores fake em memoria sem commitar segredo real.
- Assinatura invalida AbacatePay atualmente e persistida como `WebhookEvent` e falha no processamento, porque o controller so consegue fail-fast para assinatura ausente; o teste deve cobrir o comportamento real ou registrar a politica.

## Plano de implementacao

1. Adicionar suporte de `WebApplicationFactory<Program>` no projeto de integracao, com referencias/pacotes minimos e sem alterar contratos publicos.
2. Criar `PaymentHubApiFactory` reutilizando `PostgresFixture`, sobrescrevendo configuracao da API para Postgres real, chaves fake, AbacatePay fake e bootstrap desligado.
3. Criar fakes de HTTP para AbacatePay e, opcionalmente, para webhook interno de `ApplicationClient`.
4. Adicionar helpers de seed para tenant/application/API key/provider account com secrets fake protegidos por `ICredentialProtector`/`IWebhookSecretProtector`.
5. Implementar os quatro testes P1 E2E com isolamento por `ResetDatabaseAsync` e ids unicos por teste.
6. Se couber sem refactor amplo, adicionar teste P2 do dispatcher interno; caso contrario, registrar gap no relatorio do slice.
7. Atualizar docs minimas: `docs/harness/validation-matrix.md`, roadmaps 001/002, `docs/harness/learnings.md` quando houver aprendizado reutilizavel, `feature_list.md` se houver gap, `agent-progress.md` e relatorio `docs/audits/slice-3-it-e2e-api-postgres-outbox-provider-report-2026-06-29.md`.

## Validacoes planejadas

- `git status --short`.
- `dotnet restore PaymentHub.slnx`.
- `dotnet build PaymentHub.slnx`.
- `dotnet test PaymentHub.slnx`.
- `dotnet test tests/PaymentHub.IntegrationTests/PaymentHub.IntegrationTests.csproj`.
- `dotnet test PaymentHub.slnx --filter "FullyQualifiedName~EndToEnd"`.
- `dotnet test PaymentHub.slnx --filter "FullyQualifiedName~AbacatePay"`.
- `dotnet test PaymentHub.slnx --filter "FullyQualifiedName~Webhook"`.
- `dotnet test PaymentHub.slnx --filter "FullyQualifiedName~Outbox"`.
- `scripts/agent-architecture-check.sh`.
- `scripts/agent-docs-check.sh`.
- `git diff --check`.
- Se existir e couber no tempo: `scripts/agent-verify.sh` e `RUN_DOTNET_VALIDATION=1 scripts/agent-verify.sh`.

### Slice 2-B — AbacatePay webhooks externos e normalizacao de eventos

Status: CONCLUIDO 2026-06-29 (Slice 2-B.7 — docs, roadmap, learnings, audit report fechados; commit + push pendentes)

Sub-slices concluidos:

- 2-B.1 — `IAbacatePayWebhookSignatureVerifier` + `HmacAbacatePayWebhookSignatureVerifier` (HMAC-SHA256 base64 sobre body UTF-8, `CryptographicOperations.FixedTimeEquals`).
- 2-B.2 — `AbacatePayWebhookEnvelope` + `AbacatePayTransparentWebhookData` (Models).
- 2-B.3 — `IAbacatePayWebhookNormalizer` + `AbacatePayWebhookNormalizer` (suporte a `transparent.completed|refunded|disputed|lost`, decisoes de mapeamento documentadas via `MapEvent`).
- 2-B.4 — `ProviderWebhookRequest` estendido com `ProviderAccountId` e `WebhookSecret` (init-only, backward-compatible). `AbacatePayProviderAdapter.ParseWebhookAsync` reescrito para consumir verifier + normalizer com 4 categorias de erro controlado; nem secret, nem signature, nem raw body aparecem em `ErrorMessage`.
- 2-B.5 — `ProcessWebhookEventHandler` carrega `ProviderAccount` por `(tenantId, applicationId, providerCode)` via `metadata.{tenantId,applicationId,paymentId}` do payload (sem varrer tenants). Desprotege `EncryptedCredentials` via `ICredentialProtector`, extrai `webhookSecret` (preferindo campo `webhookSecret`, caindo para `secret` legacy). ProviderAccount/secret nao-AbacatePay seguem caminho legacy sem exigir HMAC. `WebhookEvent` nao persiste secret, apiKey, body.
- 2-B.6 — `ProviderWebhooksController` faz fail-fast 401 para `AbacatePay` sem `X-Webhook-Signature` (case-insensitive); aceita tambem `X-Provider-Signature` legacy; AbacatePay tem preferencia quando ambos chegam; outros providers preservam comportamento. Headers raw sao lidos via `[FromHeader]`. Handler nunca e chamado para AbacatePay sem assinatura.
- 2-B.7 — Documentacao final fechada: `docs/specs/006-provider-webhooks.md`, `docs/specs/008-provider-adapters.md` e `docs/specs/011-security-and-compliance.md` atualizadas; `docs/roadmap/001-development-timeline.md` (Phase 2 → IMPLEMENTED), `docs/roadmap/002-phase-status-board.md` (Phase 2 IMPLEMENTED, Phase 3 IMPLEMENTING, gap P2-1 resolvido); `docs/harness/validation-matrix.md` (16 linhas Phase 2/2-B); `docs/harness/learnings.md` (entrada nova com 9 recomendacoes); `feature_list.md` (PH-SEC-002 → Concluido; PH-PROVIDER-WEBHOOK-ABACATEPAY novo); `docs/audits/slice-2b-abacatepay-webhooks-report-2026-06-29.md` criado com 5 Q&A respondidas.

Validacao final (2026-06-29):

- `dotnet build PaymentHub.slnx` → 0 errors / 0 warnings (9 projetos).
- `dotnet test --filter "FullyQualifiedName~AbacatePay"` → 125 passed.
- `dotnet test --filter "FullyQualifiedName~Webhook"` → 193 passed.
- `dotnet test --filter "FullyQualifiedName~Provider"` → 135 passed.
- `dotnet test PaymentHub.slnx` → 418 passed (suite completa).
- `scripts/agent-architecture-check.sh` → Architecture check passed.
- `scripts/agent-docs-check.sh` → Docs check passed.
- `scripts/agent-smoke.sh` → Agent smoke checks passed (build + docker compose config).
- `git diff --check` → limpo.

Arquivos tocados:

- `src/PaymentHub.Application/Abstractions/Providers/ProviderModels.cs` (+ `WebhookSecret` e `ProviderAccountId`).
- `src/PaymentHub.Infrastructure.Providers/AbacatePay/AbacatePayProviderAdapter.cs` (ass + verifier + normalizer; `ParseWebhookAsync` reescrito).
- `src/PaymentHub.Infrastructure.Providers/ProvidersServiceCollectionExtensions.cs` (DI dos webhooks services).
- `src/PaymentHub.Infrastructure.Providers/AbacatePay/Webhooks/*` (novo pacote: 6 arquivos).
- `src/PaymentHub.Infrastructure.Providers/AbacatePay/Models/AbacatePayWebhookEnvelope.cs` + `Models/AbacatePayTransparentWebhookData.cs` (novos).
- `src/PaymentHub.Application/Webhooks/WebhookHandlers.cs` (ResolveAbacatePayWebhookSecretAsync + metadata routing + sanitizacao).
- `src/PaymentHub.Api/Controllers/ProviderWebhooksController.cs` (fail-fast 401 para AbacatePay).
- `tests/PaymentHub.UnitTests/Application/ProcessWebhookEventHandlerTests.cs` (construtor atualizado).
- `tests/PaymentHub.UnitTests/Application/ProcessWebhookEventHandlerAbacatePayTests.cs` (novo, 9 testes).
- `tests/PaymentHub.UnitTests/Infrastructure/Providers/AbacatePay/AbacatePayProviderAdapterWebhookTests.cs` (novo, 18 testes).
- `tests/PaymentHub.UnitTests/Infrastructure/Providers/AbacatePay/AbacatePayProviderAdapterTests.cs` (construtor + 1 teste legacy de parse).
- `tests/PaymentHub.UnitTests/Infrastructure/Providers/AbacatePay/Webhooks/*` (novo, 10 + 14 testes).
- `tests/PaymentHub.UnitTests/Api/ProviderWebhooksControllerTests.cs` (novo, 9 testes).
- `docs/specs/006-provider-webhooks.md` (nova secao AbacatePay HMAC).
- `docs/specs/008-provider-adapters.md` (nova sub-secao 2-B).
- `docs/specs/011-security-and-compliance.md` (nova secao HMAC externo AbacatePay).
- `docs/roadmap/001-development-timeline.md` (Phase 2 IMPLEMENTED).
- `docs/roadmap/002-phase-status-board.md` (Phase 2 IMPLEMENTED, Phase 3 IMPLEMENTING, gap P2-1 resolvido).
- `docs/harness/validation-matrix.md` (16 linhas Phase 2/2-B).
- `docs/harness/learnings.md` (entrada nova com 9 recomendacoes).
- `feature_list.md` (PH-SEC-002 → Concluido; PH-PROVIDER-WEBHOOK-ABACATEPAY novo).
- `docs/audits/slice-2b-abacatepay-webhooks-report-2026-06-29.md` (novo, 5 Q&A respondidas).

Riscos residuais / fora-de-escopo:

- Validacao HMAC completa fica no `ProcessWebhookEventHandler` + adapter (apos resolucao de `ProviderAccount`). Controller faz fail-fast so para header ausente (decisao documentada em 2-B.6 para evitar dupla-resolucao).
- Adapters de Stripe/MercadoPago permanecem skeletons; quando evoluirem, copiar o padrao de 2-B.4/2-B.5 + adicionar verificacao HMAC especifica no controller e no router.
- Front-end de dashboard de registro de webhooks (criacao via `POST /webhooks/create`) nao foi implementado nesta fatia.
- Multi-instancia: handler atual NAO usa `FOR UPDATE SKIP LOCKED` em `WebhookEvent` (modelo single-instance). Enderecado em Slice 7-IT multi-instancia.

Proximo slice recomendado:

- **Slice 2-C** ou **Slice 3-IT** (testes end-to-end com WebApplicationFactory apontando para Postgres real).

## Discovery

### Estado atual do endpoint de webhooks externos

- Endpoint existente: `POST /api/v1/webhooks/{providerCode}`.
- `ApiKeyAuthenticationMiddleware` libera `/api/v1/webhooks/` sem API Key de `ApplicationClient`.
- `ProviderWebhooksController` ja le raw body com `Request.EnableBuffering()` e `StreamReader`, tenta inferir `eventType` e chama `IReceiveProviderWebhookHandler`.
- Assinatura externa hoje e apenas recebida pelo header generico `X-Provider-Signature`; nao ha validacao real.
- Para AbacatePay, a documentacao atual usa `X-Webhook-Signature`. O endpoint deve aceitar esse header e validar antes de persistir efeitos.

### Estado atual de Inbox/Outbox

- `ReceiveProviderWebhookHandler` persiste `WebhookEvent` e deduplica por `providerCode + providerEventId` quando o event id existe.
- `ProcessWebhookEventHandler` processa via Worker: resolve adapter, chama `ParseWebhookAsync`, localiza `Payment`, aplica status canonico, registra `PaymentAttempt` e cria `OutboxEvent` quando ha mudanca de status.
- `WebhookEvent.Signature` existe e pode persistir assinatura em claro. Para AbacatePay, preferir validar e nao persistir assinatura se nao for necessaria para reprocessamento.
- `OutboxEvent` ja segue o padrao do Slice 7-A e o dispatcher interno real notifica `ApplicationClient` via Worker.

### Contrato atual da AbacatePay

- Documentacao consultada no discovery: `https://docs.abacatepay.com/llms.txt`, `https://docs.abacatepay.com/pages/webhooks`, `https://docs.abacatepay.com/pages/webhooks/reference`, `https://docs.abacatepay.com/pages/webhooks/create`, `https://docs.abacatepay.com/pages/transparents/reference` e `https://docs.abacatepay.com/pages/transparents/check`.
- Header de assinatura documentado: `X-Webhook-Signature`.
- Algoritmo: HMAC-SHA256.
- Conteudo assinado: raw body UTF-8, sem reserializar JSON.
- Formato da assinatura: base64.
- Payload v2 base: `{ id, event, apiVersion, devMode, data }`.
- Eventos `transparent.*` documentados: `transparent.completed`, `transparent.refunded`, `transparent.disputed`, `transparent.lost`.
- `transparent.expired` nao apareceu na referencia atual; registrar como gap se nao houver outra fonte.

### Estrategia de assinatura HMAC

- Criar `IAbacatePayWebhookSignatureVerifier` e `AbacatePayWebhookSignatureVerifier`, preferencialmente em `PaymentHub.Infrastructure.Providers/AbacatePay/`.
- Verificar HMAC-SHA256 base64 sobre o raw body exatamente recebido.
- Usar comparacao constant-time (`CryptographicOperations.FixedTimeEquals`).
- Rejeitar assinatura ausente, invalida ou base64 invalido sem exception nao tratada e sem leak de segredo.
- Divergencia da doc: o exemplo chama a chave de `ABACATEPAY_PUBLIC_KEY`, mas a API de criacao de webhook exige `secret`; decisao planejada: usar o `secret` configurado no webhook, armazenado em `ProviderAccount.EncryptedCredentials`.

### Estrategia de idempotencia

- Chave preferencial: `providerCode + eventId`, com `eventId = payload.id`.
- Duplicata deve retornar 2xx e nao reprocessar efeitos colaterais.
- Para AbacatePay v2, payload sem `id` deve ser tratado como payload invalido ou gap explicito; nao usar hash de payload como unica chave quando `id` deveria existir.

### Estrategia de normalizacao

- Criar modelos minimos de webhook AbacatePay, por exemplo `AbacatePayWebhookEnvelope`, `AbacatePayTransparentWebhookData` e `AbacatePayWebhookMetadata`.
- Extrair apenas campos necessarios: `eventId`, `eventType`, `providerPaymentId`, `providerStatus`, `amount`, `occurredAt`, `tenantId`, `applicationId`, `paymentId` e `externalReference` quando presentes.
- Nao modelar toda a API AbacatePay.
- Eventos planejados: `transparent.completed`, `transparent.refunded`, `transparent.disputed` e `transparent.lost`.
- Mapeamento planejado: `completed -> Approved`, `refunded -> Refunded`, `disputed -> Pending` ou `Chargeback` conforme decisao documentada no teste/spec, `lost -> Chargeback` ou `Failed` conforme decisao documentada.
- Se `event` e `data.status` divergirem, preferir status explicito reconhecido quando seguro; divergencia desconhecida vira erro controlado ou status conservador documentado.

### Arquivos previstos

- `src/PaymentHub.Api/Controllers/ProviderWebhooksController.cs`.
- `src/PaymentHub.Application/Webhooks/WebhookHandlers.cs`.
- `src/PaymentHub.Application/Abstractions/Providers/ProviderModels.cs` se o resultado de parse precisar carregar dados normalizados extras.
- `src/PaymentHub.Infrastructure.Providers/AbacatePay/AbacatePayProviderAdapter.cs`.
- `src/PaymentHub.Infrastructure.Providers/AbacatePay/AbacatePayOptions.cs` se configurar `WebhookSignatureHeader` fizer sentido.
- `src/PaymentHub.Infrastructure.Providers/AbacatePay/Models/*`.
- `src/PaymentHub.Infrastructure.Providers/ProvidersServiceCollectionExtensions.cs` para registrar verifier/normalizer.
- `tests/PaymentHub.UnitTests/Infrastructure/Providers/AbacatePay/*`.
- `tests/PaymentHub.UnitTests/Application/ProcessWebhookEventHandlerTests.cs` e/ou testes novos para receive/endpoint.
- `tests/PaymentHub.IntegrationTests/*` se couber um fluxo minimo com Postgres.
- Docs/specs/roadmap/report do slice: specs 006/007/008/011, roadmap 001/002, validation matrix, learnings se houver aprendizado reutilizavel, e `docs/audits/slice-2b-abacatepay-webhooks-report-2026-06-27.md`.

### Testes previstos

- Signature verifier: assinatura valida aceita; invalida rejeita; body alterado invalida; ausente rejeita; base64 invalido rejeita sem exception nao tratada; mensagem nao vaza secret.
- Parser/normalizer: `transparent.completed`, `transparent.refunded`, `transparent.disputed`, `transparent.lost`, evento desconhecido, JSON malformado, metadata e providerPaymentId extraidos.
- Endpoint/handler: assinatura valida retorna 2xx; assinatura invalida retorna 401/400; raw body e usado; duplicata retorna 2xx; payload malformado retorna 400; evento desconhecido segue politica documentada.
- Inbox/Outbox: evento novo cria `WebhookEvent`; duplicata nao gera efeito duplicado; evento aprovado atualiza `Payment`/`PaymentAttempt`; evento aprovado cria `OutboxEvent`; evento sem payment vira erro controlado sem Outbox.
- Regressao: filtros `AbacatePay`, `Webhook`, `Inbox`, `Outbox` e integracao Postgres do Slice 1-IT.

### Riscos e decisoes abertas

- Para obter o secret antes de validar HMAC, sera necessario usar metadata ou providerPaymentId do payload apenas como roteamento nao confiavel; nenhum efeito colateral deve ocorrer antes da validacao.
- `ProviderAccount.EncryptedCredentials` hoje armazena `{ apiKey, secret }`; para este slice, tratar `secret` como webhook secret AbacatePay. Futuro: aceitar campo explicito `webhookSecret` sem quebrar o formato existente.
- Sem timestamp documentado pela AbacatePay, anti-replay completo fica limitado a idempotencia por `eventId`.
- `transparent.lost` existe na referencia atual; incluir se couber no slice e registrar decisao de mapeamento. `transparent.expired` nao foi confirmado.
- Evitar persistir assinatura HMAC em claro em Inbox/Outbox. Raw body pode ser persistido como Inbox se nao contiver secrets; nao persistir API key, webhook secret ou assinatura.
- Nao alterar dispatcher interno do Slice 7-A salvo ajuste minimo e justificado.

## Historico

Registre entradas concluídas abaixo quando fizer sentido manter rastreabilidade no repositorio.

### 2026-06-27 - Slice 2-A — AbacatePay sandbox funcional (Implementer)

- Data: 2026-06-27
- Agente/superficie: OpenCode (Implementer)
- Objetivo: implementar o primeiro adapter funcional AbacatePay para Checkout Transparente PIX em sandbox/devMode, com client HTTP tipado, Bearer Token, criacao PIX, consulta de status, simulacao opt-in devMode, mapeamento de status e testes unitarios sem chamada externa real.
- Pre-condicao verificada: Slice 1-IT commitado em `c24b86b` em `origin/dev`.
- Fora de escopo: assinaturas, checkout hospedado AbacatePay com produtos, links reutilizaveis, cupons, payouts, transferencias PIX, boleto, cartao, webhooks externos completos, painel admin, conciliacao, Worker/dispatcher interno, migrations grandes, chamadas reais em testes padrao.
- Decisoes principais (todas as 5 decisoes do planner contract aceitas):
  - **Decisao 1**: passar `ProviderAccount`/credencial protegida via `CreateCheckoutProviderRequest` (init-only opcionais, backward-compat com Fake/Stripe/MercadoPago). NAO injetar repositorio scoped em adapter Singleton.
  - **Decisao 2**: implementar `CheckTransparentPixAsync` em `IAbacatePayClient` + adapter concreto, sem alterar `IPaymentProviderAdapter` neste slice.
  - **Decisao 3**: NAO expor `brCode`/`brCodeBase64` na response publica de checkout neste primeiro corte; carregar em `RawResponseJson` e registrar gap de API.
  - **Decisao 4**: `simulate-payment` apenas quando `AllowDevModeSimulation=true`; default `false` em `appsettings.json`, `true` em `appsettings.Development.json`.
  - **Decisao 5**: webhooks externos/HMAC ficam em **Slice 2-B**.
- Implementacao (14 arquivos novos + 10 alterados):
  - **Novos (14)**: `AbacatePayOptions.cs`, `AbacatePayErrorCategory.cs`, `AbacatePayClientException.cs`, `IAbacatePayClient.cs`, `AbacatePayClient.cs`, 6 models em `Models/` (envelope/customer/create-req/create-resp/check-resp/simulate-resp), `tests/.../AbacatePayClientTests.cs` (40 testes), `tests/.../AbacatePayProviderAdapterTests.cs` (17 testes), `tests/Support/FakeCredentialProtector.cs`, `docs/audits/slice-2a-abacatepay-sandbox-report-2026-06-26.md`.
  - **Alterados (10)**: `PaymentHub.Infrastructure.Providers.csproj` (+`Microsoft.Extensions.Http 10.0.0`), `AbacatePayProviderAdapter.cs` (reescrito: unprotect → extract → payload → mapping), `ProvidersServiceCollectionExtensions.cs` (options + named HttpClient + Singleton client), `ProviderModels.cs` (+3 init-only), `CreateCheckoutHandler.cs` (novo `ResolvedProvider` record + `ResolveProviderAsync` retorna account context), `PaymentStatusMapper.cs` (+`redeemed → Approved`, +`under_dispute → Pending`), `appsettings.json` + `appsettings.Development.json` (secao `Providers:AbacatePay`), `PaymentStatusMapperTests.cs` (+4 metodos cobrindo todos os status do mapping).
- Specs/docs/ADRs atualizados:
  - `docs/specs/008-provider-adapters.md` (tabela MVP atualizada + secao AbacatePay com error category, status mapping, contrato de testes).
  - `docs/specs/004-payment-lifecycle.md` (subsecao Provider AbacatePay com endpoints reais, mapping estendido, caminho canonico de criacao).
  - `docs/specs/011-security-and-compliance.md` (subsecao `Protecao de credenciais AbacatePay em fluxo outbound` com politica de `AbacatePayClientException` segura, tabela de categorias, simulacao opt-in).
  - `docs/roadmap/001-development-timeline.md` (Phase 2 `IMPLEMENTING (Slice 2-A CONCLUIDO 2026-06-27)`; lista de slices recomendados atualizada).
  - `docs/roadmap/002-phase-status-board.md` (dashboard Phase 2 + indicador `Providers reais funcionais = 1`; Bloco C fechado; chamada para Slice 2-B; referencia ao relatorio).
  - `docs/harness/validation-matrix.md` (Phase 2 AbacatePay: 11 linhas de `PENDING` → `PASS`).
  - `docs/harness/learnings.md` (entrada nova cobrindo 9 decisoes reaproveitaveis: resolver-as-value-object, request DTO init-only, IOptionsMonitor + named HttpClient + Singleton client, safe-envelope + categorized exception, Bearer via IHttpClientFactory, status mapping estendido com decisao documentada, FakeCredentialProtector para testes, BaseAddress path gotcha).
  - `docs/audits/slice-2a-abacatepay-sandbox-report-2026-06-26.md` (relatorio completo criado seguindo o template do Slice 1-IT).
- Validacoes executadas: **TODAS PASSARAM**:
  - `dotnet restore PaymentHub.slnx` → OK
  - `dotnet build PaymentHub.slnx` → **0 errors / 0 warnings** em 9 projetos
  - `dotnet test PaymentHub.slnx` → **348 passed** (291 baseline + 57 novos AbacatePay, 0 regressao)
  - `dotnet test --filter "FullyQualifiedName~AbacatePay"` → **57 passed** (40 client + 17 adapter)
  - `dotnet test --filter "FullyQualifiedName~Provider"` → **72 passed** (Fake/Stripe/MercadoPago preservados)
  - `dotnet test tests/PaymentHub.IntegrationTests/` → **10 passed** (Slice 1-IT baseline preservado)
  - `scripts/agent-architecture-check.sh` → **Architecture check passed**
  - `scripts/agent-docs-check.sh` → **Docs check passed**
  - `git diff --check` → clean
- Riscos residuais (nao resolvidos neste slice, deferred):
  - **Slice 2-B — AbacatePay webhooks externos e normalizacao de eventos**: HMAC + timestamp + event normalization. Depende de decisao de produto (HMAC obrigatorio + retention de segredo).
  - Expor `brCode`/`brCodeBase64` na response publica de checkout (micro-slice de API).
  - `AuditLog` em `AbacatePayProviderAdapter` (P2-3 do `docs/roadmap/002-phase-status-board.md`).
  - Stripe/MercadoPago adapters reais (Phase 4).
  - Idempotency-Key no client HTTP AbacatePay contra retries no `CreateCheckoutHandler`.
- Aprendizados consolidados: entrada nova em `docs/harness/learnings.md` (9 decisoes reaproveitaveis para futuros providers e adapters outbound).
- Proximo slice recomendado: **Slice 2-B — AbacatePay webhooks externos e normalizacao de eventos** (Phase 2 + 3, dependencia zero alem do Slice 2-A). Pode correr em paralelo com Phase 3 ou Phase 6 conforme decisao de produto.

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
