# Slice 7-A — Real Outbox Dispatcher Report

Data: 2026-06-26
Phase: 7 — Workers, Outbox e processamento assincrono (Bloco A — Seguranca e Confiabilidade)
Specs relacionadas: `docs/specs/007-inbox-outbox-workers.md`, `docs/specs/011-security-and-compliance.md`
ADRs criadas: `docs/adr/ADR-0007-webhook-secret-protection.md`, `docs/adr/ADR-0010-real-outbox-dispatcher-location.md`
Gap enderecado: **P1-4** da auditoria de 2026-06-17.

## Resumo

O `PaymentHub.Worker` registrava um `internal sealed class NoopApplicationWebhookDispatcher` que marcava eventos de Outbox como `Sent` sem envio HTTP real. O Slice 7-A substituiu esse dispatcher por um dispatcher HTTP real (`HttpApplicationWebhookDispatcher`), realocado para `src/PaymentHub.Infrastructure.Postgres/Webhooks/`, com tenant guard, `LastError` seguro por categoria enum, validacao HTTPS/SSRF no `WebhookUrl` (antes da persistencia) e fail-fast da chave `PaymentHub:WebhookSecretEncryptionKey` no startup do Worker.

O slice foi dividido em 9 sub-slices (7-A.1 a 7-A.9). Sub-slices 7-A.1/.2/.3/.4/.5/.6/.7/.8 foram implementados, validados e commitados entre 2026-06-25 e 2026-06-26. Sub-slice 7-A.9 consolidou a documentacao, ADRs, roadmap, feature_list, learnings e este relatorio.

Suite previa: 178 testes. Suite nova: **281 testes** (+103). ADRs `ADR-0007-webhook-secret-protection.md` e `ADR-0010-real-outbox-dispatcher-location.md` consolidadas. Specs `007-inbox-outbox-workers.md` e `011-security-and-compliance.md` atualizadas. Phase 7 alcancou 0 gaps P1 proprios.

## Objetivo do slice

Eliminar o gap P1-4 (Worker usa `NoopApplicationWebhookDispatcher` no-op) de forma segura e testavel, garantindo que:

1. O Worker entregue HTTP requests reais para o `WebhookUrl` da `ApplicationClient`.
2. Cross-tenant access seja impossivel via tenant guard no dispatcher.
3. `OutboxEvent.LastError` nao persista `ex.Message`, body HTTP, query strings ou secrets.
4. `WebhookUrl` seja validada por HTTPS + protecao SSRF antes de qualquer persistencia.
5. O Worker falhe cedo no startup se a chave `PaymentHub:WebhookSecretEncryptionKey` estiver ausente.
6. Worker e API compartilhem o mesmo dispatcher e a mesma chave, sem depender de `PaymentHub.Api`.
7. Testes deterministicos do Worker sejam possiveis sem `DbContext` direto.

## Sub-slices concluidos

