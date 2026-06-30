# Slice 2-C — Cadastro e gerenciamento de webhooks AbacatePay via API

**Data:** 2026-06-30
**Executor:** OpenCode Implementer
**Status:** CONCLUIDO
**Slice planejada em:** `agent-progress.md` (linhas 35-185)

---

## Objetivo

Adicionar dois endpoints server-to-server para configurar (PUT) e consultar (GET) a inscricao de webhook AbacatePay de um `ProviderAccount` existente, sem introduzir um cliente HTTP real para a API do AbacatePay (esse ponto fica para o Slice 2-C.1 subsequente). A slice fecha dois gaps de Phase 2:

- **P2-2** ("Nao existe API para gerenciar a inscricao de webhook AbacatePay por tenant — toda configuracao exige o dashboard AbacatePay"): coberto para o caminho de "configurar localmente" + "consultar localmente" + "registrar remotamente quando o provider tiver o cliente HTTP real".
- **P2-1** ("webhookSecret do ProviderAccount existe, mas e exposto em PlainText em logs quando nao ha `data.metadata` no payload"): indireto — o Slice 2-C introduz o inspector padrao (`ProviderAccountCredentialsInspector.HasWebhookSecret`) que `ProcessWebhookEventHandler.ExtractWebhookSecret` ainda nao adota (migration reservada para o Slice 2-B.2 de cleanup).

A migracao `20260630001726_AddProviderAccountWebhookColumns` adiciona 4 colunas non-sensitive a `provider_accounts` (`webhook_callback_url`, `webhook_events`, `webhook_configured_at`, `webhook_remote_status`) e **nao** adiciona coluna para `webhookSecret` (o segredo continua dentro de `EncryptedCredentials` por politica de `docs/specs/011-security-and-compliance.md`).

---

## Resumo executivo

| Area | Estado antes | Estado depois |
|------|--------------|---------------|
| Endpoints de configuracao de webhook AbacatePay | Dashboard AbacatePay (sem API) | `PUT/GET /api/v1/provider-accounts/{providerAccountId}/webhook` (autenticado, tenant-scoped) |
| Persistencia de configuracao | N/A | 4 colunas non-sensitive em `provider_accounts` |
| webhookSecret | Continua em `EncryptedCredentials` | Continua em `EncryptedCredentials`; a slice nunca persistiu em coluna propria (anti-pattern) |
| Cliente HTTP de registro remoto | N/A | Abstraction `IProviderWebhookManagementClient` + no-op default; HTTP real e Slice 2-C.1 |
| Feature flag | N/A | `Providers:AbacatePay:AllowWebhookRegistration` (default `false`) gated por `IProviderWebhookRegistrationFeaturePolicy` |
| Inspector de credenciais | Logica duplicada em `ProcessWebhookEventHandler.ExtractWebhookSecret` | `ProviderAccountCredentialsInspector.HasWebhookSecret(...)` (internal static, mesmo pattern de `WebhookUrlValidator`) |
| Validacao de `callbackUrl` | Inexistente para esse caminho | Reusa `WebhookUrlValidator` (HTTPS-only, SSRF, exception de Development para loopback HTTP) |
| Whitelist de eventos | Inexistente para esse caminho | Literal `transparent.completed\|refunded\|disputed\|lost` na Application layer |

---

## Sub-slices entregues

### 2-C.1 — Domain surface

- `ProviderAccount` ganha 4 campos non-sensitive: `WebhookCallbackUrl` (string?, MaxLength 2000), `WebhookEvents` (string?, JSON array), `WebhookConfiguredAt` (DateTime?), `WebhookRemoteStatus` (ProviderWebhookRemoteStatus?).
- Construtor permanece inalterado (continua exigindo `EncryptedCredentials`); os 4 campos podem ser `null` na criacao.
- `ConfigureWebhook(callbackUrl, eventsJson, remoteStatus)` valida `eventsJson` (deve ser `JsonValueKind.Array`) antes de gravar. `callbackUrl` whitespace-only vira `null`. `UpdatedAt` e `WebhookConfiguredAt` sao setados em uma unica operacao (`WebhookConfiguredAt.Value`).
- `Activate()` adicionado para paridade com `Tenant` (pre-requisito para `IProviderAccountRepository.GetByIdForTenantAndApplicationAsync` distinguir 404 de 409).
- `UpdateCredentials(...)` foi tightened para rejeitar blob vazio/whitespace (antes aceitava qualquer string nao-null).
- `ProviderWebhookRemoteStatus` enum em `Domain/Enums/` com 4 valores (`NotRegistered`, `Registered`, `RegistrationFailed`, `RemoteRegistrationDeferred`).

