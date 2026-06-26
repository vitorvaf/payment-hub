# Harness Learnings

Este arquivo registra aprendizados técnicos do projeto que devem orientar futuros agentes.

## Formato

### YYYY-MM-DD - Título

- Contexto:
- Decisão:
- Evidência:
- Impacto para próximos agentes:

### 2026-06-26 - Dispatcher HTTP real do Outbox vive em Infrastructure.Postgres.Webhooks com tenant guard, IOutboxEventStore, IClock, LastError seguro e fail-fast no startup

- Contexto: Ate o Slice 7-A.3 (2026-06-26), `PaymentHub.Worker/Program.cs` registrava um `internal sealed class NoopApplicationWebhookDispatcher` inline (linhas 62-70 do Program.cs antigo). O ciclo de Outbox era teoricamente correto (eventos lidos, marcados como `Sent`, retry policy aplicada), mas nenhum HTTP request real era feito. Gap P1-4 da auditoria de 2026-06-17 ficou exposto por toda a Phase 7. Alem disso, o dispatcher lia `ApplicationClient.WebhookSecret` raw para assinar HMAC (Slice 6-C ja tinha protegido o segredo, mas o dispatcher ainda fazia parte do codigo pre-Slice-6-C).
- Decisão: (1) `HttpApplicationWebhookDispatcher` realocado para `src/PaymentHub.Infrastructure.Postgres/Webhooks/HttpApplicationWebhookDispatcher.cs` com namespace `PaymentHub.Infrastructure.Postgres.Webhooks`. Worker **nao** pode depender de Api (validado por `scripts/agent-architecture-check.sh`); o dispatcher tem que acessar `IApplicationClientRepository`, `IOutboxEventStore`, `IWebhookSecretProtector`, `IOptions<PaymentHubOptions>` e fazer HTTP — tudo isso ja vive em `Infrastructure.Postgres`. (2) Lifetime **Scoped** (registrado em `AddPaymentHubPostgres`): o dispatcher depende de `IApplicationClientRepository` (Scoped) e de `IOptions<PaymentHubOptions>` (Singleton); Singleton seria problematico por causa do repositorio. (3) `HttpClient` via `IHttpClientFactory.CreateClient("application-webhook")` (registrado em `AddPaymentHubPostgres`). `HttpClient` direto causa socket exhaustion; o factory gerencia o pool corretamente. (4) `IOutboxEventStore` em `Application/Abstractions/Outbox/IOutboxEventStore.cs` com metodo `Task SaveAsync(OutboxEvent, CancellationToken)` e implementacao `EfOutboxEventStore` em `Infrastructure.Postgres/Outbox/`. Permite testar `OutboxDispatcherWorker` sem `DbContext` direto. (5) `OutboxDispatcherWorker` injeta `IOutboxRepository`, `IOutboxEventStore` e `IClock` (este ja existia). `DateTime.UtcNow` removido em favor de `_clock.UtcNow` para retry policy deterministica em testes. (6) Tenant guard via `_apps.GetByTenantAndIdAsync(outboxEvent.TenantId, outboxEvent.ApplicationId, ct)` em vez de `_apps.GetByIdAsync(applicationId, ct)` — o `OutboxEvent` ja traz `tenant_id` separado, entao usar `GetByIdAsync` permitiria cross-tenant access se `application_id` fosse ambiguo. (7) `OutboxEvent.LastError` agora armazena apenas `WebhookDispatcherCategory` (enum de 7 valores: `HttpFailure`, `NetworkError`, `Timeout`, `UnprotectFailure`, `MissingWebhookUrl`, `MissingWebhookSecret`, `UnexpectedDispatcherError`) + `int?` statusCode. `ex.Message` nunca e persistido. `MarkRetryWithStatus(category, statusCode, nextRetryAt)` e `MarkFailedWithStatus(category, statusCode)` sao os unicos metodos publicos para atualizar `LastError`. `WebHookDispatcherException` carrega a categoria + statusCode e sua mensagem e generica por design. (8) `UnprotectFailure` nao envia HTTP request — "abortar cedo" em vez de "enviar sem assinatura". `MissingWebhookUrl` marca como `Failed` direto sem retry. (9) `WebhookUrl` validada por `RegisterApplicationClientValidator` com `WebhookUrlValidator` helper puro (`internal static class`, `IsAllowed(string?, bool, out string?)`) — HTTPS obrigatorio, HTTP apenas em Development para loopback, bloqueio de `localhost`/`*.localhost`/`*.local`, loopback IPv4/IPv6/`::ffff:127.0.0.1`, RFC1918 (`10/8`, `172.16/12`, `192.168/16`), link-local/IMDS (`169.254/16`, `fe80::/10`), unspecified, broadcast. Mensagem de erro unificada anti-enumeration. (10) Worker tem fail-fast de `IWebhookSecretProtector` no startup (`Worker/Program.cs:53-56`) em scope anonimo antes de `host.Run()` — se `PaymentHub:WebhookSecretEncryptionKey` ausente, falha cedo com `InvalidOperationException` em vez de esperar o primeiro dispatch.
- Evidencia: 9 sub-slices (7-A.1 a 7-A.9) implementados entre 2026-06-25 e 2026-06-26. Suite previa: 178 testes. Suite nova: **281 testes** (+103). ADRs `ADR-0007-webhook-secret-protection.md` e `ADR-0010-real-outbox-dispatcher-location.md` consolidadas. Specs `007-inbox-outbox-workers.md` e `011-security-and-compliance.md` atualizadas. Reports em `docs/audits/slice-7a-real-outbox-dispatcher-report-2026-06-26.md` (consolidado), `slice-7a5-webhook-url-ssrf-report-2026-06-26.md` (SSRF) e `slice-7a6-worker-appsettings-webhook-secret-key-report-2026-06-26.md` (configuracao). Build limpo, `scripts/agent-architecture-check.sh` passou, `git diff --check` limpo. Phase 7 alcancou 0 gaps P1 proprios em 2026-06-26.
- Impacto para proximos agentes: (a) Ao adicionar novo consumer HTTP para `OutboxEvent` (ex.: webhook de retry para parceiros, dispatch para sistema externo), copiar o padrao: dispatcher em `Infrastructure.Postgres.Webhooks/` (ou `Infrastructure.Postgres.<Dominio>/`), lifetime Scoped, `IHttpClientFactory` nomeado, DI centralizado em `AddPaymentHubPostgres`, tenant guard via `GetByTenantAndIdAsync`, `IOutboxEventStore` para testabilidade. (b) Ao adicionar nova categoria de erro ao dispatcher, estender `WebhookDispatcherCategory` e atualizar testes — nunca persistir `ex.Message`. (c) Ao adicionar novo tipo de URL que sai do Payment Hub para consumidores externos (webhook, redirect URL, postback), replicar a validacao HTTPS/SSRF usando `WebhookUrlValidator` (ou extrair helper equivalente para o novo dominio). (d) Ao adicionar nova chave criptografica obrigatoria em producao, replicar o fail-fast no startup: resolver o protector em scope anonimo antes de `host.Run()`, com placeholder vazio em `appsettings.json` e valor fake em `appsettings.Development.json`. (e) Helpers puros `internal static` + `InternalsVisibleTo("PaymentHub.UnitTests")` no `.csproj` continuam sendo o caminho para testabilidade sem inflar a API publica (ja usado em `WebhookUrlValidator`; ver entrada de 2026-06-26 do Slice 7-A.5). (f) Ao migrar para multi-instancia (Phase 7 multi-instance, fora do MVP), adicionar `FOR UPDATE SKIP LOCKED` em `OutboxRepository` e sweep automatico de eventos `Processing` orfaos. Documentado em `docs/specs/007-inbox-outbox-workers.md` e ADR-0010.