| Sub-slice | Data | Descricao |
|-----------|------|-----------|
| **7-A.1 Foundation** | 2026-06-25 | `Microsoft.Extensions.Http 10.0.0` adicionado a `Infrastructure.Postgres.csproj`. `AddPaymentHubPostgres` registra `HttpClient("application-webhook")` + `IApplicationWebhookDispatcher` (Scoped). `IOutboxEventStore` em Application + `EfOutboxEventStore` em Infrastructure.Postgres. |
| **7-A.2 Realocacao do dispatcher** | 2026-06-25 | `HttpApplicationWebhookDispatcher` movido de `PaymentHub.Api/Webhooks/` para `PaymentHub.Infrastructure.Postgres/Webhooks/`. Tenant guard via `_apps.GetByTenantAndIdAsync(tenantId, applicationId, ct)`. `LastError` sem body truncado. |
| **7-A.3 DI cleanup + Noop removido + fail-fast** | 2026-06-25 | `Program.cs` da API remove registros duplicados. `Program.cs` do Worker remove `NoopApplicationWebhookDispatcher` (classe + registro). Fail-fast de `IWebhookSecretProtector` antes de `host.Run()`. |
| **7-A.4 Worker testavel** | 2026-06-25 | `OutboxDispatcherWorker` injeta `IOutboxRepository`, `IOutboxEventStore` e `IClock`. `DateTime.UtcNow` removido em favor de `_clock.UtcNow`. |
| **7-A.5 HTTPS/SSRF no `WebhookUrl`** | 2026-06-26 | Helper puro `internal static class WebhookUrlValidator` em `Application/Tenants/Validation/`. `RegisterApplicationClientValidator` recebe `IRuntimeEnvironment` e adiciona regra `Must(...)`. 80+ testes novos. |
| **7-A.6 Worker appsettings** | 2026-06-26 | `Worker/appsettings.json` ganha `PaymentHub: { WebhookSecretEncryptionKey: "" }` (placeholder). `appsettings.Development.json` mantem valor fake. |
| **7-A.7 `LastError` seguro** | 2026-06-26 | `OutboxEvent` ganha `MarkRetryWithStatus(WebhookDispatcherCategory, int, DateTime)` e `MarkFailedWithStatus(WebhookDispatcherCategory, int)`. 7 categorias enum: `HttpFailure`, `NetworkError`, `Timeout`, `UnprotectFailure`, `MissingWebhookUrl`, `MissingWebhookSecret`, `UnexpectedDispatcherError`. `WebHookDispatcherException` carrega categoria + statusCode. |
| **7-A.8 Testes fortes** | 2026-06-26 | 3 testes de dispatcher movidos + >=10 testes novos (dispatcher tenant guard, no-body-log, no-signature-sem-secret, reprocess-estavel; worker com `IOutboxEventStore` + `IClock`). Helper `ScriptedHandler.cs`. |
| **7-A.9 Documentacao final** | 2026-06-26 | ADRs `ADR-0007` e `ADR-0010`. Specs 007 e 011 atualizadas. Roadmap 000/001/002 atualizados (P1-4 resolvido). `feature_list.md` com `PH-WORKER-001` e `PH-SEC-001` -> `Concluido`. Learnings. Validation matrix. Este relatorio. |

## Arquivos principais alterados

### Criados (sub-slices 7-A.1 a 7-A.8 + 7-A.9)

- `src/PaymentHub.Infrastructure.Postgres/Webhooks/HttpApplicationWebhookDispatcher.cs` (realocado em 7-A.2).
- `src/PaymentHub.Application/Abstractions/Outbox/IOutboxEventStore.cs` (7-A.1).
- `src/PaymentHub.Infrastructure.Postgres/Outbox/EfOutboxEventStore.cs` (7-A.1).
- `src/PaymentHub.Application/Tenants/Validation/WebhookUrlValidator.cs` (7-A.5).
- `src/PaymentHub.Domain/Enums/WebhookDispatcherCategory.cs` (7-A.7).
- `tests/PaymentHub.UnitTests/Support/ScriptedHandler.cs` (7-A.8).
- `tests/PaymentHub.UnitTests/Infrastructure/Webhooks/HttpApplicationWebhookDispatcherTests.cs` (movido em 7-A.8).
- `tests/PaymentHub.UnitTests/Worker/OutboxDispatcherWorkerWithRealDispatcherTests.cs` (7-A.8).
- `tests/PaymentHub.UnitTests/Application/Validation/WebhookUrlValidatorTests.cs` (7-A.5).
- `tests/PaymentHub.UnitTests/Application/RegisterApplicationClientValidatorTests.cs` (7-A.5).
- `docs/audits/slice-7a5-webhook-url-ssrf-report-2026-06-26.md`.
- `docs/audits/slice-7a6-worker-appsettings-webhook-secret-key-report-2026-06-26.md`.
- `docs/audits/slice-7a-real-outbox-dispatcher-report-2026-06-26.md` (este arquivo).
- `docs/adr/ADR-0007-webhook-secret-protection.md`.
- `docs/adr/ADR-0010-real-outbox-dispatcher-location.md`.

### Alterados (sub-slices 7-A.1 a 7-A.9)

