# Slice 2-A — AbacatePay Sandbox Transparent PIX Report

- Data: 2026-06-27
- Phase: 2 (Primeiro Adapter de Provider)
- Specs relacionadas: `docs/specs/008-provider-adapters.md`, `docs/specs/004-payment-lifecycle.md`, `docs/specs/011-security-and-compliance.md`, `docs/specs/009-api-contracts.md`, `docs/specs/002-multitenancy-and-authentication.md`
- ADRs consultadas: indiretas via `docs/adr/000-adr-index.md` (nenhuma ADR nova; o padrao de secrets e webhooks ja esta consolidado em `ADR-0007-webhook-secret-protection.md`)
- Gap enderecado: dependencia de provider real em Phase 2 + ausencia de cobertura AbacatePay no mapper (`P2-1` parcial — webhooks externos ficam em Slice 2-B).

## Resumo

Ate o Slice 2-A, `AbacatePayProviderAdapter` era skeleton retornando `Success=false` hard-coded, e Phase 2 nao tinha nenhum provider real (apenas o `FakePaymentProviderAdapter` funcional). Este slice entrega o **primeiro adapter AbacatePay funcional** para **Checkout Transparente PIX** em sandbox/devMode:

- `IAbacatePayClient` + `AbacatePayClient`: client HTTP tipado via `IHttpClientFactory`, `Authorization: Bearer <api-key>`, envelope `{ data, success, error }`, mapeamento categorizado de 400/401/403/404/429/5xx + network + timeout + envelope-failure + simulation-disabled. Mensagens de `AbacatePayClientException` **nunca** incluem API key, body, `brCodeBase64` ou response body.
- `AbacatePayProviderAdapter` reescrito: unprotect via `ICredentialProtector`, extrai `apiKey`, monta payload PIX (amount em centavos, customer omit-if-null, metadata com tenantId/applicationId/paymentId/externalReference, expiry 3600s), mapeia status canonico via `PaymentStatusMapper`, popula `RawResponseJson` minimamente e sintetiza `abacatepay://pix/<id>` como `CheckoutUrl` (consumidores que precisarem renderizar QR Code imediato consomem `RawResponseJson` ate micro-slice de API).
- `CreateCheckoutProviderRequest` ganha 3 init-only opcionais (`ProviderAccountId`, `ProviderEnvironment`, `ProtectedCredentials`). Backward-compat: construtor posicional preservado, adapters Fake/Stripe/MercadoPago continuam compilando.
- `CreateCheckoutHandler.ResolveProviderAsync` retorna novo record `ResolvedProvider` com `ProviderAccountId`, `Environment`, `EncryptedCredentials` — preserva o account em vez de descarta-lo apos checar existencia.
- `PaymentStatusMapper.MapAbacatePay` estendido: `redeemed → Approved`, `under_dispute → Pending` (decisoes documentadas em teste).
- DI: `AddPaymentHubProviders` registra `IOptionsMonitor<AbacatePayOptions>`, `HttpClient "abacatepay"` nomeado (timeout do options), `IAbacatePayClient` Singleton, adapter Singleton (sem captive-dependency).
- `appsettings.json` + `appsettings.Development.json` ganham secao `Providers:AbacatePay` (`AllowDevModeSimulation=false` em producao, `true` em dev). Nenhuma API Key real commitada.

Suite previa: 281 unitarios + 10 integracao = 291. Suite nova: **348 testes** (291 baseline + 40 client + 17 adapter). Build limpo (0 errors / 0 warnings em 9 projetos). `scripts/agent-architecture-check.sh` e `scripts/agent-docs-check.sh` verdes. `git diff --check` limpo.

## Objetivo

Implementar o primeiro adapter funcional AbacatePay para Checkout Transparente PIX em sandbox/devMode, com client HTTP tipado, Bearer Token, criacao PIX, consulta de status, simulacao opt-in devMode quando segura, mapeamento de status e testes unitarios sem chamada externa real.

## Discovery (modo read-only, antes de qualquer codigo)