### 2026-06-25 - Webhook secret protection usa AES reversivel com prefixo de proposito, parametrizacao explicita e chave separada de credenciais

- Contexto: O Slice 6-C enderecou o gap P1-5 (`ApplicationClient.WebhookSecret` persistido em texto claro). O segredo precisa ser recuperado em memoria para assinar HMAC de webhooks internos, entao hash unidirecional nao serve. A coluna ja existia (`varchar(500) nullable`) e nenhum dado produtivo estava persistido, entao migration estrutural nao era justificada. Credenciais de provider ja usavam `ICredentialProtector`/`AesCredentialProtector` com chave separada (`PaymentHub:CredentialEncryptionKey`), entao o padrao do projeto para "segredo reversivel em repouso" ja estava definido.
- Decisão: (1) Interface `IWebhookSecretProtector` em `PaymentHub.Application/Abstractions/Security/ICrypto.cs` com `Protect`/`Unprotect` — mesma familia de `ICredentialProtector`. (2) Implementacao `AesWebhookSecretProtector` em `Infrastructure.Postgres/Security/CryptoServices.cs` com AES-CBC + IV randomico de 16 bytes + prefixo de proposito `PaymentHub.ApplicationClient.WebhookSecret.v1` verificado em tempo constante via `CryptographicOperations.FixedTimeEquals`. (3) Parametro nomeado `protectedWebhookSecret` no construtor e em `UpdateWebhook(...)` da entidade `ApplicationClient` — o dominio nao ve raw, so blob ja protegido. (4) Chave separada: `PaymentHub:WebhookSecretEncryptionKey` (independente de `CredentialEncryptionKey`) para que a rotacao tenha blast radius menor. (5) Sem `IDataProtectionProvider` (Data Protection do ASP.NET Core) — o projeto ja optou por AES proprio em `AesCredentialProtector`, e Data Protection exigiria injecao de dependencia web-framework-specific no `PaymentHub.Application`, quebrando Clean Architecture. (6) `HttpApplicationWebhookDispatcher.DispatchAsync` chama `Unprotect` imediatamente antes de `Sign`; se falhar, loga e **nao** envia HTTP request — preferencia explicita por "abortar cedo" em vez de "enviar sem assinatura".
- Evidencia: `src/PaymentHub.Application/Abstractions/Security/ICrypto.cs:15`, `src/PaymentHub.Infrastructure.Postgres/Security/CryptoServices.cs:80-150`, `src/PaymentHub.Domain/Entities/ApplicationClient.cs:33-58`, `src/PaymentHub.Api/Webhooks/HttpApplicationWebhookDispatcher.cs:52-67`. 27 testes novos (`AesWebhookSecretProtectorTests`, `RegisterApplicationClientHandlerTests`, `DevelopmentDataSeederTests`, `HttpApplicationWebhookDispatcherTests`). Build limpo, 133 testes passando. Relatorio em `docs/audits/slice-6c-webhook-secret-protection-report-2026-06-25.md`.
- Impacto para proximos agentes: Ao proteger um segredo reversivel em repouso no projeto, copiar o padrao: `I<Suffix>Protector` em `Application.Abstractions.Security`, implementacao `Aes<Suffix>Protector` em `Infrastructure.Postgres.Security`, chave separada em `PaymentHubOptions`, registro singleton em `PostgresServiceCollectionExtensions`, parametro nomeado `protected*` na entidade, log de erro sem expor valor. Prefixar o payload com proposito (`<Purpose>.v<N>`) e verificar via `CryptographicOperations.FixedTimeEquals` para rejeitar blobs de outros sistemas ou versoes anteriores. Nao usar `IDataProtectionProvider` para manter consistencia com `ICredentialProtector`. Em testes, usar `FakeWebhookSecretProtector` com marker previsivel para que `persisted.X.StartsWith(marker)` seja assercao suficiente. Se o consumidor precisa do raw em runtime, o caminho "abortar cedo quando `Unprotect` falha" eh preferivel a "enviar sem assinatura" — o dispatcher do Slice 6-C exemplifica.

