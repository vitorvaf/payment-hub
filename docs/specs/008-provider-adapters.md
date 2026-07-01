# Adapters de Provider

## Objetivo

Definir o contrato dos adapters de pagamento e a expectativa para Fake e provedores reais.

## Escopo

- Interface de adapter.
- Providers Fake, AbacatePay, Stripe e MercadoPago.
- Mapeamento de status bruto para status canonico.
- Regras de logs e dados sensiveis.

## Fora de escopo

- Implementacao real de AbacatePay, Stripe ou MercadoPago sem tarefa propria.

## Regras obrigatorias

- Adapter deve traduzir vocabulario externo para contratos internos.
- Adapter nao deve vazar payload sensivel, API keys ou secrets em logs.
- Fake pode ser funcional no MVP.
- AbacatePay, Stripe e MercadoPago podem existir como skeleton.
- Integracao real deve vir em slice especifico com testes e docs do provider.

## Contratos

```csharp
public interface IPaymentProviderAdapter
{
    string ProviderCode { get; }

    Task<CreateCheckoutProviderResult> CreateCheckoutAsync(
        CreateCheckoutProviderRequest request,
        CancellationToken cancellationToken);

    Task<ProviderWebhookParseResult> ParseWebhookAsync(
        ProviderWebhookRequest request,
        CancellationToken cancellationToken);
}
```

| Provider | MVP |
|----------|-----|
| Fake | Funcional para dev/testes |
| AbacatePay | **Funcional em sandbox/devMode (Slice 2-A, 2026-06-27)**; cartao e boleto seguem skeleton |
| Stripe | Skeleton ate tarefa de integracao real |
| MercadoPago | Skeleton ate tarefa de integracao real |

### AbacatePay (Slice 2-A — 2026-06-27)

- Modo: Checkout Transparente PIX em sandbox/devMode (sem checkout hospedado nem cartao).
- Endpoints: `POST /transparents/create`, `GET /transparents/check`, `POST /transparents/simulate-payment`.
- Auth: `Authorization: Bearer <api-key>` onde a chave vem de `ProviderAccount.EncryptedCredentials` (AES via `ICredentialProtector`) e nunca logada.
- Configuracao: secao `Providers:AbacatePay` em `appsettings*.json` com `BaseUrl`, `TimeoutSeconds` e `AllowDevModeSimulation`. Default `false` em producao e so `true` em `appsettings.Development.json`.
- Erros classificados em `AbacatePayErrorCategory` (`BadRequest`, `Unauthorized`, `NotFound`, `RateLimited`, `ServerError`, `Network`, `Timeout`, `EnvelopeFailure`, `Unexpected`, `SimulationDisabled`) com flag `IsTransient`. Mensagens do `AbacatePayClientException` NAO incluem API key, body, `brCodeBase64` ou response body.
- Status mapping canonico via `PaymentStatusMapper.MapAbacatePay`. Status adicionais alem dos basicos: `redeemed -> Approved`, `under_dispute -> Pending` (decisao documentada em teste).
- `IPaymentProviderAdapter` NAO foi alterada neste slice: `CheckTransparentPixAsync` e `SimulateTransparentPixPaymentAsync` ficam apenas em `IAbacatePayClient`. A interface so sera estendida quando houver pelo menos um segundo caller concreto.
- Webhooks externos completos (HMAC, normalizacao de eventos) ficam em **Slice 2-B (a abrir)**.

### AbacatePay — Webhooks externos (Slice 2-B — 2026-06-29)

Slice 2-B conecta o adapter AbacatePay ao caminho oficial de webhooks externos: o controller faz fail-fast quando a assinatura esta ausente, o `ProcessWebhookEventHandler` resolve o `ProviderAccount` por metadata do payload e desprotege o `webhookSecret`, e o adapter valida HMAC + normaliza o payload antes de qualquer efeito de dominio.