- `AbacatePayProviderAdapter` era skeleton: `CreateCheckoutAsync` retornava `Success=false` hard-coded com mensagem "AbacatePay adapter not yet implemented."; `ParseWebhookAsync` fazia parsing JSON naive sem HMAC.
- `IPaymentProviderAdapter` expunha apenas `ProviderCode`, `CreateCheckoutAsync`, `ParseWebhookAsync`. Sem `CheckStatusAsync` — o status sync interno ficaria no client + adapter concreto (planner Decision 2).
- `CreateCheckoutProviderRequest` nao carregava `ProviderAccountId`, `ProviderEnvironment`, nem `ProtectedCredentials`. Adapters eram Singleton — **NAO injetar repository scoped** (planner Decision 1): passar credencial protegida via request DTO.
- `CreateCheckoutHandler.ResolveProviderAsync` retornava `ProviderCode` e descartava `ProviderAccount` apos validar existencia.
- `ICredentialProtector` ja existia em `PaymentHub.Application.Abstractions.Security.ICrypto`. `RegisterProviderAccountHandler` ja protegia o JSON `{ apiKey, secret }` via AES — formato canonico do projeto para "segredo reversivel em repouso".
- `PaymentStatusMapper.MapAbacatePay` ja mapeava `pending/paid/approved/expired/cancelled/canceled/refunded/failed`. Faltavam testes explicitos e os status `redeemed`/`under_dispute` da doc oficial.
- AbacatePay publica REST/JSON em `https://api.abacatepay.com/v2` com `Authorization: Bearer`, valores em centavos e envelope padrao `{ data, success, error }`. PIX Checkout Transparente retorna `brCode` + `brCodeBase64` + `expiresAt`.

## Escopo implementado

1. `AbacatePayOptions` (`Providers:AbacatePay`): `BaseUrl`, `TimeoutSeconds`, `AllowDevModeSimulation` (default seguro `false`).
2. `AbacatePayErrorCategory` (10 valores: `BadRequest`, `Unauthorized`, `NotFound`, `RateLimited`, `ServerError`, `Network`, `Timeout`, `EnvelopeFailure`, `Unexpected`, `SimulationDisabled`).
3. `AbacatePayClientException`: carrega `Category`, `StatusCode?`, `IsTransient` (default derivado da categoria). Mensagem **nao** inclui API key, header `Authorization`, request body, response body, `brCodeBase64` ou `brCode`.
4. Models JSON: `AbacatePayEnvelope<T>`, `AbacatePayCustomerRequest`, `AbacatePayCreateTransparentPixRequest`, `AbacatePayCreateTransparentPixResponse`, `AbacatePayCheckTransparentPixResponse`, `AbacatePaySimulatePaymentResponse`.
5. `IAbacatePayClient` + `AbacatePayClient`: 3 metodos (`CreateTransparentPixAsync`, `CheckTransparentPixAsync`, `SimulateTransparentPixPaymentAsync`).
6. `AbacatePayProviderAdapter` reescrito: injeta `IAbacatePayClient`, `ICredentialProtector`, `ILogger`. Unprotect JSON → extract `apiKey` → monta payload PIX → chama client → mapeia status → preenche `RawResponseJson` + `ProviderPaymentId` + `CheckoutUrl` sintetica.
7. `CreateCheckoutProviderRequest` estendido com 3 init-only opcionais (backward-compat com Fake/Stripe/MercadoPago).
8. `CreateCheckoutHandler` ganhou `ResolvedProvider` record e propagacao de account context.
9. DI: `Microsoft.Extensions.Http 10.0.0` adicionado a `PaymentHub.Infrastructure.Providers.csproj`; `services.Configure<AbacatePayOptions>(...)`, `services.AddHttpClient("abacatepay", ...)` com timeout, `services.AddSingleton<IAbacatePayClient, AbacatePayClient>()`.
10. `appsettings.json` + `appsettings.Development.json` com secao `Providers:AbacatePay` (defaults seguros + opt-in dev para simulation).
11. 57 testes novos: 40 em `AbacatePayClientTests` + 17 em `AbacatePayProviderAdapterTests`.
12. `FakeCredentialProtector` em `tests/PaymentHub.UnitTests/Support/` (mesmo pattern de `FakeWebhookSecretProtector`).
13. `PaymentStatusMapper.MapAbacatePay` ganha `redeemed → Approved` e `under_dispute → Pending`.
14. `PaymentStatusMapperTests` ganha 4 metodos cobrindo todos os status do mapping + case-insensitive provider code + status desconhecido/vazio.