### 2026-06-24 - OpenCode harness deve separar config estrita, agentes curtos e skills sob demanda

- Contexto: A evolucao do harness OpenCode precisava adicionar agentes primarios/subagents, skills locais, permissoes seguras e scripts de verificacao sem quebrar o schema estrito do OpenCode nem inflar `AGENTS.md`.
- Decisão: Manter `.opencode/opencode.json` apenas estrutural (`skills`, `permission`, `watcher`, `tool_output`, `compaction` etc.), usar `.opencode/agents/*.md` como fonte de verdade de comportamento/metadados/permissoes dos agentes, mover fluxos para `docs/harness/opencode.md` e `agent-operating-model.md`, e criar skills pequenas em `.opencode/skills/*/SKILL.md`.
- Evidência: `scripts/agent-docs-check.sh` valida agentes, skills, ausencia de `agents`/`notes` top-level, ausencia de duplicacao via `agent`/`prompt` no JSON, reviewers sem edicao/subagents e `implementer` sem `edit: '*': allow`; `scripts/agent-architecture-check.sh` valida limites de camadas; `scripts/agent-smoke.sh` combina checks locais sem exigir provider real.
- Impacto para próximos agentes: Ao evoluir OpenCode, consulte `https://opencode.ai/config.json` antes de adicionar chaves, nao duplique agentes no JSON, mantenha reviewers com `edit: deny` e `task: deny`, registre skills no indice e reinicie o OpenCode apos mudar config/agentes/skills.

