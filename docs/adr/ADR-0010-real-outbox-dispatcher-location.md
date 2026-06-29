# ADR-0010 - Localizacao e arquitetura do dispatcher HTTP real do Outbox

## Status

Aceito

## Data

2026-06-26 (Slice 7-A.9 — consolidacao)

## Contexto

Ate o Slice 7-A.3 (2026-06-26), o `PaymentHub.Worker/Program.cs` registrava um `internal sealed class NoopApplicationWebhookDispatcher` (`src/PaymentHub.Worker/Program.cs:62-70`, removido no slice) como implementacao de `IApplicationWebhookDispatcher`. Isso significava que o ciclo de Outbox era teoricamente correto (eventos eram lidos, marcados como `Sent`, retry policy aplicada), mas **nenhum HTTP request real era feito**. O gap P1-4 da auditoria de 2026-06-17 ficou exposto por toda a Phase 7 ate este slice.

A substituicao do dispatcher no-op por um dispatcher HTTP real trouxe decisoes arquiteturais que nao estavam formalizadas em nenhum ADR:

1. **Localizacao do dispatcher**: deveria ficar em `PaymentHub.Api/Webhooks` (local original) ou em `PaymentHub.Infrastructure.Postgres/Webhooks`?
2. **Lifetime do dispatcher**: Singleton, Scoped ou Transient?
3. **Origem do `HttpClient`**: instancia direta, `IHttpClientFactory` ou `HttpClient` injetado?
4. **Onde registrar o DI**: `Program.cs` da API, do Worker, ou em `AddPaymentHubPostgres`?
5. **Testabilidade do Worker**: como tornar `OutboxDispatcherWorker` testavel sem Postgres real?
6. **Tenant guard**: o dispatcher estava usando `_apps.GetByIdAsync(outboxEvent.ApplicationId, ct)` que permite cross-tenant access; precisa trocar para `_apps.GetByTenantAndIdAsync(tenantId, applicationId, ct)`.
7. **`OutboxEvent.LastError`**: estava gravando `ex.Message` (que pode conter body HTTP, query strings, stack traces); precisa gravar categoria + status code apenas.
8. **Validacao de `WebhookUrl`**: ja existia `MaximumLength(2000)` mas sem checagem de scheme/host; precisa adicionar HTTPS/SSRF no validator.
9. **Fail-fast do Worker**: precisa resolver `IWebhookSecretProtector` antes de `host.Run()` para detectar chave ausente no startup.

Decisoes pre-relacionadas:

- ADR-0001: stack .NET 10.
- ADR-0002: Postgres Inbox/Outbox no MVP.
- ADR-0007: `WebhookSecret` protegido em repouso (pre-requisito de seguranca).
- Spec 007: workers, retry policy, idempotencia.
- Spec 011: HTTPS obrigatorio, HMAC para webhooks internos.

## Decisao

### 1. Localizacao do dispatcher HTTP real

`HttpApplicationWebhookDispatcher` fica em `src/PaymentHub.Infrastructure.Postgres/Webhooks/HttpApplicationWebhookDispatcher.cs`, com namespace `PaymentHub.Infrastructure.Postgres.Webhooks`.

**Justificativa**: o dispatcher precisa acessar `IApplicationClientRepository`, `IOutboxEventStore`, `IWebhookSecretProtector`, `IOptions<PaymentHubOptions>` e fazer POST HTTP. Tudo isso ja vive em `Infrastructure.Postgres` ou em dependencias do projeto. Manter em `Api/Webhooks` forcaria o Worker a depender de `PaymentHub.Api`, violando `scripts/agent-architecture-check.sh` (Worker **nao** pode depender de Api). O namespace `PaymentHub.Infrastructure.Postgres.Webhooks` tambem e o lugar canonico para qualquer adapter que combine Postgres + HTTP contra consumers.

### 2. Lifetime do dispatcher

`Scoped` (registrado via `services.AddScoped<IApplicationWebhookDispatcher, HttpApplicationWebhookDispatcher>()` em `AddPaymentHubPostgres`).

**Justificativa**: o dispatcher depende de `IApplicationClientRepository`, `IOutboxEventStore` e `IOptions<PaymentHubOptions>`, todos com ciclo de vida compativel com Scoped. O `HttpClient` em si e gerenciado pelo `IHttpClientFactory` (vide decisao 3), entao o custo de instanciar o dispatcher e desprezivel. Singleton seria problematico porque `IApplicationClientRepository` e Scoped (transiente via DbContext).