- `src/PaymentHub.Infrastructure.Postgres/PaymentHub.Infrastructure.Postgres.csproj` (7-A.1: `Microsoft.Extensions.Http 10.0.0`).
- `src/PaymentHub.Infrastructure.Postgres/PostgresServiceCollectionExtensions.cs` (7-A.1: `AddHttpClient` + `IOutboxEventStore`; 7-A.2: dispatcher movido para ca).
- `src/PaymentHub.Application/Tenants/RegisterApplicationClientHandler.cs` (7-A.5: validator ctor + `Must(...)` rule).
- `src/PaymentHub.Application/PaymentHub.Application.csproj` (7-A.5: `<InternalsVisibleTo Include="PaymentHub.UnitTests" />`).
- `src/PaymentHub.Domain/Entities/OutboxEvent.cs` (7-A.7: `MarkRetryWithStatus`, `MarkFailedWithStatus`).
- `src/PaymentHub.Worker/OutboxDispatcherWorker.cs` (7-A.4: `IOutboxRepository` + `IOutboxEventStore` + `IClock`).
- `src/PaymentHub.Worker/Program.cs` (7-A.3: removidos `Noop` + registros duplicados; fail-fast adicionado).
- `src/PaymentHub.Worker/appsettings.json` (7-A.6: placeholder production adicionado).
- `src/PaymentHub.Api/Program.cs` (7-A.3: removidos `using` + registros duplicados).
- `tests/PaymentHub.UnitTests/Application/RegisterApplicationClientHandlerTests.cs` (7-A.5: atualizado para suportar novo ctor do validator).
- `docs/specs/007-inbox-outbox-workers.md` (7-A.9: reescrito com politica `LastError`, dispatcher HTTP real, gaps conhecidos).
- `docs/specs/011-security-and-compliance.md` (7-A.5: secao `Protecao SSRF em ApplicationClient.WebhookUrl`; 7-A.6: secao `Configuracao da chave por ambiente`; 7-A.9: secao `Dispatcher HTTP real do Outbox (Slice 7-A)`).
- `docs/adr/000-adr-index.md` (7-A.9: ADR-0007 e ADR-0010 promovidos para ACEITAS).
- `docs/roadmap/000-payment-hub-roadmap.md` (7-A.9: P1-4 resolvido + Phase 7 com 0 gaps P1 proprios).
- `docs/roadmap/001-development-timeline.md` (7-A.9: tabela de slices concluidos + Phase 7 com 0 gaps P1 proprios).
- `docs/roadmap/002-phase-status-board.md` (7-A.9: dashboard atualizado + indicadores de saude).
- `feature_list.md` (7-A.9: `PH-WORKER-001` e `PH-SEC-001` -> `Concluido`).
- `docs/harness/learnings.md` (7-A.9: entrada nova cobrindo padrao do Slice 7-A).
- `docs/harness/validation-matrix.md` (7-A.9: Phase 7 e Phase 3 preenchidas com Slice 7-A).
- `agent-progress.md` (7-A.5/7-A.6/7-A.9: entradas em `## Historico`; status do Slice 7-A pai).

## Comportamento anterior

- `Worker/Program.cs` registrava `internal sealed class NoopApplicationWebhookDispatcher` como implementacao de `IApplicationWebhookDispatcher`.
- O Worker lia eventos do Outbox, marcava como `Sent` e nao fazia HTTP request.
- `HttpApplicationWebhookDispatcher` vivia em `src/PaymentHub.Api/Webhooks/` (violaria Clean Architecture se Worker dependesse dele).
- `OutboxEvent.LastError` armazenava `ex.Message` truncado em 500 chars (potencialmente continha body HTTP, query strings com credenciais, stack traces internos).
- `ApplicationClient.WebhookUrl` era aceita sem checagem de scheme/host (SSRF direto contra cloud metadata services, RFC1918, localhost).
- `PaymentHub:WebhookSecretEncryptionKey` ausente em producao so era detectado no primeiro dispatch.
- `OutboxDispatcherWorker` acessava `PaymentHubDbContext` direto e usava `DateTime.UtcNow` (impedia testes deterministicos).

## Comportamento novo