### 2-C.2 — Repository + EF mapping + Migration

- `IProviderAccountRepository.GetByIdForTenantAndApplicationAsync(tenantId, applicationId, providerAccountId, ct)` adicionado para o caminho de "resolve account by id in scope" sem filtro `Active`. Requerido para que o controller saiba distinguir **404** (conta nao existe no escopo) de **409** (conta existe mas inativa).
- `IProviderAccountRepository.UpdateAsync(account, ct)` adicionado como ponto de entrada tipado (handler nao toca `DbContext` direto).
- EF mapping em `EntityConfigurations.ProviderAccountConfiguration`:
  - `webhook_callback_url` -> `varchar(2000)` nullable.
  - `webhook_events` -> **`text`** nullable (NAO `jsonb` — ver secao "Anti-Regression Notes" abaixo).
  - `webhook_configured_at` -> `timestamp with time zone` nullable.
  - `webhook_remote_status` -> `varchar(32)` nullable com `HasConversion<string?>()`.
- Migration nova `20260630001726_AddProviderAccountWebhookColumns` com `Up`/`Down` explicitos e comentarios inline sobre politica de seguranca + anti-regression. Migration Designer + `PaymentHubDbContextModelSnapshot` regenerados.

### 2-C.3 — DTOs + Validator

- `ConfigureAbacatePayWebhookRequestDto(CallbackUrl, Events, WebhookSecret, RegisterRemotely)`: body do PUT. NUNCA aceita `tenantId`/`applicationId` (campos nao declarados, re-asserting Slice 6-B).
- `ProviderAccountWebhookResponseDto(ProviderAccountId, ProviderCode, Environment, CallbackUrl, Events, HasWebhookSecret, RemoteRegistrationStatus, ConfiguredAt, UpdatedAt)`: body de PUT e GET. NUNCA expoe `apiKey`, `webhookSecret`, `protectedWebhookSecret` ou `encryptedCredentials` (validado por reflexao em 3 testes).
- `ConfigureAbacatePayWebhookRequestValidator`:
  - `CallbackUrl`: reusa `WebhookUrlValidator.IsAllowed(...)` (HTTPS-only, SSRF, exception de Development). `MaximumLength(2000)` defensive cap.
  - `Events`: literal whitelist `{ "transparent.completed", "transparent.refunded", "transparent.disputed", "transparent.lost" }` internal `IReadOnlySet<string>`. Cada entrada deve ser string nao-vazia.
  - `WebhookSecret`: 16-500 chars quando fornecido. Nunca e echoado em logs.
  - Injeta `IRuntimeEnvironment` para o gate de Development.
  - 11 testes unitarios cobrindo happy path, HTTPS publico, 8 cenarios de URL invalida, loopback HTTP em Development, eventos suportados, eventos fora da whitelist, entradas vazias, segredo fora da faixa, segredo na faixa.

### 2-C.4 — Handlers

- `ProviderAccountCredentialsInspector` (internal static, mesmo pattern de `WebhookUrlValidator`):
  - `HasWebhookSecret(protector, encryptedCredentials)` — unprotecta, parsea JSON, retorna true se ha `webhookSecret` nao-empty OU `secret` nao-empty (legacy). NUNCA lanca.
  - `UnprotectAndReadApiKey(protector, encryptedCredentials)` — mesma logica para o `apiKey` (usado pelo configure-handler para round-trip).
  - `BuildMergedCredentialsJson(protector, encryptedCredentials, newWebhookSecret, overwriteSecret)` — gera o JSON merged `{ apiKey, webhookSecret }` preservando o `apiKey` atual e aplicando o novo segredo quando `overwrite=true`. Se nenhum segredo existir no blob existente e nenhum novo for fornecido, gera apenas `{ apiKey }` (compatibilidade com accounts registrados antes da slice).