## Fora de escopo (deferred para slices futuros)

- Webhooks externos completos: HMAC-SHA256 + timestamp + event normalization + tenant routing. Mantido o `ParseWebhookAsync` atual (parsing JSON naive). Enderecado em **Slice 2-B** (a abrir).
- Integracao sandbox end-to-end com chave AbacatePay real: este slice NAO executa chamada externa real; cobertura e via `ScriptedHandler` + `SingleHandlerHttpClientFactory` isolando o IO.
- Stripe e MercadoPago adapters reais: continuam skeleton (`IPaymentProviderAdapter` instanciado, `CreateCheckoutAsync` nao implementado).
- Multi-provider active-active, fallback entre providers, retries com `Idempotency-Key` no client HTTP: escopo do Slice 3-IT ou Slice 2-B conforme decisao de produto.
- Expor `brCode`/`brCodeBase64` no contrato HTTP publico (`POST /api/v1/checkouts`): exige ADR ou micro-slice de API. Hoje, `RawResponseJson` ja carrega o payload minimo para o cliente que precisar renderizar QR imediato.
- Auditoria de `AuditLog` em `AbacatePayProviderAdapter` (P2-3): `RegisterProviderAccountHandler` continua sendo o unico path com AuditLog hoje; o adapter apenas executa o fluxo de checkout.

## Arquivos criados (14)

| Arquivo | Proposito |
|---------|-----------|
| `src/PaymentHub.Infrastructure.Providers/AbacatePay/AbacatePayOptions.cs` | Options tipado (BaseUrl / TimeoutSeconds / AllowDevModeSimulation). |
| `src/PaymentHub.Infrastructure.Providers/AbacatePay/AbacatePayErrorCategory.cs` | Enum de 10 categorias com defaults transient. |
| `src/PaymentHub.Infrastructure.Providers/AbacatePay/AbacatePayClientException.cs` | Exception segura (Category + StatusCode + IsTransient; sem payload). |
| `src/PaymentHub.Infrastructure.Providers/AbacatePay/IAbacatePayClient.cs` | Contrato outbound (Create / Check / Simulate). |
| `src/PaymentHub.Infrastructure.Providers/AbacatePay/AbacatePayClient.cs` | Implementacao HTTP via IHttpClientFactory + Bearer + envelope parser + categorizador. |
| `src/PaymentHub.Infrastructure.Providers/AbacatePay/Models/AbacatePayEnvelope.cs` | `{ data, success, error }`. |
| `src/PaymentHub.Infrastructure.Providers/AbacatePay/Models/AbacatePayCustomerRequest.cs` | Customer block (name, email, taxId, cellphone). |
| `src/PaymentHub.Infrastructure.Providers/AbacatePay/Models/AbacatePayCreateTransparentPixRequest.cs` | Payload com amount / description / expiresIn / customer / metadata. |
| `src/PaymentHub.Infrastructure.Providers/AbacatePay/Models/AbacatePayCreateTransparentPixResponse.cs` | Id / status / amount / expiresAt / brCode / brCodeBase64 / devMode. |
| `src/PaymentHub.Infrastructure.Providers/AbacatePay/Models/AbacatePayCheckTransparentPixResponse.cs` | Id / status / amount / paidAt / expiresAt. |
| `src/PaymentHub.Infrastructure.Providers/AbacatePay/Models/AbacatePaySimulatePaymentResponse.cs` | Id / status (mirror do check). |
| `tests/PaymentHub.UnitTests/Infrastructure/Providers/AbacatePay/AbacatePayClientTests.cs` | 40 testes cobrindo Bearer, envelope, status codes, cancellation, simulation gate. |
| `tests/PaymentHub.UnitTests/Infrastructure/Providers/AbacatePay/AbacatePayProviderAdapterTests.cs` | 17 testes cobrindo unprotect, payload, mapping, no-leak. |
| `tests/PaymentHub.UnitTests/Support/FakeCredentialProtector.cs` | Helper in-memory reversivel (marker `fake-cred|`). |

## Arquivos alterados (10)