- **Pacote `AbacatePay.Webhooks/`** em `src/PaymentHub.Infrastructure.Providers/AbacatePay/Webhooks/`:
  - `IAbacatePayWebhookSignatureVerifier` + `HmacAbacatePayWebhookSignatureVerifier`: HMAC-SHA256 do body UTF-8 com saida Base64, comparacao em tempo constante via `CryptographicOperations.FixedTimeEquals`. Categorias de falha retornadas: `None`, `MissingSignature`, `MalformedSignature`, `MissingSecret`, `SignatureMismatch`. Header canonico: `X-Webhook-Signature`. Mensagens NAO incluem segredo, assinatura raw ou body.
  - `IAbacatePayWebhookNormalizer` + `AbacatePayWebhookNormalizer`: desserializacao tolerante de `AbacatePayWebhookEnvelope` (campos `id`, `event`, `apiVersion`, `devMode`, `data`). Suporta eventos `transparent.completed | transparent.refunded | transparent.disputed | transparent.lost`. Mapeamento canonico para `PaymentStatus` via `MapEvent`: `transparent.completed+PAID/APPROVED -> Approved`, `transparent.refunded -> Refunded`, `transparent.disputed -> Pending`, `transparent.lost -> Failed`. Eventos nao suportados retornam `IsValid=false` com mensagem segura.
  - Modelos DTO em `AbacatePay/Models/`: `AbacatePayWebhookEnvelope`, `AbacatePayTransparentWebhookData`.
- **`ProviderWebhookRequest` estendido** em `src/PaymentHub.Application/Abstractions/Providers/ProviderModels.cs`: passa a carregar `ProviderAccountId` e `WebhookSecret` (init-only, backward-compatible). `Fake`/`Stripe`/`MercadoPago` continuam compilando e funcionam com `WebhookSecret=null`.
- **`AbacatePayProviderAdapter.ParseWebhookAsync`** foi reescrito para: validar presenca de `WebhookSecret` e `Signature`, chamar o verifier, em caso de sucesso chamar o normalizer. Erros viram `ProviderWebhookParseResult { IsValid=false, ErrorMessage="AbacatePay webhook ..." }` sem vazar `webhookSecret`/signature/body/apiKey. Sucesso retorna todos os campos canonicos (`ProviderEventId`, `EventType`, `ProviderPaymentId`, `ProviderStatus`, `RawPayloadJson`).
- **DI**: `IAbacatePayWebhookSignatureVerifier` e `IAbacatePayWebhookNormalizer` registrados como Singleton em `ProvidersServiceCollectionExtensions`.
- **Cobertura de testes**: 18 testes em `AbacatePayProviderAdapterWebhookTests`, 14 em `AbacatePayWebhookNormalizerTests`, 10 em `AbacatePayWebhookSignatureVerifierTests`. Total acumulado slice 2-B: 42 testes novos.

### AbacatePay — Gerenciamento de webhook via API (Slice 2-C — 2026-06-30)

Slice 2-C introduz dois endpoints server-to-server para configurar (PUT) e consultar (GET) a inscricao de webhook AbacatePay em `ProviderAccount`. O segredo continua a viver **dentro** de `EncryptedCredentials` (nunca em coluna propria), e a chamada de registro remoto fica gated por feature flag ate o Slice 2-C.1 (sub-seguinte) implementar o client HTTP real.

- **Endpoints** (vide `009-api-contracts.md` para payloads completos):
  - `PUT /api/v1/provider-accounts/{providerAccountId}/webhook`
  - `GET /api/v1/provider-accounts/{providerAccountId}/webhook`
- **Quatro colunas non-sensitive** adicionadas em `provider_accounts` via migration `20260630001726_AddProviderAccountWebhookColumns`:
  - `webhook_callback_url` (varchar 2000, nullable) — alvo das entregas.
  - `webhook_events` (**text**, nullable) — array JSON serializado (`["transparent.completed", ...]`). Anti-regression: `text` deliberadamente (NAO `jsonb`), porque `jsonb` reformata o JSON no insert (espaco apos `:` e `,`); isso quebraria qualquer round-trip byte-exact ou HMAC no body armazenado. Mesma regra que `webhook_events.raw_payload` (Slice 3-IT).
  - `webhook_configured_at` (timestamp, nullable) — timestamp da ultima gravacao (incluindo clears).
  - `webhook_remote_status` (varchar 32, nullable) — `NotRegistered` | `Registered` | `RegistrationFailed` | `RemoteRegistrationDeferred`.
- **DTOs** em `PaymentHub.Application/Tenants/Dtos.cs`:
  - `ConfigureAbacatePayWebhookRequestDto(CallbackUrl, Events, WebhookSecret, RegisterRemotely)` — body do PUT, **sem `tenantId`/`applicationId`** (re-asserted invariant Slice 6-B).
  - `ProviderAccountWebhookResponseDto(ProviderAccountId, ProviderCode, Environment, CallbackUrl, Events, HasWebhookSecret, RemoteRegistrationStatus, ConfiguredAt, UpdatedAt)` — body de PUT/GET, **sem `apiKey`, `webhookSecret`, `protectedWebhookSecret`, `encryptedCredentials`**.