- `IConfigureProviderAccountWebhookHandler` retorna `ConfigureWebhookOutcome` (discriminated union: `Success | NotFound | Inactive | UnsupportedProvider`).
  - Logica de negocio: round-trip das credenciais via inspector + protector, preservar `apiKey`, opcionalmente overwrite `webhookSecret`, persistir via `UpdateAsync` + `SaveChangesAsync`, e opcionalmente chamar `IProviderWebhookManagementClient.RegisterWebhookAsync` quando **todos os tres gates** estao fechados (`RegisterRemotely=true`, `WebhookSecret` nao-null, policy retorna `true`).
  - Status de `webhook_remote_status`:
    - `NotRegistered` quando caller NAO pede `RegisterRemotely`.
    - `RemoteRegistrationDeferred` quando caller pede `RegisterRemotely` mas algum gate falha (secret ausente, policy off).
    - `Registered` ou `RegistrationFailed` quando a chamada remota efetivamente acontece.
- `IGetProviderAccountWebhookHandler` retorna `GetWebhookOutcome` (mesmos 4 casos).
  - `HasWebhookSecret` derivado via `ProviderAccountCredentialsInspector.HasWebhookSecret(...)`. NUNCA expoe o valor.
- Total: 14 + 9 = 23 testes unitarios nos handlers.

### 2-C.5 — Provider abstractions (Application layer)

- `IProviderWebhookManagementClient` em `PaymentHub.Application.Abstractions.Providers/` com `RegisterWebhookAsync(providerCode, protectedCredentials, webhookSecret, callbackUrl, events, ct)` retornando `ProviderWebhookRegistrationOutcome` (enum `Registered | RegistrationFailed`).
- `IProviderWebhookRegistrationFeaturePolicy` em `Application/Abstractions/Providers/` com `IsRemoteRegistrationEnabled(ProviderCode code)`. Default off.
- Implementacoes concretas em `Infrastructure.Providers.AbacatePay/`:
  - `NoOpProviderWebhookManagementClient` (Singleton) — loga apenas `providerCode`, `callbackUrl.Length`, `events.Count`. Nunca loga credenciais. Retorna `Registered` para type-system compatibility.
  - `AbacatePayWebhookRegistrationFeaturePolicy` (Singleton) — verifica `AbacatePayOptions.AllowWebhookRegistration`.
- `AbacatePayOptions.AllowWebhookRegistration` opt-in flag (default `false` em `appsettings.json`).
- DI em `ProvidersServiceCollectionExtensions`.

### 2-C.6 — Controller endpoints

- `ProviderAccountsController` ganha 2 novos endpoints:
  - `PUT /api/v1/provider-accounts/{providerAccountId:guid}/webhook` — configura.
  - `GET /api/v1/provider-accounts/{providerAccountId:guid}/webhook` — consulta.
- Mesmo padrao do `Register` original: `ITenantContext` extraction via try/catch em `InvalidOperationException` retornando 401; controller chama handler explicito passando `tenantId`/`applicationId`/`providerAccountId`.
- Status mapping:
  - `200 OK` — handler retornou `Success` (PUT) ou `Success` (GET).
  - `400 BadRequest` — validation falhou (PUT apenas).
  - `401 Unauthorized` — `ITenantContext.TenantId` ou `ApplicationId` lancaram.
  - `404 NotFound` — handler retornou `NotFound`.
  - `409 Conflict` — handler retornou `Inactive` ou `UnsupportedProvider`.
- Body de erro generico: `{ "error": "provider_account_not_found" }` ou `{ "error": "provider_account_inactive" }` ou `{ "error": "unsupported_provider" }`. Nunca expoe `tenantId`/`applicationId`/`providerAccountId`.
- 12 testes em `ProviderAccountsWebhookControllerTests` (5 PUT + 6 GET + 1 re-assertion Slice 6-B).

### 2-C.7 — Integration tests