### 2026-06-23 - Agent-readiness funciona melhor com contexto progressivo e verificacao mecanica

- Contexto: A configuracao de Copilot/Codex precisava cobrir auditoria, instrucoes, prompts, agentes, skills, docs de uso, estado e verificacao sem transformar `copilot-instructions.md` em uma enciclopedia.
- Decisão: Manter `AGENTS.md` como indice operacional, `.github/copilot-instructions.md` curto, regras especificas em `.github/instructions/`, rotinas em `.github/prompts/` e `.github/skills/`, personas em `.github/agents/`, governanca em `docs/ai/`, estado em `feature_list.md`/`agent-progress.md` e verificacao em `scripts/agent-verify.sh`.
- Evidência: `scripts/agent-verify.sh` validou arquivos, frontmatter, ausencia de `.env` real e `docker compose config`; `dotnet restore`, `dotnet build` e `dotnet test` passaram, com 106 testes unitarios e projeto de integracao ainda sem testes descobertos.
- Impacto para próximos agentes: Ao adicionar novas regras para agentes, prefira contexto progressivo e enforcement por script/check/CI. Nao inflar `copilot-instructions.md`; coloque regras por area em `.github/instructions/` e processos repetiveis em prompts ou skills. Se novas lacunas de validacao surgirem, atualize `docs/ai/validation-checklist.md` e `scripts/agent-verify.sh`.

### 2026-06-18 - Bootstrap deve ser policy-driven, opt-in e nunca loggar credenciais

- Contexto: Antes do Slice 6-D, nao havia codigo de seed automatico e nenhuma politica explicita para criacao de tenant/application inicial. O codigo tinha `TenantsController` e `ApplicationsController` que o middleware bloqueava (requeriam API Key), criando um deadlock operacional. A auditoria apontou isso como gap P1-3.
- Decisão: Centralizar a politica em `IBootstrapPolicy` (Application) + `BootstrapOptions` (strongly-typed) + `HostBootstrapPolicy` (Api, le `IHostEnvironment`); seedor `IDevelopmentDataSeeder` cria apenas `tenant` + `application` (nunca API Key, provider account, secret ou credencial). Defaults de `appsettings.json` sao `Enabled=false`, `SeedDevelopmentData=false`, `AllowProductionBootstrap=false` (fail-safe). `appsettings.Development.json` liga `Enabled=true` e `SeedDevelopmentData=true` mas mantem `AllowProductionBootstrap=false`. Idempotencia via `ITenantRepository.GetBySlugAsync` e `IApplicationClientRepository.GetByTenantAndNameAsync` (novos metodos adicionados no slice). `Production` exige opt-in explicito para criar qualquer dado.
- Evidência: 12 testes em `tests/PaymentHub.UnitTests/Api/HostBootstrapPolicyTests.cs` e 9 testes em `tests/PaymentHub.UnitTests/Application/DevelopmentDataSeederTests.cs`. Build limpo, 106 testes passando. Relatorio em `docs/audits/slice-6d-bootstrap-admin-seed-policy-report-2026-06-18.md`.
- Impacto para próximos agentes: Sempre que adicionar um caminho automatico de criacao de dados iniciais (seed, migration data, fixture), passar por `IBootstrapPolicy`. Nao criar API Key, secret ou credencial via seedor — primeiro porque logging seguro e obrigatorio (regra de `docs/harness/security.md`), segundo porque a exibicao one-time de API Key depende do response do endpoint HTTP. Para resolver o deadlock residual de "como obter a primeira API Key em producao", alinhar com o painel admin (Phase 5) ou um canal externo documentado; nao introduzir bypass de `ApiKeyAuthenticationMiddleware`. Ao auditar `appsettings*.json`, conferir que a secao `Bootstrap` tem defaults seguros.

### 2026-06-18 - Em projetos .NET 10 com `Microsoft.NET.Sdk`, `IOptions<>` exige PackageReference explicita