| Arquivo | Mudanca |
|---------|---------|
| `src/PaymentHub.Infrastructure.Providers/PaymentHub.Infrastructure.Providers.csproj` | + `Microsoft.Extensions.Http 10.0.0`. |
| `src/PaymentHub.Infrastructure.Providers/AbacatePay/AbacatePayProviderAdapter.cs` | Reescrito: injeta client + protector + logger; unprotect → extract → payload → mapping. |
| `src/PaymentHub.Infrastructure.Providers/ProvidersServiceCollectionExtensions.cs` | Bind options + named `HttpClient "abacatepay"` + Singleton client + Singleton adapter. |
| `src/PaymentHub.Application/Abstractions/Providers/ProviderModels.cs` | `CreateCheckoutProviderRequest` ganha 3 init-only: `ProviderAccountId`, `ProviderEnvironment`, `ProtectedCredentials`. |
| `src/PaymentHub.Application/Checkouts/CreateCheckoutHandler.cs` | Novo `internal record ResolvedProvider` + `ResolveProviderAsync` retorna account context + handler propaga para `CreateCheckoutProviderRequest`. |
| `src/PaymentHub.Domain/Services/PaymentStatusMapper.cs` | `MapAbacatePay`: + `redeemed → Approved`, + `under_dispute → Pending`. |
| `src/PaymentHub.Api/appsettings.json` | + secao `Providers:AbacatePay` (defaults seguros: `AllowDevModeSimulation=false`). |
| `src/PaymentHub.Api/appsettings.Development.json` | + secao `Providers:AbacatePay` (dev: `AllowDevModeSimulation=true`). |
| `tests/PaymentHub.UnitTests/Domain/PaymentStatusMapperTests.cs` | + 4 metodos cobrindo todos os status do mapping AbacatePay + case-insensitive + edge cases. |
| `agent-progress.md` | Entrada atual `### Slice 2-A — AbacatePay sandbox funcional (Implementer)` em `## Entrada atual`. (Movido para `## Historico` somente apos validacoes finais do slice.) |

## Configuracao

`appsettings.json` (production):

```json
"Providers": {
  "AbacatePay": {
    "BaseUrl": "https://api.abacatepay.com/v2",
    "TimeoutSeconds": 30,
    "AllowDevModeSimulation": false
  }
}
```

`appsettings.Development.json` (dev):

```json
"Providers": {
  "AbacatePay": {
    "BaseUrl": "https://api.abacatepay.com/v2",
    "TimeoutSeconds": 30,
    "AllowDevModeSimulation": true
  }
}
```

**Sem nenhuma API Key real commitada**. `ProviderAccount.EncryptedCredentials` e o unico lugar onde a chave AbacatePay existe em repouso.

## Credential flow

1. `RegisterProviderAccountHandler` recebe `{ apiKey, secret }` em `RegisterProviderAccountRequestDto` e serializa para JSON. `ICredentialProtector.Protect(json)` grava o blob Base64 em `ProviderAccount.EncryptedCredentials`. DTO de resposta expoe apenas id + tenant + application + provider code + environment + name + isDefault + active + createdAt (sem credential).
2. `CreateCheckoutHandler.ResolveProviderAsync` carrega o `ProviderAccount` via `IProviderAccountRepository.GetByCodeAsync(...)` (ou `GetDefaultAsync(...)` quando nao ha provider explicito) e retorna `ResolvedProvider(ProviderCode, ProviderAccountId, Environment, EncryptedCredentials)`.
3. `CreateCheckoutHandler` propaga `ResolvedProvider.ProviderAccountId`, `Environment.ToString()` e `EncryptedCredentials` para `CreateCheckoutProviderRequest` via init-only.
4. `AbacatePayProviderAdapter.CreateCheckoutAsync`:
   - Se `ProtectedCredentials` ausente → retorna `Success=false` com mensagem "AbacatePay requires ProviderAccount with encrypted credentials." **antes** de chamar o client.
   - Se `ProtectedCredentials` presente → `_protector.Unprotect(blob)` → `JsonDocument.Parse(json)` → `apiKey = root.GetProperty("apiKey").GetString()`. O JSON e descartado apos extracao.
   - `apiKey` viaja em variavel local ate `IAbacatePayClient.CreateTransparentPixAsync(request, apiKey, ct)`.
   - O client NUNCA armazena `apiKey` em campo. E jamais loga o header `Authorization`.

## Create Transparent PIX