### 3. `HttpClient` via `IHttpClientFactory`

O cliente HTTP e registrado em `AddPaymentHubPostgres` via `services.AddHttpClient("application-webhook")` e injetado no dispatcher via `IHttpClientFactory.CreateClient("application-webhook")`. Timeout configurado por `PaymentHub:WebhookHttpTimeoutSeconds` (default 10 segundos).

**Justificativa**: `HttpClient` direto emecora sockets (socket exhaustion); `IHttpClientFactory` gerencia o pool corretamente. Nomear o cliente ("application-webhook") permite policies futuras (retry, circuit breaker) sem mudar o codigo do dispatcher.

### 4. Registro centralizado em `AddPaymentHubPostgres`

Toda a DI do dispatcher + `HttpClient` + `IOutboxEventStore` + `IApplicationWebhookDispatcher` fica em `PaymentHub.Infrastructure.Postgres/PostgresServiceCollectionExtensions.cs::AddPaymentHubPostgres`. Nenhum registro adicional e necessario em `Program.cs` da API ou do Worker.

**Justificativa**: como o dispatcher vive em `Infrastructure.Postgres`, o registro tambem deve viver la. A API e o Worker compartilham o mesmo conjunto de servicos via `AddPaymentHubPostgres(builder.Configuration)`, garantindo que nao ha duplicacao transitoria de DI nem diferenca de comportamento entre os dois processos.

### 5. `IOutboxEventStore` para testabilidade do Worker

Nova interface `IOutboxEventStore` em `src/PaymentHub.Application/Abstractions/Outbox/IOutboxEventStore.cs` com metodo `Task SaveAsync(OutboxEvent outboxEvent, CancellationToken cancellationToken)`. Implementacao `EfOutboxEventStore` em `src/PaymentHub.Infrastructure.Postgres/Outbox/EfOutboxEventStore.cs` envolta em `PaymentHubDbContext`. Registro Scoped em `AddPaymentHubPostgres`.

**Justificativa**: o `OutboxDispatcherWorker` nao deve acessar `PaymentHubDbContext` diretamente para persistir mudancas; isso impede testes deterministicos. `IOutboxEventStore` permite mockar a persistencia em testes unitarios enquanto `IOutboxRepository` cobre a selecao de eventos pendentes. O lifetime Scoped e consistente com o resto da infraestrutura.

### 6. `IClock` no Worker

`OutboxDispatcherWorker` recebe `IClock` no construtor e usa `_clock.UtcNow` em vez de `DateTime.UtcNow` para calcular `next_retry_at`.

**Justificativa**: ja existia `IClock` em `PaymentHub.Application/Abstractions/Context/ITenantContext.cs:9` (registrado como `SystemClock` singleton em `PostgresServiceCollectionExtensions.cs:52`). Reutilizar evita `DateTime.UtcNow` hardcoded que impede testes deterministicos de retry policy.

### 7. Tenant guard no dispatcher

`HttpApplicationWebhookDispatcher` chama `_apps.GetByTenantAndIdAsync(outboxEvent.TenantId, outboxEvent.ApplicationId, ct)` em vez de `_apps.GetByIdAsync(outboxEvent.ApplicationId, ct)`. Em miss, loga warning com `tenantId`/`applicationId` e retorna sem lancar.

**Justificativa**: o `OutboxEvent` ja traz `tenant_id` e `application_id` separados. Usar `GetByIdAsync` permitia que um dispatcher mal-comportado buscasse uma application de outro tenant se o `application_id` fosse ambiguo ou se houvesse race condition. Tenant guard explicito elimina esse vetor.

### 8. `LastError` seguro (Slice 7-A.7)

`OutboxEvent` ganha metodos `MarkRetryWithStatus(WebhookDispatcherCategory category, int statusCode, DateTime nextRetryAt)` e `MarkFailedWithStatus(WebhookDispatcherCategory category, int statusCode)`. Categorias em `PaymentHub.Domain/Enums/WebhookDispatcherCategory.cs`:

- `HttpFailure` (1) — consumer retornou nao-2xx.
- `NetworkError` (2) — falha de DNS, conexao, TLS.
- `Timeout` (3) — `HttpClient` excedeu timeout.
- `UnprotectFailure` (4) — `IWebhookSecretProtector.Unprotect` falhou.
- `MissingWebhookUrl` (5) — application sem `WebhookUrl`.
- `MissingWebhookSecret` (6) — reservado, nao deve ocorrer.
- `UnexpectedDispatcherError` (7) — qualquer outra excecao.

Worker **nao** persiste `ex.Message` em `LastError`. A mensagem e apenas logada; o que vai para o banco e `(categoria, statusCode)`.

**Justificativa**: `ex.Message` pode conter body HTTP retornado pelo consumer (com dados de pagamento, query strings, etc.), URL com credenciais em query string, stack trace com caminhos internos, ou ate o proprio `WebhookSecret` se um consumer malicioso conseguir manipular a excecao. Categorias enum + status code sao seguros para auditoria e alerta.

### 9. Validacao HTTPS/SSRF no `WebhookUrl` (Slice 7-A.5)

Helper puro `internal static class WebhookUrlValidator` em `src/PaymentHub.Application/Tenants/Validation/WebhookUrlValidator.cs` com `public static bool IsAllowed(string? value, bool isDevelopment, out string? reason)`. Regras:

- HTTPS obrigatorio (HTTP apenas em Development para hosts loopback).
- Hostnames bloqueados: `localhost`, `*.localhost`, `*.local`.
- IPs bloqueados: loopback (127/8, ::1, ::ffff:127.0.0.1), RFC1918 (10/8, 172.16/12, 192.168/16), link-local/IMDS (169.254/16, fe80::/10), unspecified (0.0.0.0, ::), broadcast.
- Mensagem unificada anti-enumeration: `"WebhookUrl must be an absolute HTTPS URL pointing to a public endpoint."`.

`RegisterApplicationClientValidator` injeta `IRuntimeEnvironment` no construtor e adiciona `RuleFor(x => x.WebhookUrl).Must(...).WithMessage("WebhookUrl must be an absolute HTTPS URL pointing to a public endpoint.")`.

`<InternalsVisibleTo Include="PaymentHub.UnitTests" />` em `PaymentHub.Application.csproj` para expor o helper aos testes sem inflar a API publica.

### 10. Fail-fast de `IWebhookSecretProtector` no Worker

`src/PaymentHub.Worker/Program.cs:53-56` resolve `IWebhookSecretProtector` em um scope anonimo antes de `host.Run()`. Se a chave `PaymentHub:WebhookSecretEncryptionKey` estiver ausente, o startup falha com `InvalidOperationException("PaymentHub:WebhookSecretEncryptionKey is required.")`.

**Justificativa**: sem o fail-fast, o Worker subiria normalmente e so falharia no primeiro dispatch do Outbox (quando `Unprotect` e chamado). Isso atrasa a deteccao de erro de configuracao e pode mascarar problemas de deploy.

### 11. `NoopApplicationWebhookDispatcher` removido

A classe `internal sealed class NoopApplicationWebhookDispatcher` (que existia inline em `src/PaymentHub.Worker/Program.cs:62-70`) foi completamente removida, junto com o registro `builder.Services.AddScoped<IApplicationWebhookDispatcher, NoopApplicationWebhookDispatcher>();`.

**Justificativa**: dispatcher no-op nao tem mais uso legitimo em nenhum ambiente. Qualquer teste que queira simular o dispatcher agora usa `Mock<IApplicationWebhookDispatcher>` (via Moq).

## Decisoes de seguranca consolidadas

- **Tenant guard** antes de despachar webhook (item 7).
- **HMAC** sobre `{timestamp}.{rawBody}` (ja documentado em spec 011; dispatcher nao introduz nova formatacao).
- **`LastError`** nao salva `ex.Message`, body HTTP, query strings ou secrets (item 8).
- **`WebhookUrl`** tem validacao HTTPS/SSRF (item 9).
- **`WebhookSecret`** e protegido em repouso via `IWebhookSecretProtector` (ADR-0007).
- **Fail-fast** de chave ausente no startup (item 10).

## Gaps conhecidos

1. **Sweep de eventos `Processing` orfaos**: se o Worker cair entre `MarkProcessing` e a conclusao do dispatch, o evento fica `Processing` para sempre. Em MVP single-instance, nao ha impacto; em multi-instancia ou apos deploy com restart brusco, vira problema. Documentado em `docs/specs/007-inbox-outbox-workers.md`. Implementacao sugerida: cron job que move `Processing` com `updated_at < now - X` para `Pending`. **Fora do escopo do Slice 7-A.**