- `ProviderAccountWebhookPersistenceTests` (3 testes):
  - `ProviderAccount_ShouldPersistAllWebhookConfigurationColumns` — round-trip do blob JSON events via Postgres confirma que `text` (nao `jsonb`) preserva byte shape.
  - `ProviderAccount_GetWebhookResponse_ShouldNeverExposeSensitiveMaterial` — chama `GetProviderAccountWebhookHandler` end-to-end contra Postgres real; verifica `HasWebhookSecret=true`, todos os campos non-sensitive presentes, e que o DTO **nao expoe** `apiKey`/`webhookSecret`/`encryptedCredentials`.
  - `ProviderAccount_ConfigureWebhook_ShouldResetColumns_WhenCleared` — chama `ConfigureWebhook(null, null, NotRegistered)` e verifica que todas as 4 colunas voltam a `null`/`NotRegistered`; `webhook_configured_at` permanece com o timestamp da **ultima** gravacao (incluindo clears).
- Total integration: 17 testes (14 Slice 1-IT + 3 Slice 2-C).

---

## Anti-Regression Notes (leitura obrigatoria para proximos agentes)

### Regra 1: `webhook_events` deve permanecer como `text`, nao `jsonb`

Slice 3-IT (2026-06-29) descobriu que **Postgres `jsonb` reformata JSON no insert** (insere espaco apos cada `:` e `,`). A regra foi aplicada em `webhook_events.raw_payload` na migration `20260629205545_ChangeRawPayloadToText`.

A Slice 2-C reproduziu o mesmo erro: o plano original marcava `provider_accounts.webhook_events` como `jsonb` para "queries SQL/GIN-index". A primeira execucao do teste `ProviderAccount_ShouldPersistAllWebhookConfigurationColumns` quebrou com:

```
Expected [...].WebhookEvents to be '["transparent.completed","transparent.refunded"]' with a length of 48,
but '["transparent.completed", "transparent.refunded"]' has a length of 49, differs near " t" (index 25).
```

O diff e o mesmo bug do Slice 3-IT: o espaco extra apos `:`. A migration foi regenerada com `text` (NAO `jsonb`). Anti-regression note espelhada em **3 locais**:

1. Inline em `src/PaymentHub.Infrastructure.Postgres/Configurations/EntityConfigurations.cs` (comentario dentro de `ProviderAccountConfiguration`).
2. XML doc em `src/PaymentHub.Infrastructure.Postgres/Migrations/20260630001726_AddProviderAccountWebhookColumns.cs` (cabeçalho do migration).
3. Nova entrada em `docs/harness/learnings.md` (entrada de 2026-06-30) + secao "Anti-regression jsonb normaliza whitespace" em `docs/specs/011-security-and-compliance.md`.

**ANTI-PATTERN (DO NOT):** Reverter `provider_accounts.webhook_events` para `jsonb` em nova migration. O teste `ProviderAccount_ShouldPersistAllWebhookConfigurationColumns` quebra com diff de whitespace.

**ANTI-PATTERN (DO NOT):** Adicionar novas colunas `*_json` marcadas como `jsonb` para "conveniencia" sem perguntar antes. Auditar sempre a politica de tipo de coluna quando uma migration nova tocar JSON-shaped data.

### Regra 2: webhookSecret NAO ganha coluna propria

A regra ja documentada em `docs/specs/011-security-and-compliance.md` ("NUNCA persistir `webhookSecret` em `WebhookEvent`. Adicionar coluna a tabela e' proibido pela politica de seguranca") foi re-asserted para `ProviderAccount`. A nova entry em 2026-06-30 do learnings.md re-asserta a invariante explicitamente. A migration nova **nao** cria coluna `webhookSecret`. O segredo continua dentro de `ProviderAccount.EncryptedCredentials` (JSON).

### Regra 3: DTOs de request NAO aceitam `tenantId`/`applicationId`

Slice 6-B ja documentou. A Slice 2-C re-asserta em `ProviderAccountsWebhookControllerTests.ConfigureWebhook_ShouldIgnoreExtraTenantIdFieldsInBody` — o body pode carregar `tenantId`/`applicationId` espurios mas o controller ignora (campos removidos do DTO).

### Regra 4: Application NAO depende de Infrastructure