- Endpoint: `POST /transparents/create`.
- Headers: `Authorization: Bearer <api-key>`, `Accept: application/json`.
- Body: `{ "amount": <cents>, "description": <externalReference or fallback>, "expiresIn": 3600, "customer": <block ou null>, "metadata": { tenantId, applicationId, paymentId, externalReference } }`.
- Customer block omitido quando `CustomerName` e `CustomerEmail` estao ambos ausentes (evita 400 do provider). Quando preenchido, o bloco inclui apenas os campos nao nulos.
- Metadata sempre presente (4 chaves) para reconciliacao downstream.
- Resposta 2xx com `success=true`: `data.Id` → `ProviderPaymentId`; `data.Status` → `PaymentStatusMapper.FromProviderStatus("AbacatePay", data.Status)`; `data.BrCode` + `data.BrCodeBase64` + `data.ExpiresAt` + `data.DevMode` serializados no `RawResponseJson` (minimo).
- Resposta com falha categorizada (`AbacatePayClientException`) → `Success=false` com `ErrorMessage = $"AbacatePay error ({Category})."` sem expor HTTP body nem API key.

## Check Status

- Endpoint: `GET /transparents/check?id=<providerPaymentId>`.
- Headers: `Authorization: Bearer <api-key>`.
- Retorna `AbacatePayCheckTransparentPixResponse` com `Id`, `Status`, `AmountInCents`, `PaidAt`, `ExpiresAt`.
- **Nao** exposto em `IPaymentProviderAdapter` neste slice — usado apenas pelo adapter concreto para sync interno futuro. Caller `CancellationToken` cancelado propaga como `OperationCanceledException`.

## Simulate Payment

- Endpoint: `POST /transparents/simulate-payment?id=<providerPaymentId>`.
- Opt-in via `Providers:AbacatePay:AllowDevModeSimulation`. Default `false` em `appsettings.json` (production). `true` apenas em `appsettings.Development.json`.
- Quando flag e `false`, o client lanca `AbacatePayClientException(SimulationDisabled)` **antes** de montar request (preferencial a "enviar e descobrir erro").
- Quando flag e `true`, request segue o mesmo padrao do Create: `Authorization: Bearer` + envelope parsing.

## Mapeamento de status

Decisoes em `PaymentStatusMapper.MapAbacatePay` (validadas em `AbacatePay_MapsAllCanonicalStatuses`):

| Provider status bruto | Status canonico | Decisao |
|----------------------|-----------------|---------|
| `PENDING` | `Pending` | Direto |
| `PROCESSING` | `Processing` | Direto |
| `PAID` ou `APPROVED` | `Approved` | PIX liquidado = pago final |
| `REDEEMED` | `Approved` | PIX ja resgatado = pago final (decisao documentada) |
| `EXPIRED` | `Expired` | Direto |
| `CANCELLED` ou `CANCELED` | `Cancelled` | Direto (canonical aceita ambos) |
| `REFUNDED` | `Refunded` | Direto |
| `FAILED` | `Failed` | Direto |
| `UNDER_DISPUTE` | `Pending` | Decisao explicita: ate existir anti-fraude/chargeback no MVP, mantemos intermedio ate conciliacao manual |
| (vazio / desconhecido) | `Pending` | Default seguro (mesmo padrao de outros providers) |

Case-insensitive do provider code: `"AbacatePay"`, `"abacatepay"`, `"ABACATE_PAY"`, `"Abacate_Pay"` sao todos aceitos (`AbacatePay_ProviderCode_IsAcceptedCaseInsensitive`).

## Seguranca e secrets

### Regras obrigatorias implementadas