- Contexto: Ao adicionar `IDevelopmentDataSeeder` em `PaymentHub.Application` (que usa `Microsoft.NET.Sdk` puro, nao `Microsoft.NET.Sdk.Web`), a primeira compilacao falhou em `IOptions<BootstrapOptions>` e `ILogger<>` porque o shared framework `Microsoft.AspNetCore.App` nao e transitive em projetos nao-Web. Os projetos `PaymentHub.Infrastructure.Postgres` e `PaymentHub.Api` ja tinham `IOptions<>` indiretamente (via SDK Web + providers) e funcionavam.
- Decisão: Adicionar `Microsoft.Extensions.Logging.Abstractions` 10.0.0 e `Microsoft.Extensions.Options` 10.0.0 como `PackageReference` no `PaymentHub.Application.csproj`. Manter as versoes alinhadas com a versao do .NET (10.0.x) para evitar conflitos de runtime.
- Evidência: Build do `PaymentHub.Application` falhou com `error CS0246: The type or namespace name 'IOptions<>' could not be found` ate a adicao dos packages; apos, build limpo.
- Impacto para próximos agentes: Ao adicionar uma classe Application que dependa de `IOptions<T>`, `ILogger<T>` ou `IHostEnvironment` (apesar deste ultimo exigir Microsoft.Extensions.Hosting), adicionar explicitamente os packages `Microsoft.Extensions.Options` e/ou `Microsoft.Extensions.Logging.Abstractions` no `.csproj` Application. Nao assumir que o shared framework Web cobre. A mesma logica vale para qualquer projeto que use `Microsoft.NET.Sdk` puro (bibliotecas).

### 2026-06-17 - Enforcement de status ativo deve acontecer no middleware, nao no handler

- Contexto: Gap P1-1 da auditoria de 2026-06-17 apontava que `Tenant`/`ApplicationClient` inativos continuavam consumindo a API quando a API Key estava `Active`. A spec `002-multitenancy-and-authentication.md` exige "Tenant suspenso/desativado ou application inativa deve impedir criacao de checkout".
- Decisão: Centralizar o enforcement em `ApiKeyAuthenticationMiddleware` (ponto de entrada de toda requisicao autenticada). Middleware consulta `ITenantRepository.GetByIdAsync` e `IApplicationClientRepository.GetByTenantAndIdAsync` apos validar hash + escopo da API Key, e retorna `403 Forbidden` quando `Status != Active`. Tenant/application inexistentes continuam retornando `401 Unauthorized` para nao vazar existencia. Handlers de aplicacao (ex.: `CreateCheckoutHandler`) permanecem presumindo `ITenantContext` valido.
- Evidência: 11 testes em `tests/PaymentHub.UnitTests/Api/ApiKeyAuthenticationMiddlewareTests.cs` cobrindo os 7 cenarios do slice + 4 cenarios correlatos (tenant/application inexistente, anti-leak 401, anti-leak 403). Build limpo e 70 testes passando. Relatorio em `docs/audits/slice-6a-active-status-enforcement-report-2026-06-17.md`.
- Impacto para próximos agentes: Nao duplicar verificacao de status em `CreateCheckoutHandler` ou outros handlers — o middleware ja garante. Para adicionar novos endpoints autenticados, manter o middleware como unico gate. Ao introduzir novos status (ex.: `Provisioning`, `Blocked`), estender a verificacao no middleware em vez de espalhar por handlers. Specs `002`, `011` e o report do slice sao referencia obrigatoria antes de mexer no `ApiKeyAuthenticationMiddleware`.

### 2026-06-17 - Testes de middleware exigem MemoryStream explicito em `DefaultHttpContext`

- Contexto: Ao adicionar testes que validam o body JSON de `WriteAsJsonAsync` em `DefaultHttpContext`, a primeira execucao falhou com `JsonReaderException: The input does not contain any JSON tokens` mesmo com `ctx.Response.Body.Position = 0`. Investigacao mostrou que `DefaultHttpContext.Response.Body` vem como `Stream.Null`, descartando todo o write.
- Decisão: Setar `ctx.Response.Body = new MemoryStream()` em qualquer helper de teste que use `DefaultHttpContext` e precise ler a resposta. Tambem nao comparar `ContentType` exato porque ASP.NET Core 10 adiciona `; charset=utf-8`; preferir `Should().StartWith("application/json")`.
- Evidência: Testes `RejectionResponses_ShouldNotLeakApiKeyOrEntityStatus` e `UnauthorizedResponses_ShouldNotLeakApiKeyOrEntityStatus` em `ApiKeyAuthenticationMiddlewareTests.cs` so passaram apos o ajuste.
- Impacto para próximos agentes: Antes de adicionar testes que inspecionam `ctx.Response.Body` ou `ContentType`, garantir `ctx.Response.Body = new MemoryStream()` no setup. Para comparar tipos MIME, usar `StartWith` em vez de `Be`.

### 2026-06-17 - `ApplicationClient` precisa de `Suspend()`/`Activate()` para paridade com `Tenant`