- `Worker/Program.cs` NAO tem mais `NoopApplicationWebhookDispatcher` (classe + registro removidos). DI compartilhado com a API via `AddPaymentHubPostgres`.
- `HttpApplicationWebhookDispatcher` vive em `src/PaymentHub.Infrastructure.Postgres/Webhooks/`. Lifetime Scoped. `HttpClient` via `IHttpClientFactory.CreateClient("application-webhook")`.
- Dispatcher chama `_apps.GetByTenantAndIdAsync(outboxEvent.TenantId, outboxEvent.ApplicationId, ct)` (tenant guard). Em miss, loga warning e marca retry.
- `OutboxEvent.LastError` armazena apenas `(WebhookDispatcherCategory, int? statusCode)`. `ex.Message` nunca e persistido. Logs do Worker podem carregar a mensagem completa para debugging.
- `RegisterApplicationClientValidator` valida `WebhookUrl` via `internal static class WebhookUrlValidator`. HTTPS obrigatorio; HTTP apenas em Development para hosts loopback. Bloqueia `localhost`/`*.localhost`/`*.local`, loopback IPv4/IPv6/`::ffff:127.0.0.1`, RFC1918 (`10/8`, `172.16/12`, `192.168/16`), link-local/IMDS (`169.254/16`, `fe80::/10`), unspecified (`0.0.0.0`, `::`), broadcast. Boundary RFC1918 correta (`172.15.x.x` e `172.32.x.x` nao bloqueados).
- Worker resolve `IWebhookSecretProtector` em scope anonimo antes de `host.Run()`. Falha cedo em producao sem chave.
- `OutboxDispatcherWorker` injeta `IOutboxRepository`, `IOutboxEventStore` e `IClock`. Testes deterministicos sem `DbContext` direto.
- `UnprotectFailure`: dispatcher nao envia HTTP request (abortar cedo). `MissingWebhookUrl`: marca como `Failed` direto sem retry.

## Decisoes arquiteturais

Consolidadas em `docs/adr/ADR-0010-real-outbox-dispatcher-location.md`:

1. **Localizacao**: `src/PaymentHub.Infrastructure.Postgres/Webhooks/HttpApplicationWebhookDispatcher.cs` (namespace `PaymentHub.Infrastructure.Postgres.Webhooks`). Worker NAO depende de Api (validado por `scripts/agent-architecture-check.sh`).
2. **Lifetime**: Scoped (registrado em `AddPaymentHubPostgres`). Singleton seria problematico por causa de `IApplicationClientRepository`.
3. **`HttpClient`**: `IHttpClientFactory.CreateClient("application-webhook")`. `HttpClient` direto causa socket exhaustion.
4. **DI centralizado**: tudo em `AddPaymentHubPostgres`. Sem duplicacao transitoria.
5. **`IOutboxEventStore`**: interface em Application + `EfOutboxEventStore` em Infrastructure.Postgres. Permite mockar persistencia em testes.
6. **`IClock`**: reutiliza `SystemClock` ja existente; Worker usa `_clock.UtcNow`.
7. **Tenant guard**: `_apps.GetByTenantAndIdAsync(tenantId, applicationId, ct)` (NUNCA `_apps.GetByIdAsync(applicationId, ct)`).
8. **`LastError` seguro**: enum `WebhookDispatcherCategory` + `int?` statusCode. Metodos publicos `MarkRetryWithStatus` e `MarkFailedWithStatus`.
9. **Validacao `WebhookUrl`**: HTTPS obrigatorio (HTTP apenas em Development para loopback); helper puro `WebhookUrlValidator`.
10. **Fail-fast de crypto**: `Worker/Program.cs:53-56` resolve `IWebhookSecretProtector` antes de `host.Run()`.
11. **`NoopApplicationWebhookDispatcher` removido**: classe + registro apagados do codigo de producao.

Consolidadas em `docs/adr/ADR-0007-webhook-secret-protection.md`:

1. `IWebhookSecretProtector` em Application/Abstractions/Security/ICrypto.cs.
2. `AesWebhookSecretProtector` em Infrastructure.Postgres/Security/CryptoServices.cs (AES-CBC reversivel com prefixo `PaymentHub.ApplicationClient.WebhookSecret.v1`).
3. Parametro nomeado `protectedWebhookSecret` na entidade `ApplicationClient`.
4. Chave separada: `PaymentHub:WebhookSecretEncryptionKey` (independente de `CredentialEncryptionKey`).
5. Sem `IDataProtectionProvider` (Data Protection do ASP.NET Core) — manter consistencia com `ICredentialProtector`.
6. `HttpApplicationWebhookDispatcher` chama `Unprotect` imediatamente antes de `Sign`; se falhar, NAO envia HTTP request.