- **API key nunca logada**: `AbacatePayClient` NAO loga `Authorization` header, `apiKey`, request body, response body ou `brCodeBase64`. Apenas `path`, `category` e `statusCode` chegam a logs estruturados.
- **API key nunca persistida**: extraida uma unica vez do JSON desprotegido, em variavel local, descartada apos o `HttpClient.SendAsync`. Nao ha campo em `AbacatePayClient` nem em `AbacatePayProviderAdapter`.
- **API key nunca retornada**: `CreateCheckoutProviderResult.RawResponseJson` nao inclui apiKey, secret, nem marker do protector (`FakeCredentialProtector.Marker`).
- **Mensagem da exception segura**: `AbacatePayClientException.Message` NAO inclui API key, body, `brCodeBase64` ou response body. Apenas "AbacatePay error (Category)." ou "AbacatePay HTTP <code>." quando aplicavel.
- **Caminho "no credentials" falha cedo**: `CreateCheckoutAsync` retorna `Success=false` com mensagem segura **antes** de chamar o client quando `ProtectedCredentials` ausente. Evita stack trace vazando regra.
- **Simulacao opt-in**: `AllowDevModeSimulation=false` em production; client lanca `SimulationDisabled` antes de enviar request.
- **Caller cancellation propagation**: `CancellationToken` cancelado propaga como `OperationCanceledException` (nao envelopado em `AbacatePayClientException`), distinguindo operador de timeout do `HttpClient`.
- **`BaseAddress` path gotcha corrigido**: paths sem leading `/` (`transparents/create`) preservam o segmento `/v2/` do BaseAddress. Paths com leading `/` resolveriam do host root, ignorando `/v2/`. Decisao documentada em teste.

### Atestado por testes

- `AbacatePayClientTests`: assertions explicitas `ex.Message.Should().NotContain(ApiKey)`, `ex.Message.Should().NotContain("brCodeBase64")`, `ex.Message.Should().NotContain("boom")` (body literal). `result.RawResponseJson.Should().NotContain(ApiKey)` no adapter.
- `AbacatePayProviderAdapterTests`: `result.RawResponseJson.Should().NotContain(ApiKey)`, `.Should().NotContain(Secret)`, `.Should().NotContain(FakeCredentialProtector.Marker)`.

## Testes adicionados

### `AbacatePayClientTests` (40 testes)

1. `CreateTransparentPixAsync_ShouldSendBearerHeaderAndParseBrCode` — verifica `Authorization: Bearer <apiKey>` no request, parsing de envelope e roundtrip de `brCode`/`brCodeBase64`.
2. `CreateTransparentPixAsync_ShouldSerializeAmountInCentsAndMetadata` — JSON de request contem `"amount":9999` e metadata com tenantId/paymentId.
3-9. `CreateTransparentPixAsync_ShouldCategorizeHttpFailures` (Theory com 7 status codes): 400→BadRequest, 401/403→Unauthorized, 404→NotFound, 429→RateLimited transient, 500/502→ServerError transient. Mensagens nao vazam apiKey/body.
10. `CreateTransparentPixAsync_ShouldMapHttpRequestExceptionToNetwork` — `HttpRequestException` → `Network` transient.
11. `CreateTransparentPixAsync_ShouldMapTimeoutToTimeoutCategory` — `TaskCanceledException` por timeout → `Timeout` transient.
12. `CreateTransparentPixAsync_ShouldHonorCallerCancellation` — `CancellationToken` cancelado propaga `OperationCanceledException`, NAO envelopado.
13. `CreateTransparentPixAsync_ShouldMapEnvelopeFailure` — `success=false` em HTTP 2xx → `EnvelopeFailure`.
14. `CreateTransparentPixAsync_ShouldMapMalformedJsonToEnvelopeFailure` — body nao-JSON → `EnvelopeFailure`.
15. `CreateTransparentPixAsync_ShouldThrowBadRequestWhenApiKeyMissing` — apiKey vazio → `Unauthorized`, sem request HTTP.
16. `CheckTransparentPixAsync_ShouldSendGetWithIdQuery` — `GET /transparents/check?id=<id>`, header Bearer.
17-20. `CheckTransparentPixAsync_ShouldMapAllKnownStatuses` (Theory com 4 status: PENDING, PAID, EXPIRED, CANCELLED).
21. `CheckTransparentPixAsync_ShouldRejectEmptyId` — id vazio → `BadRequest`, sem request HTTP.
22. `SimulateTransparentPixPaymentAsync_ShouldBeDisabledByDefault` — flag false → `SimulationDisabled`, sem request HTTP.
23. `SimulateTransparentPixPaymentAsync_ShouldCallEndpointWhenEnabled` — flag true → POST com id query.

### `AbacatePayProviderAdapterTests` (17 testes)