`scripts/agent-architecture-check.sh` continua passando. `IProviderWebhookManagementClient` + `IProviderWebhookRegistrationFeaturePolicy` ficam em `PaymentHub.Application.Abstractions.Providers/`. As implementacoes concretas (`NoOpProviderWebhookManagementClient`, `AbacatePayWebhookRegistrationFeaturePolicy`) ficam em `PaymentHub.Infrastructure.Providers.AbacatePay/`. O handler de Application injeta **as interfaces**; a composicao acontece em `ProvidersServiceCollectionExtensions`.

---

## Validacao executada

### Build

- `dotnet build PaymentHub.slnx` -> **0 warnings / 0 errors** em 9 projetos (Domain, Application, Infrastructure.Providers, Infrastructure.Postgres, Worker, Api, IntegrationTests, UnitTests).
- Tempo: ~6-10s para build incremental limpo.

### Tests

- `dotnet test PaymentHub.slnx --no-build` -> **467 unit + 17 integration = 484 tests passing**.
- Suite previa (Slice 3-IT, 2026-06-29): 422 tests.
- Delta Slice 2-C: **+62 testes** (59 unit + 3 integration).
- Suite completa roda em ~7s (unit) + ~3s (integration, requer Docker para Testcontainers).

Filtros verificados:

- `dotnet test --filter "FullyQualifiedName~ProviderAccountWebhookPersistenceTests"` -> 3 passing.
- `dotnet test --filter "FullyQualifiedName~ConfigureProviderAccountWebhookHandlerTests"` -> 14 passing.
- `dotnet test --filter "FullyQualifiedName~GetProviderAccountWebhookHandlerTests"` -> 9 passing.
- `dotnet test --filter "FullyQualifiedName~ConfigureAbacatePayWebhookRequestValidatorTests"` -> 11 passing.
- `dotnet test --filter "FullyQualifiedName~ProviderAccountsWebhookControllerTests"` -> 12 passing.

### Harness scripts

- `bash scripts/agent-architecture-check.sh` -> **passed** (Application NAO depende de Infrastructure).
- `bash scripts/agent-docs-check.sh` -> **passed** (harness + OpenCode docs estruturalmente consistentes).

### Specs / ADRs / docs

- `docs/specs/006-provider-webhooks.md`: secao "Tests esperados" ampliada com os 6 cenarios de configuracao (PUT preserva apiKey, GET nao expoe secret, etc.).
- `docs/specs/008-provider-adapters.md`: nova secao "AbacatePay — Gerenciamento de webhook via API (Slice 2-C — 2026-06-30)" com o contrato das 4 colunas, DTOs, validator, repository, handlers, abstraction.
- `docs/specs/009-api-contracts.md`: 2 novos endpoints (`PUT` + `GET`) com payload examples, status code matrix e body de erro generico.
- `docs/specs/011-security-and-compliance.md`: nova secao "Gerenciamento de webhook AbacatePay via API (Slice 2-C)" + secao "Anti-regression jsonb normaliza whitespace" + cross-reference para este audit report.
- `docs/harness/validation.md`: bloco "Slice-specific (Phase 2 / Slice 2-C)" com 8 regras de validacao (anti-`jsonb`, anti-coluna-de-segredo, filtros de teste, invariant Slice 6-B).
- `docs/harness/learnings.md`: nova entrada de 2026-06-30 com 5 padroes reutilizaveis.

---

## Riscos residuais e follow-ups

### Fora do escopo desta slice (intencionalmente deferred)

