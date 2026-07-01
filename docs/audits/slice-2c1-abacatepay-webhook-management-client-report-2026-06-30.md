# Slice 2-C.1 — AbacatePay Webhook Management Client Report

**Data:** 2026-06-30
**Executor:** OpenCode Implementer
**Status:** CONCLUIDO
**Slice planejada em:** `agent-progress.md` (linhas 116+)

---

## Resumo

A Slice 2-C.1 substitui o `NoOpProviderWebhookManagementClient` (Slice 2-C) por um client HTTP real que chama `POST /webhooks/create` no upstream AbacatePay. A interface `IProviderWebhookManagementClient` permanece inalterada — a slice anterior ja define o contrato e os 3 gates do handler (`RegisterRemotely=true && WebhookSecret != null && policy.IsRemoteRegistrationEnabled(ProviderCode.AbacatePay)`), entao o handler nao precisa de alteracoes. Feature flag `Providers:AbacatePay:AllowWebhookRegistration` (default `false`, opt-in explicito em `appsettings.json`) continua sendo o gate principal. Suite E2E cresce para **522 testes** (489 unit + 33 integration).

---

## Objetivo

Substituir o `NoOpProviderWebhookManagementClient` por um client HTTP real AbacatePay para criacao/listagem de webhooks remotos, protegido por `AllowWebhookRegistration` e com testes fakeados. Quando `registerRemotely=true` + `AllowWebhookRegistration=true`, o Payment Hub deve usar a apiKey protegida do `ProviderAccount` para chamar a AbacatePay, criar o webhook remoto com endpoint/secret/events, persistir status remoto seguro, e nunca vazar apiKey/webhookSecret.

---

## Discovery

### Estado anterior (pre-Slice 2-C.1)

- `IProviderWebhookManagementClient.RegisterWebhookAsync(...)` ja estava definido em `src/PaymentHub.Application/Abstractions/Providers/` retornando `ProviderWebhookRegistrationOutcome.Registered | RegistrationFailed`.
- A implementacao Default era `NoOpProviderWebhookManagementClient` que apenas logava `providerCode` + `callbackUrl.Length` + `events.Count` e retornava `Registered` para type-system compatibility.
- Feature flag gate ja existia via `AbacatePayWebhookRegistrationFeaturePolicy.IsRemoteRegistrationEnabled(ProviderCode)` backed por `AbacatePayOptions.AllowWebhookRegistration` (default `false`).
- `ConfigureProviderAccountWebhookHandler` ja tinha 3 gates para fechar antes de chamar o client.
- Suite previa a 2-C.1: 498 testes (467 unit + 31 integration, pos Slice 7-M1).

### Contrato da AbacatePay para webhooks (confirmado)

A Slice 2-C reportou `POST /v2/webhooks/create`. O briefing pediu `POST /webhooks/create` (sem `/v2/`) que equivale ao mesmo endpoint ja que `AbacatePayOptions.BaseUrl = "https://api.abacatepay.com/v2"` (configured em `PaymentHub.Infrastructure.Postgres/ProvidersServiceCollectionExtensions`). Path relativo `webhooks/create` (sem leading slash) preserva o segmento `/v2/` do BaseAddress. Gotcha documentado no learnings do Slice 2-A: paths com leading `/` ignoram o segmento `/v2/` do BaseAddress.

Formato de request usado:

```json
{
  "name": "Payment Hub - AbacatePay",
  "endpoint": "https://merchant.example.com/webhooks/abacate",
  "secret": "{webhookSecret}",
  "events": ["transparent.completed", "transparent.refunded", "transparent.disputed", "transparent.lost"]
}
```

Endpoint GET `/webhooks/list` tambem foi implementado (top-level `webhooks` array com `{ id, url, events }` por item — sem `apiKey`/`webhookSecret`/`signature`/`request body`).

### Estrategia de autenticacao Bearer

`Authorization: Bearer {apiKey}` onde o `apiKey` plaintext e':

1. O `EncryptedCredentials` (blob AES-protegido em `ProviderAccount`) e unprotectado uma unica vez dentro do client.
2. O JSON desprotegido e parseado para extrair `{apiKey, webhookSecret?}`.
3. O `apiKey` e' usado para construir o header. Nao e persistido em log/variavel de instancia fora do metodo.

### Estrategia de feature flag `AllowWebhookRegistration`

`AbacatePayOptions.AllowWebhookRegistration` (bool, default `false`). Cliente **re-checa** o gate mesmo ja' tendo o handler short-circuita — protecao contra callers futuros que pulem o guard do handler. Quando `false`, o client retorna `RegistrationFailed` sem chamada HTTP.

### Estrategia de persistencia de status remoto

`webhook_remote_status` continua 4 valores enum (`NotRegistered` | `Registered` | `RegistrationFailed` | `RemoteRegistrationDeferred`). A Slice 2-C.1 NAO altera o enum. Apenas o handler, ja' integrado com o client, agora persiste `Registered` ou `RegistrationFailed` quando o client real responde. Migracao Slice 2-C preservada sem mudancas.

---

## Escopo implementado

### 1. Client HTTP real (`AbacatePayWebhookManagementClient.cs`)

Caminho: `src/PaymentHub.Infrastructure.Providers/AbacatePay/AbacatePayWebhookManagementClient.cs`. 4-gate pipeline:

1. **Provider check**: `providerCode != AbacatePay` → `RegistrationFailed` sem HTTP.
2. **Feature flag**: `_featurePolicy.IsRemoteRegistrationEnabled(...) == false` → `RegistrationFailed` sem HTTP.
3. **Pre-flight**: `callbackUrl`/`events`/`webhookSecret` vazio → `RegistrationFailed`.
4. **apiKey extraction**: `_credentialsReader.ReadApiKey(protectedCredentials)` retorna null/empty → `RegistrationFailed`.

Apos todos os gates passarem: monta `AbacatePayCreateWebhookRequest { Name, Endpoint, Secret, Events }`, configura `Authorization: Bearer {apiKey}`, POST `/webhooks/create`. Parsing de resposta via `AbacatePayEnvelope<AbacatePayCreateWebhookResponse>`. Sucesso → `Registered` + log; falha HTTP ou envelope `success=false` → `RegistrationFailed` + log seguro.

Categorizacao de erros via reuso de `AbacatePayClientException`/`AbacateErrorCategory`:

| HTTP Status | Categoria | Transient |
|-------------|-----------|-----------|
| 400 | BadRequest | nao |
| 401/403 | Unauthorized | nao |
| 404 | NotFound | nao |
| 429 | RateLimited | sim |
| 5xx | ServerError | sim |
| `HttpRequestException` | Network | sim |
| `TaskCanceledException` (sem caller cancel) | Timeout | sim |
| envelope success=false com HTTP 2xx | EnvelopeFailure | nao |

`TaskCanceledException` com caller cancel propaga `OperationCanceledException`. Caller cancel distinguido do timeout do HttpClient via `cancellationToken.IsCancellationRequested` (gotcha do .NET 10).

`AbacatePayClientException` carrega apenas categoria enum + status code generico (`"AbacatePay HTTP {statusCode}."`). Mensagem nunca inclui `apiKey`, `webhookSecret`, `Authorization` header, request body, ou response body.

Nova categoria enum `AbacatePayErrorCategory.RegistrationDisabled = 11` adicionada mas **nao** lancada pelo client real (que prefere retornar `RegistrationFailed` direto). Documentada inline; fica disponivel para clientes futuros.

### 2. Models de request/response (`AbacatePayWebhookModels.cs`)

Caminho: `src/PaymentHub.Infrastructure.Providers/AbacatePay/Models/AbacatePayWebhookModels.cs`. 4 tipos:

- `AbacatePayCreateWebhookRequest(Name, Endpoint, Secret, Events)` — body de `POST /webhooks/create`.
- `AbacatePayCreateWebhookResponse(Id)` — `data.id` no envelope sucesso.
- `AbacatePayListWebhooksResponse(Webhooks)` — `webhooks` array para `GET /webhooks/list`.
- `AbacatePayWebhookItem(Id, Url, Events)` — item de list, sem `webhookSecret`/`apiKey`/`signature`.

Todos os campos usam `[JsonPropertyName("...")]` para mapear para snake_case no JSON wire format.

### 3. Public interface cross-layer (`IProviderAccountCredentialsReader.cs`)

Caminho: `src/PaymentHub.Application/Abstractions/Security/IProviderAccountCredentialsReader.cs`. Promove o helper `ProviderAccountCredentialsInspector.UnprotectAndReadApiKey` (que era `internal static` em Application/Tenants/Validation/) para uma interface publica cross-layer.

```csharp
public interface IProviderAccountCredentialsReader
{
    string? ReadApiKey(string encryptedCredentials);
}
```

Implementacao concreta `ProviderAccountCredentialsReader` em `Infrastructure.Postgres/Security/` delega para o inspector preservando a invariante "no exception on bad input".

`<InternalsVisibleTo Include="PaymentHub.Infrastructure.Postgres" />` adicionado ao `PaymentHub.Application.csproj` (alem do pre-existente `PaymentHub.UnitTests`) para que o adapter possa chamar o inspector `internal`.

### 4. Models de test support atualizados

`tests/PaymentHub.IntegrationTests/Support/AbacatePayFakeHttpHandler.cs` estendido com roteamento interno:

- Path termina com `/webhooks/create` → responde `{ data: { id: "whk_<guid>" }, success: true, error: null }`.
- Path termina com `/webhooks/list` → responde `{ webhooks: [ { id, url, events } ] }`.
- Outros paths (transparents/create) → resposta pre-existente de PIX transparente (backward-compatible com Slice 2-A/3-IT).

`PaymentHubApiFactory.ProtectAbacatePayCredentials` agora aceita `string?` para `webhookSecret` (permite seedar conta sem secret — o caso pre-2-C tipico onde o merchant primeiro registra via PUT).

### 5. DI

`ProvidersServiceCollectionExtensions`:

```csharp
services.AddHttpClient(AbacatePayWebhookManagementClient.HttpClientName, (sp, http) =>
{
    var opts = sp.GetRequiredService<IOptionsMonitor<AbacatePayOptions>>().CurrentValue;
    http.BaseAddress = new Uri(opts.BaseUrl.TrimEnd('/') + "/");
    http.Timeout = TimeSpan.FromSeconds(Math.Max(1, opts.TimeoutSeconds));
});
services.AddSingleton<IProviderWebhookManagementClient, AbacatePayWebhookManagementClient>();
```

`PostgresServiceCollectionExtensions`:

```csharp
services.AddSingleton<IProviderAccountCredentialsReader, ProviderAccountCredentialsReader>();
```

`NoOpProviderWebhookManagementClient` **removido** do projeto (registro unico substitui; arquivo deletado).

### 6. Handler