1. `CreateCheckoutAsync_ShouldUnprotectCredentialsAndCallClient` — apiKey chega ao client apos unprotect.
2. `CreateCheckoutAsync_ShouldBuildPixRequestWithAmountInCentsAndMetadata` — payload com amount/description/metadata corretos.
3. `CreateCheckoutAsync_ShouldIncludeCustomerWhenNameOrEmailPresent` — customer block populado.
4. `CreateCheckoutAsync_ShouldOmitCustomerWhenNotProvided` — customer null quando ambos ausentes.
5. `CreateCheckoutAsync_ShouldReturnFailureWhenProtectedCredentialsMissing` — `Success=false`, sem request.
6. `CreateCheckoutAsync_ShouldReturnFailureWhenApiKeyMissingInCredentials` — JSON sem `apiKey` → `Failure` com mensagem "apiKey".
7. `CreateCheckoutAsync_ShouldReturnFailureWhenCredentialsNotValidJson` — JSON malformado → `Failure` com mensagem "JSON".
8. `CreateCheckoutAsync_ShouldSurfaceClientExceptionAsFailure` — `RateLimited` → `Failure` com `ErrorMessage` categorizado, sem leak.
9-13. `CreateCheckoutAsync_ShouldMapProviderStatusToCanonical` (Theory com 5 status) — verifica `RawResponseJson.status` reflete provider status bruto.
14. `CreateCheckoutAsync_ShouldProduceSyntheticPixCheckoutUrl` — `abacatepay://pix/<id>`.
15. `CreateCheckoutAsync_ShouldNotLeakApiKeyOrSecretInResult` — `RawResponseJson` nao contem apiKey, secret, marker.
16. `CreateCheckoutAsync_ShouldReturnFailureWhenProviderPaymentIdMissing` — id vazio → `Failure` controlado.
17. `ParseWebhookAsync_ShouldExtractProviderPaymentIdAndStatus` — parsing JSON basico (webhook completo fica em Slice 2-B).

### `PaymentStatusMapperTests` (4 metodos adicionados, cobrindo AbacatePay)

- `AbacatePay_MapsAllCanonicalStatuses` (Theory com 11 inline cases: pending/processing/paid/approved/expired/cancelled/canceled/refunded/redeemed/under_dispute/failed).
- `AbacatePay_ProviderCode_IsAcceptedCaseInsensitive` (Theory com 4 variacoes de capitalizacao).
- `AbacatePay_UnknownStatus_ShouldDefaultToPendingSafely`.
- `AbacatePay_EmptyStatus_ShouldDefaultToPending`.

### `FakeCredentialProtector`

- Helper in-memory com marker `fake-cred|` + base64 reversivel. NUNCA usado em codigo produtivo; apenas em testes de adapter. Mirror de `FakeWebhookSecretProtector`.

## Validações executadas

| Validacao | Comando | Resultado |
|-----------|---------|-----------|
| Build | `dotnet restore PaymentHub.slnx` | OK |
| Build | `dotnet build PaymentHub.slnx` | 0 erros / 0 warnings em 9 projetos |
| Tests (filter AbacatePay) | `dotnet test --filter "FullyQualifiedName~AbacatePay"` | 57 passed (40 client + 17 adapter) |
| Tests (filter Provider) | `dotnet test --filter "FullyQualifiedName~Provider"` | 72 passed |
| Tests (full suite) | `dotnet test PaymentHub.slnx` | **348 passed** (291 baseline + 57 novos), 0 warnings |
| Tests (integration) | `dotnet test tests/PaymentHub.IntegrationTests/PaymentHub.IntegrationTests.csproj` | 10 passed (Slice 1-IT baseline preservado) |
| Architecture | `scripts/agent-architecture-check.sh` | Architecture check passed |
| Docs | `scripts/agent-docs-check.sh` | Docs check passed |
| Diff hygiene | `git diff --check` | clean |

## Gaps remanescentes (deferred)