## Decisoes de seguranca

- **Tenant guard** antes de despachar webhook (item 7 da arquitetura).
- **HMAC** sobre `{timestamp}.{rawBody}` (existente, mantido). Algoritmo `HMAC-SHA256`. Hex lowercase.
- **`LastError` nao contem** `ex.Message`, body HTTP, query strings, stack traces ou secrets.
- **`WebhookUrl` validada** por HTTPS/SSRF antes de qualquer persistencia.
- **`WebhookSecret` protegido** em repouso via `IWebhookSecretProtector` (AES-CBC reversivel).
- **Fail-fast** de chave ausente no startup do Worker.
- **Segredo raw nunca aparece** em logs, respostas HTTP, DTOs ou `OutboxEvent.LastError`.
- **Mensagem de erro unificada** anti-enumeration: `"WebhookUrl must be an absolute HTTPS URL pointing to a public endpoint."` — nao revela qual regra foi violada.
- **Compartilhamento API/Worker**: a mesma chave precisa estar disponivel nos dois processos; divergencia e detectada em runtime via `InvalidOperationException("Protected webhook secret purpose mismatch.")`.

## Contrato do webhook interno

```json
{
  "eventId": "guid",
  "eventType": "payment.approved",
  "paymentId": "guid",
  "externalReference": "job-search-order-123",
  "amount": 2990,
  "currency": "BRL",
  "provider": "Fake",
  "status": "Approved",
  "providerPaymentId": "fake_123",
  "occurredAt": "2026-06-16T12:00:00Z"
}
```

Headers:

```http
Content-Type: application/json
X-PaymentHub-Event-Id: <eventId>
X-PaymentHub-Event-Type: payment.approved
X-PaymentHub-Timestamp: <unix_time_seconds>
X-PaymentHub-Signature: <hex_lowercase_hmac_sha256>
```

`eventId` e o id estavel do `OutboxEvent`. Reprocessamento mantem o mesmo `eventId`.

## Assinatura HMAC

```text
rawBody = corpo HTTP exatamente como enviado
timestamp = valor do header X-PaymentHub-Timestamp
signedPayload = timestamp + "." + rawBody
signature = HMACSHA256(webhookSecret, UTF8(signedPayload))
signatureFormat = hexadecimal lowercase
```

O segredo raw vem de `IWebhookSecretProtector.Unprotect(blob)`. Se `Unprotect` falhar, dispatcher NAO envia HTTP request (categoria `UnprotectFailure`).

## Worker e Outbox

- `OutboxDispatcherWorker` (Scoped hosted service) injeta `IOutboxRepository`, `IOutboxEventStore`, `IClock`, `IOutboxDispatcherOptions`.
- Seleciona eventos `Pending` cujo `next_retry_at` esteja vazio ou vencido.
- Para cada evento: marca `Processing` (futuro), chama `IApplicationWebhookDispatcher.DispatchAsync(outboxEvent, cancellationToken)`, atualiza `OutboxEvent` conforme resposta.
- `DateTime.UtcNow` removido; usa `_clock.UtcNow`.
- `Retry policy`: 0s, 1m, 5m, 15m, 1h, Failed.
- `WebhookProcessorWorker` (escopo deste slice: nao alterado). Processa `WebhookEvent` recebido.

## Politica de LastError seguro

`OutboxEvent.LastError` (string?) armazena representacao compacta de `(category, statusCode)`. Implementacao em `src/PaymentHub.Domain/Entities/OutboxEvent.cs:65-150`:

```csharp
public void MarkRetryWithStatus(WebhookDispatcherCategory category, int statusCode, DateTime nextRetryAt)
public void MarkFailedWithStatus(WebhookDispatcherCategory category, int statusCode)
```

Categorias (`src/PaymentHub.Domain/Enums/WebhookDispatcherCategory.cs`):