- **Validator** `ConfigureAbacatePayWebhookRequestValidator` injeta `IRuntimeEnvironment` e reusa `WebhookUrlValidator` (HTTPS-only, loopback HTTP em Development). Whitelist de eventos e literal na aplicacao para que Application nunca dependa de Infrastructure.
- **Domain** `ProviderAccount.ConfigureWebhook(callbackUrl, eventsJson, remoteStatus)` valida `eventsJson` e apenas aceita array JSON. `Activate()` adicionado para paridade com `Tenant` (ainda sem caller externo nesta slice).
- **Repository** `IProviderAccountRepository.GetByIdForTenantAndApplicationAsync(tenantId, applicationId, providerAccountId, ct)` **sem** filtro `Active`, para permitir distinguir `404 Not Found` (linha inexistente no escopo) de `409 Conflict` (linha presente mas inativa).
- **Handlers** (`PaymentHub.Application/Tenants/`):
  - `IConfigureProviderAccountWebhookHandler` retorna `ConfigureWebhookOutcome { Success | NotFound | Inactive | UnsupportedProvider }`. Logica: preservar `apiKey` ao round-trip `EncryptedCredentials`; sobrescrever `webhookSecret` apenas quando o caller fornece um novo; chamar `IAbacatePayWebhookManagementClient.RegisterWebhookAsync(...)` apenas quando o caller seta `registerRemotely=true`, forneceu `webhookSecret`, **e** a feature policy permite.
  - `IGetProviderAccountWebhookHandler` retorna `GetWebhookOutcome` (mesmos 4 casos). `HasWebhookSecret` e derivado via `ProviderAccountCredentialsInspector.HasWebhookSecret(...)` que **unprotecta** o blob e verifica os campos `webhookSecret` (preferido) / `secret` (legacy) sem nunca vazar o valor.
- **Abstraction** `IProviderWebhookManagementClient` + `IProviderWebhookRegistrationFeaturePolicy` vivem em `Application/Abstractions/Providers/` (Clean Architecture). Implementacoes `NoOpProviderWebhookManagementClient` e `AbacatePayWebhookRegistrationFeaturePolicy` ficam em `Infrastructure.Providers.AbacatePay/`. Default da feature flag: `false` (opt-in explicito).
- **Feature flag** `Providers:AbacatePay:AllowWebhookRegistration` (bool, default `false`). Quando `false`, mesmo com `registerRemotely=true`, o handler NAO chama o client e grava `RemoteRegistrationDeferred`.
- **Controller** `ProviderAccountsController` ganha 2 novos endpoints sob `[Route("api/v1/provider-accounts")]`. Reusa o padrao `ITenantContext` da Slice 6-B (try/catch em `InvalidOperationException` retornando 401). Status mapping: 200 / 401 / 404 / 409. Body em 401/404/409 sempre com `{ "error": "...", "message": "..." }` generico, sem `tenantId`/`applicationId`/`providerAccountId` que pudessem vazar existencia.
- **Cobertura de testes**: 14 em `ConfigureProviderAccountWebhookHandlerTests`, 9 em `GetProviderAccountWebhookHandlerTests`, 11 em `ConfigureAbacatePayWebhookRequestValidatorTests`, 12 em `ProviderAccountsWebhookControllerTests`, 3 em `ProviderAccountWebhookPersistenceTests`. Total adicionado pelo Slice 2-C: 49 testes.

### AbacatePay — Cliente HTTP real de gerenciamento de webhook (Slice 2-C.1 — 2026-06-30)

Slice 2-C.1 substitui o `NoOpProviderWebhookManagementClient` por um client HTTP real que chama `POST /webhooks/create` no upstream AbacatePay. A interface `IProviderWebhookManagementClient` permanece inalterada — a slice 2-C ja define o contrato e os 3 gates do handler (`RegisterRemotely=true && WebhookSecret != null && policy.IsRemoteRegistrationEnabled(ProviderCode.AbacatePay)`), entao o handler nao precisa de alteracoes.