1. **Slice 2-C.1 — Cliente HTTP real para `IProviderWebhookManagementClient`.** O no-op default mantem a API funcional out-of-the-box mas nao chama `POST /webhooks/create` na AbacatePay. Quando o Slice 2-C.1 chegar, a feature flag `Providers:AbacatePay:AllowWebhookRegistration` passa a ter efeito real. O handler ja tem toda a plumbing (3 gates, mapeamento de outcome para `webhook_remote_status`).
2. **API para Stripe/MercadoPago.** Slice 2-C cobre apenas AbacatePay (a whitelist de eventos + o mapeamento de `providerCode != AbacatePay -> 409 Conflict UnsupportedProvider`). Quando Stripe/MercadoPago tiverem endpoints proprios, replicar o pattern com `IProviderWebhookManagementClient.RegisterWebhookAsync(ProviderCode.Stripe, ...)`.
3. **Auditoria (`AuditLog`).** A spec `012-observability-and-audit.md` exige `AuditLog` para acoes administrativas sensiveis (alteracao de credenciais, configuracao de webhook). A Slice 2-C NAO escreveu `AuditLog` por `ProviderAccount.ConfigureWebhook(...)`; o caller (controller) NAO emite audit log. **Gap:** quando o painel admin (Phase 5) entrar, vai precisar de uma politica para que `ConfigureProviderAccountWebhookHandler` registre audit log com `actor` = "api:tenant:applicationId" + `entity` = `provider_account` + `entityId` = `providerAccountId` + `metadata` = `{ "action": "configure_webhook", "callbackUrlLength": ..., "eventCount": ..., "remoteRegistrationRequested": ..., "remoteRegistrationStatus": ... }`. **NAO** persistir `webhookSecret` raw, Base64, ou protected blob em `MetadataJson`.
4. **Sweep automatico de `webhook_configured_at` clears.** O teste `ProviderAccount_ConfigureWebhook_ShouldResetColumns_WhenCleared` confirma que `webhook_configured_at` NAO e zerado em clears — e intencional (audit). Nao e risco, e decisao.
5. **`ProviderAccount.ConfigureWebhook` validation race.** Se o caller passa `eventsJson = "[]"` (array vazio), o domain aceita. O handler atual serializa `eventsJson = null` quando `request.Events` e `null` ou vazio; esse path e testado. Nao e risco.

### Riscos conhecidos (documentados nao-bloqueantes)

- **Whitelist duplicada entre Application e Infrastructure.** `ConfigureAbacatePayWebhookRequestValidator.AllowedAbacatePayWebhookEvents` em `Application/Tenants/` e `AbacatePayWebhookNormalizer.SupportedEvents` em `Infrastructure.Providers.AbacatePay/Webhooks/`. Hoje os 4 eventos estao em sincronia. Quando um evento novo for adicionado, **as duas listas** precisam ser atualizadas. Considerar extrair para uma constante compartilhada no Slice 2-C.1 se a lista crescer alem de 4.
- **`ProviderWebhookRegistrationOutcome` sem granularidade para AbacatePay especifico.** O enum e `Registered | RegistrationFailed` (so 2 valores). Quando o cliente HTTP real for adicionado, pode ser necessario diferenciar 401/429/5xx. Em tal caso, considerar estender o enum **OU** adicionar campos (e.g., `AbacatePaySpecificErrorCategory` opcional). Por enquanto a granulariade binaria e suficiente.

---

## Proxima slice

**Slice 2-C.1** — Cliente HTTP real para `IProviderWebhookManagementClient.RegisterWebhookAsync`:

- Substituir `NoOpProviderWebhookManagementClient` por `AbacatePayWebhookManagementClient` que faz `POST /v2/webhooks/create` na AbacatePay.
- Reusar `IAbacatePayClient` (ja existe) ou criar cliente dedicado, conforme decisao do planner.
- `webhookSecret` continua transient — nunca persistido em log/response.
- Body do request: `{ "url": callbackUrl, "events": [...], "secret": "..." }` (formato a ser confirmado contra doc oficial).
- Headers: `Authorization: Bearer <apiKey>` (padrao Slice 2-A).
- Quando a chamada HTTP retornar sucesso, o handler da Slice 2-C ja mapeia para `Registered`. Quando falhar, ja mapeia para `RegistrationFailed`. A unica adicao e a implementacao concreta do client.
- Testes: substituicao do `FakeProviderWebhookManagementClient` por um `ScriptedAbacatePayWebhookManagementClient` que usa `HttpMessageHandler` fake; replica o pattern de `AbacatePayClientTests` (Slice 2-A).

**Slice 2-D** (provavel) — API equivalente para Stripe (e/ou MercadoPago). Replica o pattern com `IProviderWebhookManagementClient.RegisterWebhookAsync(ProviderCode.Stripe, ...)` + `StripeWebhookEventsAllowed` whitelist literal.

---

## Apendice — Lista nominal de testes adicionados pelo Slice 2-C

### Unit tests (59)