- Contexto: A auditoria documentou 3 valores para `ApplicationStatus` (`Active`, `Suspended`, `Disabled`), assim como em `TenantStatus`. Porem, so `Tenant` tinha `Suspend()`/`Activate()`; `ApplicationClient` nao. Para testar enforcement sem reflexao, era necessario um caminho publico para alterar `Status`.
- Decisão: Adicionar `ApplicationClient.Suspend()` e `ApplicationClient.Activate()` no dominio, mantendo `Status` como `private set`. Mudanca puramente comportamental, sem migration.
- Evidência: `src/PaymentHub.Domain/Entities/ApplicationClient.cs:46-58` (metodos adicionados). Testes em `ApiKeyAuthenticationMiddlewareTests.cs` instanciam `ApplicationClient` e chamam `Suspend()` para o caso inativo.
- Impacto para próximos agentes: Sempre que adicionar um enum de status a uma entidade, ja incluir os metodos de transicao publica necessarios para o dominio (`Suspend`/`Activate`/`Archive` etc.) — sem isso, testes e futuros fluxos administrativos acabam usando reflexao ou alterando colunas via SQL direto. Manter `Status` `private set` para preservar invariantes; expor apenas transicoes explicitas.

### 2026-06-17 - `docs/specs` passa a ser fonte de verdade de contratos

- Contexto: O projeto ja tinha docs explicativas em `docs/architecture`, `docs/api` e `docs/database`,
  mas faltava uma camada formal de specs para guiar implementacoes por contrato.
- Decisão: Criar `docs/specs/` como fonte de verdade para escopo, dominio, autenticacao,
  checkout, webhooks, Inbox/Outbox, adapters, API, banco, seguranca, observabilidade,
  testes e integracao Job Search. Criar `docs/adr/` para decisoes arquiteturais aceitas.
- Evidência: Specs `000` a `014` e ADRs `ADR-0001` a `ADR-0005` foram adicionados;
  `AGENTS.md`, `README.md` e harness passaram a apontar para specs/ADRs.
- Impacto para próximos agentes: Antes de alterar codigo, leia a spec relacionada.
  Se o codigo divergir da spec, registre o gap e corrija em slice pequeno.
  Mudancas de contrato devem atualizar a spec; decisoes arquiteturais novas devem atualizar/criar ADR.

### 2026-06-17 - Gaps documentais para revisao futura

- Contexto: Durante a formalizacao das specs, foi identificado que a semantica `Created`/`Pending`
  do checkout precisava ser consolidada e que `.env.example` pode aparecer compactado em contextos raw.
- Decisão: Documentar em `docs/specs/004-payment-lifecycle.md` que `Created` e o estado interno
  antes do provider e `Pending` e o estado persistido/retornado apos `checkoutUrl`.
  Nao alterar `.env.example` nesta tarefa docs-only.
- Evidência: `Payment` inicia em `Created`; `CreateCheckoutHandler` chama `AttachProviderResult(..., PaymentStatus.Pending)` antes de salvar a resposta de sucesso.
- Impacto para próximos agentes: Auditorias futuras devem comparar codigo e specs antes de mudar comportamento; se reformatarem `.env.example`, manter apenas valores fake e nao commitar `.env` real.

### 2026-06-17 - Specs de pagamento devem ficar explícitas sobre idempotência e webhooks

- Contexto: A auditoria encontrou gaps onde o texto da spec estava correto em alto nivel,
  mas faltavam detalhes operacionais para impedir fallback silencioso de provider,
  replay com payload diferente e webhook orfao marcado como processado.
- Decisão: Checkout compara `request_hash` antes de retornar replay,
  provider explicito nunca cai para outro provider, webhooks sao parseados pelo adapter
  e pagamento inexistente vira `Failed` com `last_error`.
- Evidência: Testes unitarios cobrem conflito de idempotencia, provider explicito invalido/sem conta ativa,
  transicoes perigosas de `PaymentStatus`, parsing por adapter, webhook orfao
  e HMAC `{timestamp}.{rawBody}`.
- Impacto para próximos agentes: Ao alterar checkout ou webhook, confira specs `004`, `005`, `006`, `008`, `011` e `014`;
  preserve `eventId` como id do `OutboxEvent` e nao marque webhook sem pagamento como `Processed`.

### 2026-06-17 - Webhook interno usa eventId, HMAC com timestamp e attempts por resultado financeiro

