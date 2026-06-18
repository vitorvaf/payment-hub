# Harness Learnings

Este arquivo registra aprendizados técnicos do projeto que devem orientar futuros agentes.

## Formato

### YYYY-MM-DD - Título

- Contexto:
- Decisão:
- Evidência:
- Impacto para próximos agentes:

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