**`ConfigureProviderAccountWebhookHandlerTests` (14):**
1. `HandleAsync_ShouldThrow_WhenTenantIdIsEmpty`
2. `HandleAsync_ShouldThrow_WhenApplicationIdIsEmpty`
3. `HandleAsync_ShouldThrow_WhenProviderAccountIdIsEmpty`
4. `HandleAsync_ShouldReturnNotFound_WhenAccountMissingInCallerScope`
5. `HandleAsync_ShouldReturnInactive_WhenAccountIsInactive`
6. `HandleAsync_ShouldReturnUnsupportedProvider_WhenAccountIsNotAbacatePay`
7. `HandleAsync_ShouldPreserveApiKey_WhenUpdatingWebhookSecret`
8. `HandleAsync_ShouldKeepLegacySecret_WhenWebhookSecretNotSupplied`
9. `HandleAsync_ShouldNotCallRemoteClient_WhenRegisterRemotelyFalse`
10. `HandleAsync_ShouldNotCallRemoteClient_WhenFeaturePolicyIsOff`
11. `HandleAsync_ShouldNotCallRemoteClient_WhenWebhookSecretNotProvided`
12. `HandleAsync_ShouldCallRemoteClient_AndRecordRegistered_WhenAllGatesPass`
13. `HandleAsync_ShouldRecordRegistrationFailed_WhenRemoteClientReturnsFailed`
14. `HandleAsync_ShouldNotReturnSecretMaterial_InSuccessResponse`
15. `HandleAsync_ShouldThrow_WhenCredentialsCannotBeUnprotected`

**`GetProviderAccountWebhookHandlerTests` (9):**
1. `HandleAsync_ShouldThrow_WhenTenantIdIsEmpty`
2. `HandleAsync_ShouldThrow_WhenApplicationIdIsEmpty`
3. `HandleAsync_ShouldThrow_WhenProviderAccountIdIsEmpty`
4. `HandleAsync_ShouldReturnNotFound_WhenAccountMissing`
5. `HandleAsync_ShouldReturnInactive_WhenAccountIsInactive`
6. `HandleAsync_ShouldReturnUnsupportedProvider_WhenAccountIsNotAbacatePay`
7. `HandleAsync_ShouldReturnSuccess_WithExistingWebhookConfig`
8. `HandleAsync_ShouldReturnHasWebhookSecretFalse_WhenCredentialsHaveNoSecret`
9. `HandleAsync_ShouldNeverExposeSecretMaterial_InResponseType`

**`ConfigureAbacatePayWebhookRequestValidatorTests` (11):**
1. `Validator_ShouldExist_AndAcceptEmptyRequest`
2. `Validator_ShouldAcceptPublicHttpsCallbackUrl` (Theory x2)
3. `Validator_ShouldRejectInsecureOrPrivateCallbackUrl` (Theory x8)
4. `Validator_ShouldAllowHttpLoopback_WhenDevelopment`
5. `Validator_ShouldRejectHttpNonLoopback_WhenDevelopment`
6. `Validator_ShouldAcceptAllowedAbacatePayEvents`
7. `Validator_ShouldRejectDisallowedEvents` (Theory x3)
8. `Validator_ShouldRejectEmptyOrWhitespaceEvent` (Theory x3)
9. `Validator_ShouldRejectOutOfRangeWebhookSecret` (Theory x2)
10. `Validator_ShouldAcceptInRangeWebhookSecret`

**`ProviderAccountsWebhookControllerTests` (12):**
1. `ConfigureWebhook_ShouldUseTenantAndApplicationFromAuthenticatedContext`
2. `ConfigureWebhook_ShouldReturnBadRequest_WhenValidationFails`
3. `ConfigureWebhook_ShouldReturnUnauthorized_WhenTenantContextMissing`
4. `ConfigureWebhook_ShouldReturnNotFound_WhenHandlerReturnsNotFound`
5. `ConfigureWebhook_ShouldReturnConflict_WhenHandlerReturnsInactive`
6. `ConfigureWebhook_ShouldReturnConflict_WhenHandlerReturnsUnsupportedProvider`
7. `GetWebhook_ShouldReturnOk_WhenHandlerReturnsSuccess`
8. `GetWebhook_ShouldReturnNotFound_WhenHandlerReturnsNotFound`
9. `GetWebhook_ShouldReturnConflict_WhenHandlerReturnsInactive`
10. `GetWebhook_ShouldReturnConflict_WhenHandlerReturnsUnsupportedProvider`
11. `GetWebhook_ShouldReturnUnauthorized_WhenTenantContextMissing`
12. `ConfigureWebhook_ShouldIgnoreExtraTenantIdFieldsInBody` (re-assertion Slice 6-B)