- Contexto: A auditoria final separou tres riscos: consumidor sem chave estavel de idempotencia,
  HMAC sem contrato anti-replay completo e `PaymentAttemptStatus.Succeeded` usado para status negativos.
- Decisão: Documentar `eventId` como id estavel do `OutboxEvent`,
  assinar `{timestamp}.{rawBody}` com HMAC-SHA256 em hex lowercase
  e mapear webhooks `Rejected`, `Failed`, `Expired` e `Cancelled` para `PaymentAttemptStatus.Failed`.
- Evidência: Specs `003`, `004`, `006`, `007`, `009`, `011` e `014` foram alinhadas;
  testes de webhook cobrem `Approved`, `Rejected` e `Failed`.
- Impacto para próximos agentes: Nao confundir webhook processado com pagamento aprovado.
  Preserve `eventId` nos retries do mesmo outbox e valide HMAC sobre body bruto, sem reserializar JSON.

### 2026-06-16 - OpenCode rejeita chaves top-level `agents` e `notes`

- Contexto: A execução de `opencode` abortava porque `.opencode/opencode.json` continha as chaves top-level `agents` e `notes`.
- Decisão: Manter `opencode.json` apenas com chaves aceitas pelo schema instalado; preservar referências de agentes em `.opencode/agents/*.md` e documentar a convenção em `.opencode/README.md`.
- Evidência: A CLI reportou `Configuration is invalid ... Unrecognized keys: agents, notes`; os tipos locais do SDK indicam `agent` no singular como chave de configuração válida.
- Impacto para próximos agentes: Não adicionar `agents` ou `notes` no topo de `.opencode/opencode.json`; validar mudanças de OpenCode com `opencode debug config` ou comando equivalente.

### 2026-06-18 - Endpoints autenticados devem derivar tenant/application de ITenantContext; nunca aceitar do body

- Contexto: Slice 6-B fechou o gap P1-2. Antes do slice, `RegisterProviderAccountHandler` construia `ProviderAccount` a partir de `request.TenantId`/`request.ApplicationId` vindos do body, ignorando `ITenantContext`. Risco: cross-tenant registration por uma application autenticada. Depois do slice, `tenantId`/`applicationId` sao parametros explicitos do handler, injetados pelo controller via `ITenantContext`, e o DTO do body nao expoe mais esses campos.
- Decisao: Regra preferencial aplicada: remover `tenantId`/`applicationId` do `RegisterProviderAccountRequestDto` em vez de mante-los deprecated. ASP.NET Core ignora silenciosamente chaves extras no JSON, entao o simples fato de o DTO nao declarar a propriedade ja garante que valores divergentes nao chegam ao handler. Controller chama `ITenantContext.TenantId`/`ApplicationId` dentro de `try/catch (InvalidOperationException) => Unauthorized(...)`. Handler rejeita `Guid.Empty` com `InvalidOperationException` e nao persiste quando o caller nao fornece IDs validos. Padrao igual ao `CheckoutsController` (ja usa `ITenantContext`).
- Evidencia: 15 testes em `tests/PaymentHub.UnitTests/Application/RegisterProviderAccountHandlerTests.cs` (10) e `tests/PaymentHub.UnitTests/Api/ProviderAccountsControllerTests.cs` (5). Verificacoes: tipo do DTO nao expoe `TenantId`/`ApplicationId` (compile-time via `Type.GetProperty`); tipo da resposta nao expoe `ApiKey`/`Secret`/`EncryptedCredentials`; handler usa IDs do caller; body com campos extras e divergentes nao afeta operacao; contexto ausente retorna 401 sem chamar handler; validacao falha retorna 400 sem consultar contexto. Build limpo e 85 testes passando. Report em `docs/audits/slice-6b-provider-account-authenticated-context-report-2026-06-18.md`.
- Impacto para próximos agentes: Sempre que criar/alterar um endpoint autenticado server-to-server, seguir o padrao: (1) DTO de request nao deve conter `tenantId`/`applicationId`; (2) controller injeta `ITenantContext`, le os valores via propriedade (com `try/catch` em `InvalidOperationException` retornando 401), passa-os explicitamente ao handler; (3) handler confia nos parametros explicitos e rejeita `Guid.Empty`. Isso evita o vetor de cross-tenant/cross-application registration e impede que o body seja usado como vetor de bypass. `ITenantContext` e a fonte unica de tenant/application em fluxos autenticados; nao criar mecanismo paralelo. Ao auditar novos endpoints, conferir `009-api-contracts.md` e `002-multitenancy-and-authentication.md` para garantir que a regra esteja documentada.