| Categoria | Valor | Quando | Retry? |
|-----------|-------|--------|--------|
| `HttpFailure` | 1 | Consumer retornou nao-2xx | sim |
| `NetworkError` | 2 | DNS, conexao reset, TLS handshake | sim |
| `Timeout` | 3 | `HttpClient` excedeu `WebhookHttpTimeoutSeconds` | sim |
| `UnprotectFailure` | 4 | `IWebhookSecretProtector.Unprotect` falhou | sim |
| `MissingWebhookUrl` | 5 | Application sem `WebhookUrl` | nao (Failed direto) |
| `MissingWebhookSecret` | 6 | Reservado (nao deve ocorrer) | depende |
| `UnexpectedDispatcherError` | 7 | Excecao nao esperada | sim |

`WebhookDispatcherException` (em `src/PaymentHub.Application/Abstractions/Outbox/WebhookDispatcherException.cs`) carrega `Category` + `StatusCode?` e mensagem generica por design.

## Protecao de WebhookSecret

`IWebhookSecretProtector` em `src/PaymentHub.Application/Abstractions/Security/ICrypto.cs`:

```csharp
public interface IWebhookSecretProtector
{
    string Protect(string plainTextSecret);
    string Unprotect(string protectedSecret);
}
```

`AesWebhookSecretProtector` em `src/PaymentHub.Infrastructure.Postgres/Security/CryptoServices.cs`:

- AES-CBC com chave de 32 bytes (lida de `PaymentHub:WebhookSecretEncryptionKey`).
- IV randomico de 16 bytes por cifragem.
- Prefixo de proposito `PaymentHub.ApplicationClient.WebhookSecret.v1` antes do segredo raw.
- Verificacao de prefixo em tempo constante via `CryptographicOperations.FixedTimeEquals`.
- `Unprotect` rejeita blobs sem prefixo (mensagem generica `"Protected webhook secret purpose mismatch."`).

Configuracao por ambiente (Slice 7-A.6):

- `appsettings.json` (production): placeholder vazio `"PaymentHub: { WebhookSecretEncryptionKey: "" }"`.
- `appsettings.Development.json`: valor fake `dev-webhook-secret-key-change-me-32bytes` (39 chars, >= 32).
- Producao: `PaymentHub__WebhookSecretEncryptionKey=<valor-real>` via env var / secret manager.

## Validacao WebhookUrl HTTPS/SSRF

`internal static class WebhookUrlValidator` em `src/PaymentHub.Application/Tenants/Validation/WebhookUrlValidator.cs`:

```csharp
public static bool IsAllowed(string? value, bool isDevelopment, out string? reason)
```

Regras:

- `Uri.TryCreate(value, UriKind.Absolute, out var uri)` obrigatorio.
- HTTPS obrigatorio; HTTP apenas em Development para hosts loopback.
- Bloqueia `localhost`, `*.localhost`, `*.local`.
- Bloqueia IPs: loopback (`127/8`, `::1`, `::ffff:127.0.0.1`), RFC1918 (`10/8`, `172.16/12`, `192.168/16`), link-local/IMDS (`169.254/16`, `fe80::/10`), unspecified (`0.0.0.0`, `::`), broadcast.
- Boundary RFC1918 correta (`172.15.x.x` e `172.32.x.x` nao bloqueados).

Mensagem unificada anti-enumeration: `"WebhookUrl must be an absolute HTTPS URL pointing to a public endpoint."`.

Integracao:

- `RegisterApplicationClientValidator` em `src/PaymentHub.Application/Tenants/RegisterApplicationClientHandler.cs` recebe `IRuntimeEnvironment` no ctor.
- Regra: `RuleFor(x => x.WebhookUrl).MaximumLength(2000).Must((req, url) => WebhookUrlValidator.IsAllowed(url, environment.IsDevelopment, out _)).When(x => !string.IsNullOrWhiteSpace(x.WebhookUrl)).WithMessage("WebhookUrl must be an absolute HTTPS URL pointing to a public endpoint.")`.
- Auto-wired via `AddValidatorsFromAssemblyContaining<RegisterTenantValidator>()` em `Program.cs:81`.

## Testes adicionados

Suite previa: 178 testes. Suite nova: **281 testes** (+103). Detalhes:

- **7-A.5** (66+ testes): `WebhookUrlValidatorTests` (vacuous, public HTTPS, malformed URIs, non-HTTP schemes, HTTP outside Dev, HTTP on public host in Dev, localhost + `.localhost`, `.local`, loopback IPv4+IPv6+IPv4-mapped, RFC1918 tres blocos + boundary negativa, link-local/IMDS, unspecified IPv4+IPv6, broadcast, HTTP loopback em Dev).
- **7-A.5** (17 testes): `RegisterApplicationClientValidatorTests` (boundary cases, valid HTTPS, HTTP rejected in prod/dev, localhost/loopback/RFC1918/IMDS, malformed, HTTP loopback in Dev, MaximumLength, regras restantes TenantId/Name).
- **7-A.7/8**: dispatcher tests movidos para `tests/PaymentHub.UnitTests/Infrastructure/Webhooks/`. Worker tests em `tests/PaymentHub.UnitTests/Worker/OutboxDispatcherWorkerWithRealDispatcherTests.cs` com `IOutboxRepository` + `IOutboxEventStore` + `IClock` mockados. Helper `tests/PaymentHub.UnitTests/Support/ScriptedHandler.cs` (subclasse `HttpMessageHandler`).

Filtros:

- `~WebhookUrl`: 69 passed.
- `~RegisterApplicationClient`: 50 passed.
- `~WebhookSecret`: 26 passed.
- `~ApplicationWebhook`: 13 passed (sem regressao).
- `~OutboxDispatcherWorker`: 17 passed (sem regressao).
- `~Bootstrap`: 15 passed (sem regressao).
- `~ApiKeyAuthenticationMiddleware`: 11 passed (sem regressao).
- `~ProviderAccount`: 15 passed (sem regressao).

## Validacoes executadas

| Comando | Resultado |
|---------|-----------|
| `git status --short` | Apenas arquivos de doc alterados (sem codigo produtivo). |
| `dotnet restore PaymentHub.slnx` | 9 projetos, 0 errors, 0 warnings. |
| `dotnet build PaymentHub.slnx` | 9 projetos, **0 errors / 0 warnings**. |
| `dotnet test PaymentHub.slnx` | **281 passed**, 0 failed, 0 skipped. |
| `--filter ~ApplicationWebhook` | 13 passed (sem regressao). |
| `--filter ~OutboxDispatcherWorker` | 17 passed (sem regressao). |
| `--filter ~WebhookSecret` | 26 passed. |
| `--filter ~RegisterApplicationClient` | 50 passed. |
| `--filter ~WebhookUrl` | 69 passed. |
| `docker compose config` | Validado. |
| `scripts/agent-verify.sh` | passed. |
| `RUN_DOTNET_VALIDATION=1 scripts/agent-verify.sh` | passed. |
| `scripts/agent-architecture-check.sh` | passed (Worker NAO depende de Api). |
| `scripts/agent-docs-check.sh` | passed. |
| `git diff --check` | passed. |

## Evidencias

- `src/PaymentHub.Infrastructure.Postgres/Webhooks/HttpApplicationWebhookDispatcher.cs`
- `src/PaymentHub.Application/Abstractions/Outbox/IOutboxEventStore.cs`
- `src/PaymentHub.Application/Abstractions/Outbox/WebhookDispatcherException.cs`
- `src/PaymentHub.Infrastructure.Postgres/Outbox/EfOutboxEventStore.cs`
- `src/PaymentHub.Domain/Enums/WebhookDispatcherCategory.cs`
- `src/PaymentHub.Domain/Entities/OutboxEvent.cs` (`MarkRetryWithStatus`, `MarkFailedWithStatus`)
- `src/PaymentHub.Application/Tenants/Validation/WebhookUrlValidator.cs`
- `src/PaymentHub.Application/Tenants/RegisterApplicationClientHandler.cs` (`RegisterApplicationClientValidator` ctor)
- `src/PaymentHub.Worker/OutboxDispatcherWorker.cs` (usa `IOutboxRepository` + `IOutboxEventStore` + `IClock`)
- `src/PaymentHub.Worker/Program.cs` (fail-fast em scope anonimo antes de `host.Run()`)
- `src/PaymentHub.Worker/appsettings.json` (placeholder production)
- `src/PaymentHub.Worker/appsettings.Development.json` (valor dev fake)
- `docs/adr/ADR-0007-webhook-secret-protection.md`
- `docs/adr/ADR-0010-real-outbox-dispatcher-location.md`
- `docs/adr/000-adr-index.md`
- `docs/specs/007-inbox-outbox-workers.md`
- `docs/specs/011-security-and-compliance.md`
- `docs/roadmap/000-payment-hub-roadmap.md`
- `docs/roadmap/001-development-timeline.md`
- `docs/roadmap/002-phase-status-board.md`
- `feature_list.md` (`PH-WORKER-001` e `PH-SEC-001` -> `Concluido`)
- `docs/harness/learnings.md` (entrada nova Slice 7-A)
- `docs/harness/validation-matrix.md` (Phase 7 + Phase 3 preenchidas)
- `docs/audits/slice-7a5-webhook-url-ssrf-report-2026-06-26.md`
- `docs/audits/slice-7a6-worker-appsettings-webhook-secret-key-report-2026-06-26.md`
- `docs/audits/slice-7a-real-outbox-dispatcher-report-2026-06-26.md` (este arquivo)
- `agent-progress.md` (entrada de Historico do Slice 7-A.9)