2. **Concorrencia multi-instancia**: o `OutboxDispatcherWorker` seleciona eventos `Pending` sem `FOR UPDATE SKIP LOCKED`. Em multi-instancia, dois workers podem pegar o mesmo evento. Spec 007 menciona `FOR UPDATE SKIP LOCKED` como objetivo futuro; a implementacao depende de driver/banco. **Fora do escopo do Slice 7-A.**

3. **Headers adicionais B4-security**: `X-PaymentHub-Event` / `X-PaymentHub-Tenant` / `X-PaymentHub-Application` nao estao sendo validados/autorizados pelo consumidor. O dispatcher envia; o consumidor confia. Risco de spoofing e baixo (consumidor confia no HMAC), mas a presenca dos headers e informacional. **Fora do escopo do Slice 7-A.**

4. **API `appsettings.json` placeholder**: o mesmo gap que o Slice 7-A.6 fechou para o Worker ainda existe em `src/PaymentHub.Api/appsettings.json` (sem secao `PaymentHub`). **Fora do escopo do Slice 7-A** (constraint de briefing). Recomendacao: aplicar o mesmo placeholder em slice proprio.

5. **Testes de integracao com Postgres real**: dispatcher e worker tem cobertura unitaria forte (vide Slice 7-A.8), mas nenhum teste de integracao com Postgres/migrations (P2-2). **Fora do escopo do Slice 7-A.**

## Consequencias

- Worker continua sem depender de API (validado por `scripts/agent-architecture-check.sh`).
- Dispatcher real desbloqueia a evolucao dos webhooks internos: o consumidor passa a receber eventos assinados; testes de fluxo end-to-end ficam mais realistas.
- `IOutboxEventStore` + `IClock` permitem testar o Worker de forma deterministica sem banco real.
- `LastError` seguro impede que dados sensiveis do consumidor (body HTTP, query strings) sejam persistidos.
- Validacao HTTPS/SSRF impede que o Worker seja usado como proxy para alvos internos.
- Fail-fast reduz MTTR em deploys com configuracao errada.

## Status final

Aceito em 2026-06-26 com a conclusao do Slice 7-A (sub-slices 7-A.1 a 7-A.9). Phase 7 alcancou 0 gaps P1 proprios.

## Arquivos relacionados

- `src/PaymentHub.Infrastructure.Postgres/Webhooks/HttpApplicationWebhookDispatcher.cs`
- `src/PaymentHub.Application/Abstractions/Outbox/IApplicationWebhookDispatcher.cs`
- `src/PaymentHub.Application/Abstractions/Outbox/IOutboxEventStore.cs`
- `src/PaymentHub.Application/Abstractions/Outbox/WebhookDispatcherException.cs`
- `src/PaymentHub.Infrastructure.Postgres/Outbox/EfOutboxEventStore.cs`
- `src/PaymentHub.Infrastructure.Postgres/PostgresServiceCollectionExtensions.cs` (DI centralizado)
- `src/PaymentHub.Domain/Enums/WebhookDispatcherCategory.cs`
- `src/PaymentHub.Domain/Entities/OutboxEvent.cs` (MarkRetryWithStatus, MarkFailedWithStatus)
- `src/PaymentHub.Application/Tenants/Validation/WebhookUrlValidator.cs`
- `src/PaymentHub.Application/Tenants/RegisterApplicationClientHandler.cs` (validator com IRuntimeEnvironment)
- `src/PaymentHub.Worker/OutboxDispatcherWorker.cs`
- `src/PaymentHub.Worker/Program.cs` (fail-fast)
- `src/PaymentHub.Worker/appsettings.json` (placeholder)
- `docs/specs/007-inbox-outbox-workers.md`
- `docs/specs/011-security-and-compliance.md`
- `docs/adr/ADR-0007-webhook-secret-protection.md`
- `docs/audits/slice-7a-real-outbox-dispatcher-report-2026-06-26.md`
- `docs/audits/slice-7a5-webhook-url-ssrf-report-2026-06-26.md`
- `docs/audits/slice-7a6-worker-appsettings-webhook-secret-key-report-2026-06-26.md`