- **Client real** `AbacatePayWebhookManagementClient` (em `src/PaymentHub.Infrastructure.Providers/AbacatePay/AbacatePayWebhookManagementClient.cs`):
  - **Named HttpClient dedicado**: `abacatepay-webhooks` (distinto do `abacatepay` que serve os endpoints de transparent-PIX). Permite tunar timeout / retry / rate-limit do ciclo de webhook management independentemente do ciclo de create-transport. Configurado em `ProvidersServiceCollectionExtensions.AddPaymentHubProviders(...)` com BaseAddress de `AbacatePayOptions.BaseUrl` + Timeout de `AbacatePayOptions.TimeoutSeconds`.
  - **Pipeline de 4 gates**:
    1. Provider check: so `ProviderCode.AbacatePay` prossegue. Outros providers retornam `RegistrationFailed` sem chamar HTTP.
    2. Feature flag: re-checagem defensiva de `Providers:AbacatePay:AllowWebhookRegistration`. O handler ja short-circuita, mas o client re-checa para nao ser burlado por callers futuros.
    3. Pre-flight: `callbackUrl` nao-vazio, `events` nao-vazio, `webhookSecret` nao-vazio. Falha → `RegistrationFailed`.
    4. ApiKey extraction: `IProviderAccountCredentialsReader.ReadApiKey(protectedCredentials)` recupera o apiKey. Falha → `RegistrationFailed`.
  - **Request**:
    - Method: `POST`. Path: `webhooks/create` (relativo, preserva `/v2/` do `BaseAddress`).
    - Header: `Authorization: Bearer {apiKey}`.
    - Body JSON: `{ "name": "Payment Hub - AbacatePay", "endpoint": "{callbackUrl}", "secret": "{webhookSecret}", "events": [...] }`.
  - **Categorizacao de erros** reusando `AbacatePayClientException` / `AbacatePayErrorCategory`:
    - 400 → `BadRequest`. 401/403 → `Unauthorized`. 404 → `NotFound`. 429 → `RateLimited`. 5xx → `ServerError`.
    - `HttpRequestException` → `Network` (transient). `TaskCanceledException` com cancellation do caller → propaga `OperationCanceledException`; sem cancelamento do caller → `Timeout` (transient).
    - Envelope `success=false` ou `data` null com HTTP 2xx → `RegistrationFailed` (categoria `EnvelopeFailure` por tras).
    - Todas as excecoes sao capturadas e traduzidas para `RegistrationFailed` — o client NAO propaga exceptions para o handler exceto `OperationCanceledException`.
  - **No-leak guarantees** (re-asserting audit Slice 2-C anti-patterns):
    - **NUNCA** loga `apiKey`, `webhookSecret`, `Authorization` header, body request, body response.
    - **NUNCA** persiste o secret em `last_error` ou `ProviderAccount.WebhookRemoteStatus` (que continua 4 valores enum).
    - Loga apenas: `providerCode`, `endpoint.Length`, `eventCount`, `category` enum, `statusCode`. A mensagem de `AbacatePayClientException` carrega apenas o enum name + status code generico (`"AbacatePay HTTP {statusCode}."`).
  - **Nova categoria** `AbacatePayErrorCategory.RegistrationDisabled = 11` adicionada ao enum (Slice 2-C.1). Documentada inline; nunca e lancada pelo client real (que prefere retornar `RegistrationFailed`), mas fica disponivel para clientes futuros.
- **Nova abstraction publica** `IProviderAccountCredentialsReader` em `PaymentHub.Application/Abstractions/Security/`:
  - Promove o helper `ProviderAccountCredentialsInspector.UnprotectAndReadApiKey` (que era `internal static` na Slice 2-C) para uma interface publica cross-layer.
  - Implementacao `ProviderAccountCredentialsReader` em `Infrastructure.Postgres/Security/` delega para o inspector preservando a invariante "no exception on bad input".
  - Adicionado `<InternalsVisibleTo Include="PaymentHub.Infrastructure.Postgres" />` no `PaymentHub.Application.csproj` para que o inspector continue visivel.
- **DI** (`ProvidersServiceCollectionExtensions`):
  - `services.AddSingleton<IProviderWebhookManagementClient, AbacatePayWebhookManagementClient>();` substitui o registro do `NoOpProviderWebhookManagementClient`.
  - `services.AddHttpClient(AbacatePayWebhookManagementClient.HttpClientName, ...)` configura o named client com BaseAddress + Timeout do `AbacatePayOptions`.
  - `NoOpProviderWebhookManagementClient.cs` foi **removido** (registro unico substitui; Slice 2-C ja tinha o client real planejado).