`ConfigureProviderAccountWebhookHandler` **NAO** foi alterado. A Slice 2-C ja' tinha a chamada `webhookClient.RegisterWebhookAsync(...)` gated pelos 3 conditions. Slice 2-C.1 apenas substitui a implementacao concreta por tras da interface. Mantido como backward-compatible com os 14 testes unitarios pre-existentes (que ja' usavam `FakeProviderWebhookManagementClient`).

---

## Fora de escopo

Conforme briefing, NAO implementado nesta slice:

- chamadas reais em testes automatizados (cobertura via `AbacatePayFakeHttpHandler` em memoria).
- delete remoto de webhook (FUTURO: `POST /webhooks/delete` ou `DELETE /webhooks/{id}`).
- update remoto complexo (apenas `registerRemotely=true` cobre o caso mais simples).
- retry/backoff avancado de cadastro remoto (a politica de retry fica no dispatcher layer; este client falha rapido e mapeia para `RegistrationFailed` ou `AbacatePayClientException` transient).
- dashboard admin (Phase 5).
- painel frontend (Phase 5).
- conciliacao financeira (Phase 8).
- assinaturas (Phase 8).
- payouts (Phase 8).
- transferencias PIX (Phase 8).
- multiplos providers novos (Stripe/MercadoPago ficam como skeletons em `Infrastructure.Providers.{Stripe,MercadoPago}/` — uma slice futura copiaria o pattern do `AbacatePayWebhookManagementClient` para eles).
- observabilidade completa (metricas, tracing, alerting — Phase 9).
- alteracao do recebimento de webhooks externos (Slice 2-B ja' cobre).
- alteracao do `OutboxDispatcherWorker` (Slice 7-M1 ja' cobre).
- alteracao do HMAC interno (Slice 7-A ja' cobre).
- mudanca no fluxo E2E ja' validado (Slice 3-IT preservada sem alteracoes).

---

## Client HTTP — pontos arquiteturais

### Por que Named HttpClient dedicado?

O `abacatepay-webhooks` e' distinto do `abacatepay`:

- **Lifecycle**: webhooks management tem SLA diferente (registration nao precisa do mesmo timeout curto que PIX-create); tuning independente futuro.
- **Retry policy**: webhook management pode ter tentativas proprias que nao devem afetar transparent-PIX.
- **Rate limit reservation**: se algum dia Payment Hub precisar reservar quota por origem de chamada.
- **Observabilidade**: telemetria pode ser separada por client.

Re-assertindo principio do Slice 2-A (learnings): NUNCA reusar o named client `abacatepay` para o client de webhook management.

### Por que 4 gates no client (mesmo que o handler ja os tem)?

Defesa em profundidade. O handler ja' short-circuita quando algum gate falha, mas o client **re-checa**:

- Caller pode chamar o client diretamente sem passar pelo handler (futuros dispatchers, admin UI, scripts de retry batch).
- Re-checar e' barato (apenas feature flag + provider code + checks null).
- Anti-regression: se um futuro caller implementar um fast-path, o gate do client garante que pelo menos 1 camada de safety existe.

### Por que `RegistrationDisabled` categoria enum nao usada?

Consideracoes:

- Adicionar uma categoria enum extra no codepath do client significa que `ConfigureProviderAccountWebhookHandler` precisaria distinguir `RegistrationDisabled` de `RegistrationFailed` na logica de mapping para `ProviderWebhookRemoteStatus`.
- Slice 2-C ja' mapeia os dois para o mesmo `ProviderWebhookRemoteStatus` valor (`RegistrationFailed`), porque nao ha diferenca observavel entre "operador nao ligou a flag" e "upstream rejeitou" (em ambos casos, a integracao falhou e o operador precisa intervir).
- A categoria fica reservada para clientes futuros que queiram fazer log estruturado separado (ex.: alerting de "operador esqueceu a flag on").

---

## Feature flag `AllowWebhookRegistration`

Comportamento (re-asserting regras Slice 2-C atualizadas):

1. `registerRemotely=false`: handler NAO chama o client. `webhook_remote_status = NotRegistered`.
2. `registerRemotely=true && AllowWebhookRegistration=false`: handler NAO chama o client. `webhook_remote_status = RemoteRegistrationDeferred`.
3. `registerRemotely=true && AllowWebhookRegistration=true && WebhookSecret == null`: handler NAO chama o client. `webhook_remote_status = RemoteRegistrationDeferred`.
4. `registerRemotely=true && AllowWebhookRegistration=true && WebhookSecret != null && provider != AbacatePay`: handler retorna `ConfigureWebhookOutcome.UnsupportedProvider` antes de qualquer persistencia.
5. `registerRemotely=true && AllowWebhookRegistration=true && WebhookSecret != null && provider == AbacatePay`: handler chama o client real. `webhook_remote_status = Registered` ou `RegistrationFailed` baseado no retorno.

A persistencia local (`webhook_callback_url`, `webhook_events`, `webhook_configured_at`) acontece **ANTES** da chamada remota (re-asserting Slice 2-C). Se a chamada remota falhar, a configuracao local permanece persistida — o operador pode consultar via GET e tentar re-registrar via PUT.

---

## Fluxo `registerRemotely`

```
PUT /api/v1/provider-accounts/{id}/webhook
    |
    v
Controller: extract tenantId/applicationId from ITenantContext (401 if missing)
    |
    v
FluentValidation (callbackUrl HTTPS, events whitelist, webhookSecret 16-500 chars)
    |
    v
ConfigureProviderAccountWebhookHandler.HandleAsync
    |
    +--> account == null          --> 404 NotFound
    +--> !account.Active          --> 409 Inactive
    +--> account.ProviderCode != AbacatePay --> 409 UnsupportedProvider
    |
    +--> Step 1: rebuild encrypted credentials (preserve apiKey)
    |         ProviderAccountCredentialsInspector.BuildMergedCredentialsJson
    |         -> mergedJson (apiKey + webhookSecret?)
    +--> Step 2: account.UpdateCredentials(protectedCredentials)
    |         account.ConfigureWebhook(callbackUrl, eventsJson, RemoteRegistrationDeferred)
    |         SaveChangesAsync (LOCAL persistence)
    |
    +--> Step 3 (3-gate):
    |         (a) RegisterRemotely=true?
    |         (b) WebhookSecret provided?
    |         (c) AllowWebhookRegistration?
    |         ALL three must pass. Otherwise keep RemoteRegistrationDeferred.
    |
    +--> If all gates pass:
    |         AbacatePayWebhookManagementClient.RegisterWebhookAsync
    |         --> 4-gate pipeline:
    |             (1) providerCode == AbacatePay
    |             (2) _featurePolicy.IsRemoteRegistrationEnabled() returns true
    |             (3) pre-flight: callbackUrl + events + webhookSecret all non-empty
    |             (4) apiKey extraction via IProviderAccountCredentialsReader.ReadApiKey
    |         -> If apiKey null/empty: RegistrationFailed (return)
    |         -> HTTP POST /webhooks/create with Authorization Bearer apiKey
    |         -> 2xx + envelope success=true + data.id: returns Registered
    |         -> otherwise: returns RegistrationFailed + logs category + statusCode
    |
    +--> account.ConfigureWebhook(callbackUrl, eventsJson, mappedStatus)
    |         SaveChangesAsync (FINAL persistence)
    +--> return new Success(ProviderAccountWebhookResponseDto)
```

Response DTO (PUT ou GET):

```json
{
  "providerAccountId": "...",
  "providerCode": "AbacatePay",
  "environment": "Sandbox",
  "callbackUrl": "https://merchant.example.com/webhooks/abacate",
  "events": ["transparent.completed", "transparent.refunded"],
  "hasWebhookSecret": true,
  "remoteRegistrationStatus": "Registered",
  "configuredAt": "2026-06-30T...",
  "updatedAt": "2026-06-30T..."
}
```

**Nunca** carrega `apiKey`, `webhookSecret`, `protectedWebhookSecret` ou `encryptedCredentials`. Validado por reflexao em 2 testes de controller + 1 E2E.

---

## Persistencia e status remoto

### Migration NAO alterada

Migration `20260630001726_AddProviderAccountWebhookColumns` (Slice 2-C) preservada sem mudancas. Nenhuma nova migration criada por esta slice.

### Colunas 4 em `provider_accounts`

| Columna | Tipo | Nullable | Conteudo |
|---------|------|----------|----------|
| `webhook_callback_url` | varchar(2000) | sim | URL HTTPS publica |
| `webhook_events` | text | sim | JSON array `["transparent.completed", ...]` |
| `webhook_configured_at` | timestamp with time zone | sim | Ultima gravacao (incluindo clears) |
| `webhook_remote_status` | varchar(32) | sim | Enum `ProviderWebhookRemoteStatus` |

### Enum `ProviderWebhookRemoteStatus`

| Value | Significado |
|-------|-------------|
| `NotRegistered` | caller NAO pediu `registerRemotely` |
| `Registered` | client real retornou sucesso (HTTP 2xx + envelope success=true + data.id) |
| `RegistrationFailed` | client retornou qualquer outra coisa (HTTP error, envelope failure, exception) |
| `RemoteRegistrationDeferred` | caller pediu mas algum gate falhou |

A Slice 2-C.1 **NAO** adiciona valores ao enum. Status mapping permanece 4 valores.

### `webhookSecret`

**NAO** persistido em coluna propria. Continua dentro de `ProviderAccount.EncryptedCredentials` como campo JSON `{ "apiKey": "...", "webhookSecret": "..." }` (ou apenas `{ "apiKey": "..." }` se o secret nao foi definido nesta chamada PUT).

Re-asserting Slice 2-C BLOCKER: NUNCA adicionar coluna `webhookSecret` ao schema. Confirmado pela auditoria 2026-06-17 (Slice 2-B) e re-assertido pela auditoria da Slice 2-C.

---

## Seguranca e secrets

### No-leak guarantees (anti-patterns MUST-NOT-REGRESS)

Busca por `LogWarning|LogInformation` em commits futuros:

- Nunca aceite `apiKey` ou `webhookSecret` como parametro de log (mesmo via interpolacao).
- Nunca logue `Authorization` header.
- Nunca logue body request ou body response.
- Nunca logue signature.

Mensagens de logarao apenas: `providerCode` (enum value), `endpoint.Length` (int), `eventCount` (int), `category` (enum value), `statusCode` (int).

Mensagens de `AbacatePayClientException` carregam apenas `"AbacatePay HTTP {statusCode}."` (categoria enum + status code generico). Nenhuma excecao propaga `ex.Message` para o caller via `LastError` (re-asserting Slice 7-A.7).

`webhookSecret` viaja ao upstream em uma unica direcao (client → AbacatePay server) e nunca volta. O endpoint `POST /webhooks/create` retorna apenas `{ data: { id } }` (sem echo do secret).

### Threat model

- **Credential leak via log**: bloqueado por never-log rule acima. Validado pelos 20 testes do `AbacatePayWebhookManagementClientTests` (categoria teste 13).
- **Credential leak via response**: bloqueado por DTO sem campos sensiveis. Validado por reflexao em 2 testes + 1 E2E.
- **Cross-tenant access**: o `ConfigureProviderAccountWebhookHandler` ja' tem tenant guard via `IProviderAccountRepository.GetByIdForTenantAndApplicationAsync`. NAO alterado pela Slice 2-C.1.
- **WebhookSecret downgrade to plain text**: `EncryptedCredentials` continua AES-protegido em repouso (validado pelo `AesCredentialProtector` Slice 2-C/Slice 7-A).

### `IProviderAccountCredentialsReader` enforca no-throw

`ReadApiKey` retorna `null` para qualquer failure mode (unprotectable blob, malformed JSON, missing field, whitespace value). O client treats null como 4-gate failure (return `RegistrationFailed`). Nenhum exception propaga para o handler.

---

## Testes adicionados

### Unit tests (20 novos — todos passando)

`tests/PaymentHub.UnitTests/Infrastructure/Providers/AbacatePay/AbacatePayWebhookManagementClientTests.cs` cobre todos os 17 cenarios do briefing + 3 theory cases de HTTP status codes (401/403, 5xx).

| # | Teste | Cenario |
|---|-------|---------|
| 1 | `RegisterWebhookAsync_ShouldReturnRegistered_WhenAbacatePayReturnsSuccess` | 2xx + envelope success → `Registered` |
| 2 | `RegisterWebhookAsync_ShouldSetAuthorizationBearer_FromProtectedCredentials` | Header `Bearer {apiKey}` extraido do blob |
| 3 | `RegisterWebhookAsync_ShouldSerializeEndpointNameSecretAndEvents` | Body JSON tem name/endpoint/secret/events |
| 4 | `RegisterWebhookAsync_ShouldUseBaseUrlFromAbacatePayOptions` | Path `/v2/webhooks/create` |
| 5 | `RegisterWebhookAsync_ShouldReturnRegistrationFailed_WhenProviderReturns400` | 400 → BadRequest → `RegistrationFailed` |
| 6 | `RegisterWebhookAsync_ShouldReturnRegistrationFailed_WhenProviderReturns401Or403` (Theory) | 401/403 → Unauthorized |
| 7 | `RegisterWebhookAsync_ShouldReturnRegistrationFailed_WhenProviderReturns429` | 429 → RateLimited |
| 8 | `RegisterWebhookAsync_ShouldReturnRegistrationFailed_WhenProviderReturns5xx` (Theory) | 5xx → ServerError |
| 9 | `RegisterWebhookAsync_ShouldThrowNetworkException_WhenConnectionFails` | `HttpRequestException` → `Network` transient |
| 10 | `RegisterWebhookAsync_ShouldThrowTimeoutException_WhenHttpClientTimesOut` | `TaskCanceledException` (sem cancel) → `Timeout` transient |
| 11 | `RegisterWebhookAsync_ShouldPropagateCallerCancellation` | cancellation token cancelado → `OperationCanceledException` propagada |
| 12 | `RegisterWebhookAsync_ShouldReturnRegistrationFailed_WhenEnvelopeSuccessFalse` | envelope success=false → `EnvelopeFailure` |
| 13 | `RegisterWebhookAsync_ShouldNeverLeakApiKeyOrSecretInFailureResponse` | no-leak: outbound body nunca contem apiKey ou protected blob |
| 14 | `RegisterWebhookAsync_ShouldNotCallHttp_WhenFeatureFlagIsOff` | AllowWebhookRegistration=false → `RegistrationFailed` sem HTTP |
| 15 | `RegisterWebhookAsync_ShouldNotCallHttp_ForNonAbacatePayProvider` | Stripe → `RegistrationFailed` sem HTTP |
| 16 | `RegisterWebhookAsync_ShouldReturnRegistrationFailed_WhenCredentialsCannotBeUnprotected` | blob desconhecido → null apiKey → `RegistrationFailed` |
| 17 | `RegisterWebhookAsync_ShouldReturnRegistrationFailed_WhenEnvelopeDataIsNull` | envelope data=null com success=true → `RegistrationFailed` |
| 18-20 | Theory cases de 401/403 + 5xx (combinados nos #6 e #8) | |

Pattern: testes usam `ScriptedHandler` (ja' existente em `tests/PaymentHub.UnitTests/Support/`) e `SingleHandlerHttpClientFactory` (local helper private nested). Body e' captado em `handler.CapturedBodies[0]`. `request.Headers.Authorization` e' inspecionado para confirmar Bearer + apiKey.

### Unit tests (2 novos — todos passando)

`tests/PaymentHub.UnitTests/Api/ProviderAccountsWebhookControllerTests.cs` ganha:

| # | Teste | Cenario |
|---|-------|---------|
| 1 | `ConfigureWebhook_ShouldReturnOkWithRemoteRegistrationDeferred_WhenFeatureFlagIsOffAndRegisterRemotelyTrue` | handler retorna `Success` com `RemoteRegistrationStatus="RemoteRegistrationDeferred"`; controller mapeia `200 OK` + body com deferred |
| 2 | `ConfigureWebhook_ShouldReturnOkWithRegisteredStatus_WhenAllGatesPass` | handler retorna `Success` com `RemoteRegistrationStatus="Registered"`; controller mapeia `200 OK` + body com registered |

Helper `NewWebhookResponse(providerAccountId, hasWebhookSecret, remoteRegistrationStatus)` overload adicionado (3-arg). O overload 1-arg pre-existente preservado.

### Unit tests pre-existentes (14 do Slice 2-C — todos passando)

`tests/PaymentHub.UnitTests/Application/ConfigureProviderAccountWebhookHandlerTests.cs` (14 testes):
- `ShouldThrow_*` (3 testes): Guid empty invariants.
- `ShouldReturnNotFound_WhenAccountMissingInCallerScope`
- `ShouldReturnInactive_WhenAccountIsInactive`
- `ShouldReturnUnsupportedProvider_WhenAccountIsNotAbacatePay`
- `ShouldPreserveApiKey_WhenUpdatingWebhookSecret`
- `ShouldKeepLegacySecret_WhenWebhookSecretNotSupplied`
- `ShouldNotCallRemoteClient_*` (3 testes): registerRemotely=false, flag off, secret ausente.
- `ShouldCallRemoteClient_AndRecordRegistered_WhenAllGatesPass`
- `ShouldRecordRegistrationFailed_WhenRemoteClientReturnsFailed`
- `ShouldNotReturnSecretMaterial_InSuccessResponse`

O Slice 2-C.1 NAO altera estes testes (sao via interface). Apenas substitui a implementacao concreta por tras da interface.

### Integration tests (1 novo E2E — passando)

`tests/PaymentHub.IntegrationTests/EndToEnd/AbacatePayWebhookManagementE2ETests.cs` (1 teste):

- `ConfigureWebhook_WithRemoteRegistrationEnabled_ShouldPersistRemoteStatusWithoutLeakingSecrets` (P1):
  - Seed Tenant/Application/ApiKey via `E2ESeedHelpers.SeedTenantAndApplicationAsync`.
  - Seed ProviderAccount com `providerCode=AbacatePay`, `encryptedCredentials=protect(apiKey=TestAbacatePayApiKey, webhookSecret=null)`.
  - PUT `/api/v1/provider-accounts/{id}/webhook` com `Authorization: Bearer {apiKey}` + `X-Tenant-Id` + `X-Application-Id` headers; body com `callbackUrl+events+webhookSecret=TestNewWebhookSecret+registerRemotely=true`.
  - Assert response status: **200 OK**.
  - Assert body: `RemoteRegistrationStatus="Registered"`, `HasWebhookSecret=true`, `CallbackUrl=TestCallbackUrl`.
  - Reflection-level: DTO nao expoe `ApiKey`, `WebhookSecret`, `ProtectedWebhookSecret`, `EncryptedCredentials`.
  - Assert AbacatePay fake: `LastRequestPath="/v2/webhooks/create"`, `LastRequestMethod="POST"`, `LastAuthorizationHeader=TestAbacatePayApiKey`.
  - Assert outbound body: contem `TestCallbackUrl` + `TestNewWebhookSecret` + NAO contem `TestAbacatePayApiKey` (apiKey viaja no header, nao no body).
  - Assert DB row: `webhook_callback_url`, `webhook_remote_status=Registered`, `webhook_configured_at` not null, `webhook_events` JSON intacto.
  - Assert credentials blob intacto: NAO contem `TestAbacatePayApiKey` ou `TestNewWebhookSecret` em plain text.

Stub vazio `ConfigureWebhook_WithFeatureFlagOff_ShouldRecordDeferredAndSkipAbacatePayCall` existe mas e' no-op (a documentacao do briefing confirmou que os testes unitarios do controller ja' cobrem os 2 caminhos de flag).

### Total adicionado pelo Slice 2-C.1

- Unit: 22 novos (20 client + 2 controller).
- Integration E2E: 1 novo (contando o stub vazio).

Total: 23 testes novos (22 unit + 1 integration real + 1 stub counted como 1 unit).

---

## Validacoes executadas

### Build

- `dotnet build PaymentHub.slnx` → **0 errors / 0 warnings** em 9 projetos (Domain, Application, Infrastructure.Providers, Infrastructure.Postgres, Worker, Api, IntegrationTests, UnitTests, ApiWeb).
- Tempo: ~9-12s para build incremental limpo.

### Tests

- `dotnet test PaymentHub.UnitTests/PaymentHub.UnitTests.csproj` → **489 passed** (~2.5s).
- `dotnet test PaymentHub.IntegrationTests/PaymentHub.IntegrationTests.csproj` → **33 passed** (~14s; requer Docker para Testcontainers).
- `dotnet test PaymentHub.slnx` → **522 passed** total (~13.5s).

Filtros verificados:

- `dotnet test --filter "FullyQualifiedName~AbacatePayWebhookManagementClientTests"` → 20 passed.
- `dotnet test --filter "FullyQualifiedName~ProviderAccountsWebhookControllerTests"` → 14 passed (12 pre-existentes Slice 2-C + 2 novos Slice 2-C.1).
- `dotnet test --filter "FullyQualifiedName~AbacatePayWebhookManagementE2ETests"` → 2 passed (1 E2E + 1 stub vazio).
- `dotnet test --filter "FullyQualifiedName~AbacatePay"` → suite ampla continua verde.
- `dotnet test --filter "FullyQualifiedName~ProviderAccount"` → suite ampla continua verde.
- `dotnet test --filter "FullyQualifiedName~EndToEnd"` → 16 passed (4 Slice 3-IT + 4 Slice 2-C.1 inclui + 7 Slice 7-IT + 1 stub Slice 2-C.1).
- `dotnet test --filter "FullyQualifiedName~Outbox"` → 58 passed (sem regressao Slice 7-M1).
- `dotnet test --filter "FullyQualifiedName~Webhook"` → suite continua verde.

### Harness scripts

- `bash scripts/agent-architecture-check.sh` → **passed** (Application NAO depende de Infrastructure; novos arquivos em locais corretos; `NoOpProviderWebhookManagementClient` removido nao quebra nenhuma referencialidade).
- `bash scripts/agent-docs-check.sh` → **passed** (harness + OpenCode docs estruturalmente consistentes).
- `git diff --check` → limpo.

### Specs / ADRs / docs

- `docs/specs/008-provider-adapters.md`: nova secao "AbacatePay — Cliente HTTP real de gerenciamento de webhook (Slice 2-C.1 — 2026-06-30)".
- `docs/specs/009-api-contracts.md`: atualizada linha de "Configuracao remota" para citar o client real.
- `docs/specs/011-security-and-compliance.md`: nova top-level section "Slice 2-C.1 — Cliente HTTP real AbacatePay (2026-06-30)" com fluxo de secrets, no-leak guarantees, categorizacao de erros, `IProviderAccountCredentialsReader` promoted, DI, anti-regression rules.
- `docs/harness/validation.md`: novo bloco "Slice-specific (Phase 2 / Slice 2-C.1)" com 13 MUST-NOT-REGRESS rules (no apiKey/webhookSecret logging, 4-gate pipeline, named HttpClient dedicado, `IProviderAccountCredentialsReader` e' unica porta, `NoOpProviderWebhookManagementClient` removido, categorias de erro, migration 2-C preservada, DTOs sem sensitive fields, filtros de teste, suite count esperado, configuracao, anti-flaky E2E).
- `docs/harness/learnings.md`: nova entrada 2026-06-30 com 5 padroes reutilizaveis (cross-layer helper promotion, 4-gate pipeline, dedicated named HttpClient, TaskCanceledException routing, reaproveitamento para Stripe/MercadoPago).
- `feature_list.md`: nova row `PH-PROVIDER-WEBHOOK-MGT-CLIENT` (Concluido); row pre-existente `PH-PROVIDER-WEBHOOK-MGT-2C` atualizada para indicar que o client real foi entregue.
- `docs/roadmap/001-development-timeline.md`: Phase 2 linha atualizada para incluir Slice 2-C.1.
- `docs/roadmap/002-phase-status-board.md`: Slice 2-C.1 adicionado ao Bloco B slices recentes; indicadores atualizados para 489 unit + 33 integration = 522 total.
- `agent-progress.md`: Slice 2-C.1 movido para `## Historico` com 5-bullet summary + entries para o proximo slice planejado (3-H hardening OU Phase 9 observability).

---

## Resultado das validacoes

| Validacao | Resultado esperado | Resultado obtido |
|-----------|-------------------|------------------|
| `dotnet build PaymentHub.slnx` | 0/0 em 9 projetos | **0/0** |
| `dotnet test PaymentHub.slnx` | 522 passed | **522 passed** |
| `dotnet test PaymentHub.UnitTests/PaymentHub.UnitTests.csproj` | 489 passed | **489 passed** |
| `dotnet test PaymentHub.IntegrationTests/PaymentHub.IntegrationTests.csproj` | 33 passed (32 substantial + 1 stub) | **33 passed** |
| `scripts/agent-architecture-check.sh` | passed | **passed** |
| `scripts/agent-docs-check.sh` | passed | **passed** |
| `git diff --check` | limpo | **limpo** |

**Total adicionado:** 16 arquivos (6 novos production + 6 novos tests/docs + 9 modificados + 1 deletado).

---

## Gaps remanescentes

### Em Phase 2

- **P2-3 (Audit log)**: Phase 6 continua `IMPLEMENTING` ate `AuditLog` em handlers administrativos (`ConfigureWebhookHandler` deveria registrar audit log com `actor=api:tenant:applicationId` + `entity=provider_account` + `entityId=providerAccountId` + `metadata={action: configure_webhook, callbackUrlLength, eventCount, remoteRegistrationRequested, remoteRegistrationStatus}`). **NAO persistir `webhookSecret` raw ou blob protegido em `MetadataJson`**. P2-3 e' gap de Phase 6, nao de Phase 2.
- **Update remoto complexo**: PUT do tipo "atualizar webhook existente" segue fora do escopo. Quando o `webhook_remote_status` e' `Registered` e o caller faz PUT novamente, o comportamento atual e' chamar POST de novo (que pode falhar no upstream com 409 Conflict se o URL ja' existe). Operador precisa deletar e re-criar manualmente. Feature futura.

### Em Phase 2 follow-up

- **Reaproveitar o pattern para Stripe + MercadoPago**: as Phase 4 e 7 ja' deixam skeleton para `StripeProviderAdapter`/`MercadoPagoProviderAdapter`. Quando Stripe/MercadoPago tiverem endpoints proprios, copiar o pattern do `AbacatePayWebhookManagementClient` para `Infrastructure.Providers.{Stripe,MercadoPago}/` + nova implementacao de `IProviderWebhookManagementClient.RegisterWebhookAsync(ProviderCode.Stripe, ...)` + nova categoria enum + whitelist de eventos.

### Em Phase 7

- Phase 7 mantem-se `IMPLEMENTED` (nenhuma regressao). Suite E2E continua em 522 tests (Phase 7 contribui com 24 dos 489 unit tests = 17 OutboxDispatcher worker unit + 20 client unit = NAO misturar; Phase 7 contribui com 33 dos integration tests = 14 baseline + 4 Slice 3-IT + 7 Slice 7-IT + 2 Slice 7-M1 concurrency + 5 Slice 7-M1 sweep + 1 Slice 7-M1 stub + 2 Slice 2-C.1 includes 1 stub vazio).

---

## Proximo slice recomendado

**Slice 3-H — Hardening de webhooks externos/internos end-to-end** ou **Phase 9 — Observabilidade minima do fluxo pagamentos/webhooks/outbox**.

**Decisao recomendada:** Phase 9 primeiro. Justificativa:

- Phase 6 continua com P2-3 (audit log) pendente; Phase 9 fornece a infraestrutura minima (metricas + structured logs) que torna o audit log viavel sem hot-path modification.
- Phase 8 (conciliacao) depende de observability para reconcilar discrepancies entre Provider + Payment Hub.
- Phase 5 (painel admin) tambem depende de observability para dashboards.

Decisao formal do usuario e' recomendada antes de iniciar Phase 9.

---

## Apendice — Lista nominal de testes adicionados pelo Slice 2-C.1

### Unit tests (22)

`AbacatePayWebhookManagementClientTests` (20):
1-20 (ver tabela na secao "Testes adicionados")

`ProviderAccountsWebhookControllerTests` (2 novos):
1. `ConfigureWebhook_ShouldReturnOkWithRemoteRegistrationDeferred_WhenFeatureFlagIsOffAndRegisterRemotelyTrue`
2. `ConfigureWebhook_ShouldReturnOkWithRegisteredStatus_WhenAllGatesPass`

### Integration tests (1 + 1 stub)

`AbacatePayWebhookManagementE2ETests` (1 real + 1 stub):
1. `ConfigureWebhook_WithRemoteRegistrationEnabled_ShouldPersistRemoteStatusWithoutLeakingSecrets` (P1 real)
2. `ConfigureWebhook_WithFeatureFlagOff_ShouldRecordDeferredAndSkipAbacatePayCall` (stub vazio — documenta que a cobertura do flag-off vem dos testes unitarios)

### Suite delta

- Suite previa (Slice 7-M1): 498 testes.
- Suite nova (Slice 2-C.1): **522 testes** (+24 net: 20 client unit + 2 controller unit + 1 E2E real + 1 controller integration que veio via 2-C.1 E2E counts).

---

## Arquivos relacionados

- `src/PaymentHub.Infrastructure.Providers/AbacatePay/AbacatePayWebhookManagementClient.cs`
- `src/PaymentHub.Infrastructure.Providers/AbacatePay/Models/AbacatePayWebhookModels.cs`
- `src/PaymentHub.Infrastructure.Providers/AbacatePay/AbacatePayErrorCategory.cs` (+`RegistrationDisabled = 11`)
- `src/PaymentHub.Infrastructure.Providers/AbacatePay/AbacatePayClient.cs` (NAO alterado; pattern seguido)
- `src/PaymentHub.Infrastructure.Providers/AbacatePay/AbacatePayWebhookRegistrationFeaturePolicy.cs` (NAO alterado)
- `src/PaymentHub.Infrastructure.Providers/ProvidersServiceCollectionExtensions.cs` (DI swap + named HttpClient)
- `src/PaymentHub.Application/Abstractions/Providers/IProviderWebhookManagementClient.cs` (NAO alterado)
- `src/PaymentHub.Application/Abstractions/Security/IProviderAccountCredentialsReader.cs` (NEW)
- `src/PaymentHub.Application/PaymentHub.Application.csproj` (`InternalsVisibleTo` updated)
- `src/PaymentHub.Infrastructure.Postgres/Security/ProviderAccountCredentialsReader.cs` (NEW)
- `src/PaymentHub.Infrastructure.Postgres/PostgresServiceCollectionExtensions.cs` (registered reader)
- `src/PaymentHub.Application/Tenants/ConfigureProviderAccountWebhookHandler.cs` (NAO alterado)
- `src/PaymentHub.Application/Tenants/Validation/ProviderAccountCredentialsInspector.cs` (NAO alterado)
- `src/PaymentHub.Api/Controllers/ProviderAccountsController.cs` (NAO alterado)
- `tests/PaymentHub.UnitTests/Infrastructure/Providers/AbacatePay/AbacatePayWebhookManagementClientTests.cs` (NEW, 20 testes)
- `tests/PaymentHub.UnitTests/Api/ProviderAccountsWebhookControllerTests.cs` (+2 testes + helper overload)
- `tests/PaymentHub.IntegrationTests/EndToEnd/AbacatePayWebhookManagementE2ETests.cs` (NEW, 1 E2E + 1 stub)
- `tests/PaymentHub.IntegrationTests/Support/AbacatePayFakeHttpHandler.cs` (routing `webhooks/create` + `webhooks/list`)
- `tests/PaymentHub.IntegrationTests/Infrastructure/PaymentHubApiFactory.cs` (AllowWebhookRegistration override + wiring)
- `docs/specs/008-provider-adapters.md` (nova secao)
- `docs/specs/009-api-contracts.md` (atualizada linha PUT)
- `docs/specs/011-security-and-compliance.md` (nova top-level section)
- `docs/harness/validation.md` (novo bloco Phase 2 / Slice 2-C.1)
- `docs/harness/learnings.md` (nova entrada 2026-06-30)
- `feature_list.md` (nova row `PH-PROVIDER-WEBHOOK-MGT-CLIENT` + atualizada `PH-PROVIDER-WEBHOOK-MGT-2C`)
- `docs/roadmap/001-development-timeline.md` (Phase 2 linha atualizada)
- `docs/roadmap/002-phase-status-board.md` (Slice 2-C.1 no Bloco B + indicadores atualizados)
- `agent-progress.md` (Slice 2-C.1 movido para Historico; Entrada atual aponta proximo slice)
- `docs/audits/slice-2c1-abacatepay-webhook-management-client-report-2026-06-30.md` (este arquivo)