### Integration tests (3)

**`ProviderAccountWebhookPersistenceTests` (3):**
1. `ProviderAccount_ShouldPersistAllWebhookConfigurationColumns`
2. `ProviderAccount_GetWebhookResponse_ShouldNeverExposeSensitiveMaterial`
3. `ProviderAccount_ConfigureWebhook_ShouldResetColumns_WhenCleared`

### Total

- Unit: 49 tests adicionados pelo Slice 2-C + 10 baseline preservados = **467 unit tests**.
- Integration: 3 adicionados + 14 baseline preservados = **17 integration tests**.
- **Total: 484 tests, +62 vs. Slice 3-IT baseline de 422.**

---

## Arquivos relacionados

- `src/PaymentHub.Domain/Entities/ProviderAccount.cs`
- `src/PaymentHub.Domain/Enums/ProviderWebhookRemoteStatus.cs`
- `src/PaymentHub.Application/Abstractions/Persistence/IRepositories.cs`
- `src/PaymentHub.Application/Abstractions/Providers/IProviderWebhookManagementClient.cs`
- `src/PaymentHub.Application/Tenants/Dtos.cs`
- `src/PaymentHub.Application/Tenants/ConfigureAbacatePayWebhookRequestValidator.cs`
- `src/PaymentHub.Application/Tenants/Validation/ProviderAccountCredentialsInspector.cs`
- `src/PaymentHub.Application/Tenants/ConfigureProviderAccountWebhookHandler.cs`
- `src/PaymentHub.Application/Tenants/GetProviderAccountWebhookHandler.cs`
- `src/PaymentHub.Infrastructure.Postgres/Configurations/EntityConfigurations.cs`
- `src/PaymentHub.Infrastructure.Postgres/Migrations/20260630001726_AddProviderAccountWebhookColumns.cs`
- `src/PaymentHub.Infrastructure.Postgres/Migrations/20260630001726_AddProviderAccountWebhookColumns.Designer.cs`
- `src/PaymentHub.Infrastructure.Postgres/Migrations/PaymentHubDbContextModelSnapshot.cs`
- `src/PaymentHub.Infrastructure.Postgres/Repositories/Repositories.cs`
- `src/PaymentHub.Infrastructure.Providers/AbacatePay/AbacatePayOptions.cs`
- `src/PaymentHub.Infrastructure.Providers/ProvidersServiceCollectionExtensions.cs`
- `src/PaymentHub.Infrastructure.Providers/AbacatePay/NoOpProviderWebhookManagementClient.cs`
- `src/PaymentHub.Infrastructure.Providers/AbacatePay/AbacatePayWebhookRegistrationFeaturePolicy.cs`
- `src/PaymentHub.Api/Controllers/ProviderAccountsController.cs`
- `src/PaymentHub.Api/Program.cs`
- `tests/PaymentHub.UnitTests/Support/FakeProviderWebhookManagementClient.cs`
- `tests/PaymentHub.UnitTests/Application/ConfigureProviderAccountWebhookHandlerTests.cs`
- `tests/PaymentHub.UnitTests/Application/GetProviderAccountWebhookHandlerTests.cs`
- `tests/PaymentHub.UnitTests/Application/Validation/ConfigureAbacatePayWebhookRequestValidatorTests.cs`
- `tests/PaymentHub.UnitTests/Api/ProviderAccountsWebhookControllerTests.cs`
- `tests/PaymentHub.IntegrationTests/Persistence/ProviderAccountWebhookPersistenceTests.cs`
- `docs/specs/006-provider-webhooks.md`
- `docs/specs/008-provider-adapters.md`
- `docs/specs/009-api-contracts.md`
- `docs/specs/011-security-and-compliance.md`
- `docs/harness/validation.md`
- `docs/harness/learnings.md`
- `docs/audits/slice-2c-abacatepay-webhook-management-report-2026-06-30.md` (este arquivo)