- **Persistência do status remoto** continua 100% do Slice 2-C:
  - `webhook_remote_status` recebe `Registered` ou `RegistrationFailed` quando o client real e chamado.
  - `webhook_remote_status` recebe `RemoteRegistrationDeferred` quando o handler short-circuita por feature flag off ou `webhookSecret` ausente.
  - `webhook_remote_status` recebe `NotRegistered` quando o caller NAO pediu `registerRemotely`.
  - Migracao 2-C preservada: `webhook_events` continua `text` (NAO `jsonb`); `webhookSecret` continua dentro de `EncryptedCredentials` (nenhuma coluna propria).
- **Cobertura de testes** (Slice 2-C.1):
  - 20 testes em `AbacatePayWebhookManagementClientTests` (17 scenarios do briefing + 3 theory cases de status codes).
  - 2 testes em `ProviderAccountsWebhookControllerTests` (`ShouldReturnOkWithRemoteRegistrationDeferred_WhenFeatureFlagIsOffAndRegisterRemotelyTrue` + `ShouldReturnOkWithRegisteredStatus_WhenAllGatesPass`).
  - 1 teste E2E em `AbacatePayWebhookManagementE2ETests` que exercita o caminho real via `PaymentHubApiFactory` + Postgres Testcontainers + `AbacatePayFakeHttpHandler` estendido para rotear `/webhooks/create` e `/webhooks/list`.
  - O helper `ScriptedHandler` em `tests/PaymentHub.UnitTests/Support/` e reusado para os 17 unit tests do client.
  - Total adicionado pelo Slice 2-C.1: **23 testes** (20 unit + 2 controller + 1 E2E).
- **Mudancas no `AbacatePayFakeHttpHandler` (integration)**: o fake handler agora detecta `request.RequestUri.AbsolutePath`:
  - Termina com `/webhooks/create` → responde envelope `{ "data": { "id": "whk_..." }, "success": true, "error": null }`.
  - Termina com `/webhooks/list` → responde `{ "webhooks": [ ... ] }`.
  - Outros paths (transparents/create) → resposta pre-existente de PIX transparente. Backward-compatible com Slice 2-A/3-IT.
- **Anti-Regression Notes** (re-asserting Slice 2-C):
  - `webhook_events` permanece `text` (NAO `jsonb`).
  - `webhookSecret` NAO ganha coluna propria.
  - DTOs NAO aceitam `tenantId`/`applicationId` (re-asserted Slice 6-B).
  - 3-gate rule preservada no handler.
  - `OutboxDispatcherWorker` e HMAC interno NAO foram alterados.

## Criterios de aceite

- Provider code e estavel.
- Falha de provider retorna erro controlado.
- Parser de webhook retorna provider payment id, event type e status bruto mapeavel.
- AbacatePay: API key so chega ao client apos `ICredentialProtector.Unprotect`; nenhuma chave e logada; `RawResponseJson` nao expoe credencial; erros categorizados com `IsTransient`.
- Adapter e tests cobrem Bearer header, unmarshal de envelope, mapeamento de status e ausencia de leak de segredos.
- AbacatePay webhooks externos: HMAC-SHA256 Base64 sobre body UTF-8 validado em tempo constante; assinatura/categoria de erro nunca aparece em `ErrorMessage`; payload `transparent.*` normalizado para `PaymentStatus`; metadata do payload (`data.metadata.{tenantId,applicationId,paymentId}`) orienta o roteamento sem varrer tenants.

## Testes esperados