## Gaps remanescentes

Gaps documentados e deferidos (fora do escopo deste slice):

1. **M1-security (sweep de `Processing` orfaos)**: Se o Worker cair entre `MarkProcessing` e a conclusao do dispatch, o evento fica `Processing` para sempre. Em MVP single-instance, nao ha impacto. Em multi-instancia ou apos deploy com restart brusco, vira problema. Documentado em `docs/specs/007-inbox-outbox-workers.md`. Implementacao sugerida: cron job que move `Processing` com `updated_at < now - X` para `Pending`. Fora do escopo.
2. **C.3-qa (`FOR UPDATE SKIP LOCKED`)**: Concorrencia multi-instancia via lock transacional. Nao e problema em single-instance; sera necessario quando Worker for escalado horizontalmente.
3. **B4-security (headers adicionais)**: `X-PaymentHub-Event-Type`/`X-PaymentHub-Tenant`/`X-PaymentHub-Application` nao estao sendo validados/autorizados pelo consumidor. O dispatcher envia; o consumidor confia no HMAC. Risco de spoofing e baixo (HMAC ja garante autenticidade).
4. **P2-2 (Slice 1-IT)**: Testes de integracao com Postgres/migrations nao foram implementados. Cobertura zero ate Slice 1-IT.
5. **API `appsettings.json` placeholder**: Mesmo gap que o Slice 7-A.6 fechou para o Worker existe em `src/PaymentHub.Api/appsettings.json` (sem secao `PaymentHub`). Fora de escopo do Slice 7-A.9 (constraint de briefing). Recomendacao: aplicar o mesmo placeholder em slice proprio.
6. **P2-3 (AuditLog em handlers administrativos)**: Phase 6 ainda nao fechou. Continua `IMPLEMENTING` por causa deste gap.
7. **Risco de rotacao de WebhookSecret**: ADR-0007 documentou que a rotacao completa do segredo (re-cifrar todos os blobs) fica fora do escopo. Quando necessario, deve ser feita em slice proprio.
8. **Provider real (Slice 2-A)**: Fora do escopo.

## Proximos passos recomendados

Em ordem sugerida (sem ordem obrigatoria; depende de decisao de produto):

1. **Slice 1-IT** — Base inicial de testes de integracao com Postgres/migrations. Recomendado para fechar P2-2 antes de avancar para Phase 4.
2. **Slice 2-A** — AbacatePay sandbox funcional. Recomendado para validar P2-1 (assinatura de webhooks externos) e tirar Phase 2 do `IMPLEMENTING`.
3. **Slice 3-H** — Hardening de webhooks externos/internos end-to-end (se houver ganho apos Slice 1-IT + 2-A).
4. **Slice 6-E** (P2-3 AuditLog) — Fechar Phase 6 e levar para `VALIDATED`. Recomendado se painel admin for priorizado.
5. **Slice 5-A** (ADR-0008 autenticacao do painel admin) — So faz sentido apos Phase 6 estar totalmente `VALIDATED`.
6. **Phase 9 — Observabilidade minima** (apos fluxo real estar estavel com Slice 1-IT + 2-A + 3-H).

Apos a consolidacao deste slice, Bloco A (Phase 6 + Phase 7) esta fechado com 0 gaps P1 proprios. O proximo bloco pode ser escolhido conforme prioridade de produto.