### 2026-06-16 - Scaffold do Payment Gateway MVP criado com .NET 10 + Clean Architecture

- Contexto: Tarefa inicial para criar a base multitenant do gateway, cobrindo Domain/Application/Infrastructure/Api/Worker, Inbox/Outbox, idempotência e adapter `FakePaymentProvider`.
- Decisão: Adotar `net10.0` em todos os projetos, EF Core 10 + Npgsql 10,
  Swashbuckle 6.8.1 (compatível com `Microsoft.OpenApi 1.6.22`),
  `FluentValidation 11.11.0`, Serilog, override explícito de `System.Security.Cryptography.Xml 10.0.9`
  para silenciar vulnerabilidade transitiva e migrations geradas via `dotnet-ef 9.0.2`
  (rodar com `DOTNET_ROOT=/usr/lib/dotnet`).
- Evidência: `dotnet build PaymentHub.slnx` resulta em 0 erros / 0 warnings em 9 projetos; `dotnet test` executa 49 testes unitários; `docker compose config` valida o compose.
- Impacto para próximos agentes: A migration inicial
  (`src/PaymentHub.Infrastructure.Postgres/Migrations/*_InitialSchema.cs`)
  deve ser aplicada com `dotnet ef database update` antes de subir a API.
  Não usar `Microsoft.OpenApi 2.0.0` até que Swashbuckle atualize para acomodar o novo namespace;
  até lá mantenha Swashbuckle 6.8.x.
  Ao rodar `dotnet ef` em ambientes com runtimes múltiplos, defina `DOTNET_ROOT` antes do comando.

### 2026-06-16 - `Money` exige ValueConverter para Postgres

- Contexto: Ao rodar `dotnet ef migrations add InitialSchema` o tooling reportou que `Payment.Amount` (tipo `Money`) não pode ser mapeado.
- Decisão: Criar `MoneyToLongConverter` (em `PaymentHub.Infrastructure.Postgres.Configurations`)
  para mapear `Money` para `long` (valor em centavos)
  e configurar a coluna `amount_in_cents` no `PaymentConfiguration`.
- Evidência: Migration gerada com sucesso; testes unitários de `Payment` continuam passando usando `Money.Of(...)`.
- Impacto para próximos agentes: Sempre que criar um value object novo, adicionar o conversor em `Configurations/`
  para que o EF Core consiga mapear.
  Manter `Money.Of` como porta de entrada para evitar criar instâncias inválidas.

### 2026-06-16 - `dotnet new sln` gera `slnx` no SDK 10

- Contexto: O comando `dotnet new sln` no SDK 10.0.109 cria `PaymentHub.slnx` (XML)
  em vez de `.sln` legado, e a maioria das referências esperava `.sln`.
- Decisão: Manter `PaymentHub.slnx` na raiz e usar `dotnet build PaymentHub.slnx`
  / `dotnet test` (sem args) para descobrir a solução.
  Em pipelines legadas, executar `dotnet sln migrate` para gerar `.sln` se necessário.
- Evidência: `dotnet build PaymentHub.slnx` resolve 9 projetos; `dotnet test` descobre automaticamente o projeto de teste.
- Impacto para próximos agentes: Documentar nos runbooks a referência `slnx` e atualizar scripts CI para usar `slnx` (ou rodar `dotnet sln migrate` antes).

### 2026-06-16 - Workers e API compartilham DbContext, mas usam serviços diferentes

- Contexto: API e Worker precisam consultar/alterar o mesmo banco, mas cada um deve expor apenas os serviços necessários.
- Decisão: API registra `HttpApplicationWebhookDispatcher` (HttpClient real com HMAC);
  Worker registra `NoopApplicationWebhookDispatcher` que apenas loga o evento.
  A separação por processo impede vazamento de dispatcher HTTP em ambiente que não deve entregar webhooks
  (ex.: worker de leitura).
- Evidência: `Program.cs` do Worker injeta `NoopApplicationWebhookDispatcher` e usa `services.AddHttpClient` apenas para o cliente nomeado; `Program.cs` da API injeta `HttpApplicationWebhookDispatcher`.
- Impacto para próximos agentes: Para evoluir para um serviço dedicado de dispatch,
  criar novo projeto `PaymentHub.Dispatcher` reaproveitando `IApplicationWebhookDispatcher`.
  Não tentar acoplar Worker e API no mesmo processo.