- **Slice 2-B — AbacatePay webhooks externos e normalizacao de eventos**: HMAC-SHA256 + timestamp + event normalization + tenant routing. O `ParseWebhookAsync` atual faz parsing JSON naive e NAO verifica assinatura. Depende de decisao de produto (HMAC obrigatorio ou opcional, retention do segredo).
- **Expor `brCode`/`brCodeBase64` na response publica de checkout**: hoje apenas em `RawResponseJson`. Micro-slice de API (Phase 1 ou 2) com ADR ou contrato explicito.
- **Integracao sandbox end-to-end com chave AbacatePay real**: este slice NAO executa chamada externa real; cobertura e via `ScriptedHandler` + `SingleHandlerHttpClientFactory`. Cobertura futura depende de `AllowDevModeSimulation=true` em CI com credenciais fake de sandbox, com politica explicita de retencao.
- **`AuditLog` em `AbacatePayProviderAdapter`**: P2-3 do `docs/roadmap/002-phase-status-board.md` (Acoes administrativas sensiveis sem AuditLog). Hoje apenas `RegisterProviderAccountHandler` registra.
- **Stripe e MercadoPago adapters reais**: continuam skeleton. Phase 4 (multi-provider) quando Prioridade permitir.
- **Idempotency-Key no client HTTP AbacatePay**: se a chamada 2xx cair entre o client e o handler (timeout + retry), o Payment Hub pode acabar criando 2 PIX na AbacatePay. Mitigacao depende do slice 3-IT (retry policy no `CreateCheckoutHandler`) ou de um client que envie `Idempotency-Key` no header e mapeie para o endpoint correspondente da AbacatePay.
- **`PaymentEventStatus` granular em `CreateCheckoutProviderResult`**: hoje a API retorna o `PaymentStatus.Pending` generico; com a possibilidade de `Processing` ou `RequiresAction` no futuro, podemos precisar expor o status canonico direto na response.

## Próximo slice recomendado

**Slice 2-B — AbacatePay webhooks externos e normalizacao de eventos** (Phase 2 + 3).

A fazer quando a equipe confirmar:

1. Validar HMAC do provider (signature header + timestamp).
2. Normalizar eventos para o modelo canonico (`payment.approved`, `payment.refunded`, etc.) ja coberto pelo Slice 7-A dispatcher.
3. Persistir em `WebhookEvent` (Inbox) antes de processar.
4. Cobrir com testes que nao dependam de rede real (similar a `AbacatePayClientTests`).

Dependencias: nenhuma alem do Slice 2-A (concluido). Pode correr em paralelo com slices de Phase 3 ou Phase 6.

## Arquivos relacionados

- `src/PaymentHub.Infrastructure.Providers/AbacatePay/AbacatePayProviderAdapter.cs`
- `src/PaymentHub.Infrastructure.Providers/AbacatePay/AbacatePayClient.cs`
- `src/PaymentHub.Infrastructure.Providers/AbacatePay/IAbacatePayClient.cs`
- `src/PaymentHub.Infrastructure.Providers/AbacatePay/AbacatePayOptions.cs`
- `src/PaymentHub.Infrastructure.Providers/AbacatePay/AbacatePayErrorCategory.cs`
- `src/PaymentHub.Infrastructure.Providers/AbacatePay/AbacatePayClientException.cs`
- `src/PaymentHub.Infrastructure.Providers/AbacatePay/Models/*`
- `src/PaymentHub.Infrastructure.Providers/ProvidersServiceCollectionExtensions.cs`
- `src/PaymentHub.Application/Abstractions/Providers/ProviderModels.cs`
- `src/PaymentHub.Application/Checkouts/CreateCheckoutHandler.cs`
- `src/PaymentHub.Domain/Services/PaymentStatusMapper.cs`
- `src/PaymentHub.Api/appsettings.json`
- `src/PaymentHub.Api/appsettings.Development.json`
- `tests/PaymentHub.UnitTests/Infrastructure/Providers/AbacatePay/AbacatePayClientTests.cs`
- `tests/PaymentHub.UnitTests/Infrastructure/Providers/AbacatePay/AbacatePayProviderAdapterTests.cs`
- `tests/PaymentHub.UnitTests/Support/FakeCredentialProtector.cs`
- `tests/PaymentHub.UnitTests/Domain/PaymentStatusMapperTests.cs`
- `docs/specs/008-provider-adapters.md` (atualizado)
- `docs/specs/004-payment-lifecycle.md` (atualizado)
- `docs/specs/011-security-and-compliance.md` (atualizado)
- `docs/roadmap/001-development-timeline.md` (atualizado)
- `docs/roadmap/002-phase-status-board.md` (atualizado)
- `docs/harness/learnings.md` (entrada nova)
- `docs/harness/validation-matrix.md` (atualizado)
- `agent-progress.md` (entrada movida para `## Historico`)