- Fake cria checkout e parseia webhook.
- Mappers de status por provider (incluindo AbacatePay: `PENDING`, `PAID`, `APPROVED`, `EXPIRED`, `CANCELLED`, `CANCELED`, `FAILED`, `REFUNDED`, `REDEEMED`, `UNDER_DISPUTE`).
- Provider desconhecido.
- Falha de provider nao persiste dado sensivel em logs.
- AbacatePayClient: 400/401/403/404/429/5xx, timeout/network, `success=false`, parseamento de `brCode`/`brCodeBase64`, simulacao gated por `AllowDevModeSimulation`.
- AbacatePayProviderAdapter: unprotect + extract apiKey, payload com amount/customer/metadata, ProviderPaymentId retornado, status mapeado, sintese de CheckoutUrl `abacatepay://pix/<id>`.
- AbacatePay webhooks (Slice 2-B):
  - Verifier: HMAC valido/invalido, base64 malformado, body adulterado, secret ausente, header ausente, multibyte, body null.
  - Normalizer: payload vazio/malformed/null, evento unsupported, evento valido com 6 status canonicos diferentes, metadata livre, ignorando campos extras.
  - Adapter: webhookSecret ausente, signature ausente, signature invalida, base64 malformado, body JSON malformado, evento nao suportado, payment id ausente, status ausente, id do envelope ausente. Nenhum caso vaza `webhookSecret`/signature/body bruto.
  - Controller: AbacatePay sem `X-Webhook-Signature` retorna 401 sem persistir; com `X-Webhook-Signature` segue para o handler; com `X-Provider-Signature` apenas segue; com ambos, `X-Webhook-Signature` ganha. Outros providers preservam comportamento legacy.

## Arquivos relacionados

- `src/PaymentHub.Application/Abstractions/Providers/IPaymentProviderAdapter.cs`
- `src/PaymentHub.Application/Abstractions/Providers/ProviderModels.cs`
- `src/PaymentHub.Infrastructure.Providers/AbacatePay/AbacatePayProviderAdapter.cs`
- `src/PaymentHub.Infrastructure.Providers/AbacatePay/AbacatePayClient.cs`
- `src/PaymentHub.Infrastructure.Providers/AbacatePay/AbacatePayOptions.cs`
- `src/PaymentHub.Infrastructure.Providers/AbacatePay/AbacatePayErrorCategory.cs`
- `src/PaymentHub.Infrastructure.Providers/AbacatePay/AbacatePayClientException.cs`
- `src/PaymentHub.Infrastructure.Providers/AbacatePay/IAbacatePayClient.cs`
- `src/PaymentHub.Infrastructure.Providers/AbacatePay/Webhooks/IAbacatePayWebhookSignatureVerifier.cs`
- `src/PaymentHub.Infrastructure.Providers/AbacatePay/Webhooks/HmacAbacatePayWebhookSignatureVerifier.cs`
- `src/PaymentHub.Infrastructure.Providers/AbacatePay/Webhooks/IAbacatePayWebhookNormalizer.cs`
- `src/PaymentHub.Infrastructure.Providers/AbacatePay/Webhooks/AbacatePayWebhookNormalizer.cs`
- `src/PaymentHub.Infrastructure.Providers/AbacatePay/Webhooks/AbacatePayWebhookNormalizationResult.cs`
- `src/PaymentHub.Infrastructure.Providers/AbacatePay/Models/AbacatePayWebhookEnvelope.cs`
- `src/PaymentHub.Infrastructure.Providers/AbacatePay/Models/AbacatePayTransparentWebhookData.cs`
- `src/PaymentHub.Infrastructure.Providers/ProvidersServiceCollectionExtensions.cs`
- `src/PaymentHub.Application/Webhooks/WebhookHandlers.cs`
- `src/PaymentHub.Api/Controllers/ProviderWebhooksController.cs`
- `tests/PaymentHub.UnitTests/Infrastructure/Providers/AbacatePay/AbacatePayClientTests.cs`
- `tests/PaymentHub.UnitTests/Infrastructure/Providers/AbacatePay/AbacatePayProviderAdapterTests.cs`
- `tests/PaymentHub.UnitTests/Infrastructure/Providers/AbacatePay/AbacatePayProviderAdapterWebhookTests.cs`
- `tests/PaymentHub.UnitTests/Infrastructure/Providers/AbacatePay/Webhooks/AbacatePayWebhookSignatureVerifierTests.cs`
- `tests/PaymentHub.UnitTests/Infrastructure/Providers/AbacatePay/Webhooks/AbacatePayWebhookNormalizerTests.cs`
- `tests/PaymentHub.UnitTests/Application/ProcessWebhookEventHandlerTests.cs`
- `tests/PaymentHub.UnitTests/Application/ProcessWebhookEventHandlerAbacatePayTests.cs`
- `tests/PaymentHub.UnitTests/Api/ProviderWebhooksControllerTests.cs`
- `tests/PaymentHub.UnitTests/Domain/PaymentStatusMapperTests.cs`
- `tests/PaymentHub.UnitTests/Support/FakeCredentialProtector.cs`
- `docs/audits/slice-2b-abacatepay-webhooks-report-2026-06-29.md`
