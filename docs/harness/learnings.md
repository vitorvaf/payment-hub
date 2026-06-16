# Harness Learnings

Este arquivo registra aprendizados técnicos do projeto que devem orientar futuros agentes.

## Formato

### YYYY-MM-DD - Título

- Contexto:
- Decisão:
- Evidência:
- Impacto para próximos agentes:

### 2026-06-16 - OpenCode rejeita chaves top-level `agents` e `notes`

- Contexto: A execução de `opencode` abortava porque `.opencode/opencode.json` continha as chaves top-level `agents` e `notes`.
- Decisão: Manter `opencode.json` apenas com chaves aceitas pelo schema instalado; preservar referências de agentes em `.opencode/agents/*.md` e documentar a convenção em `.opencode/README.md`.
- Evidência: A CLI reportou `Configuration is invalid ... Unrecognized keys: agents, notes`; os tipos locais do SDK indicam `agent` no singular como chave de configuração válida.
- Impacto para próximos agentes: Não adicionar `agents` ou `notes` no topo de `.opencode/opencode.json`; validar mudanças de OpenCode com `opencode debug config` ou comando equivalente.

### 2026-06-16 - Scaffold do Payment Gateway MVP criado com .NET 10 + Clean Architecture

- Contexto: Tarefa inicial para criar a base multitenant do gateway, cobrindo Domain/Application/Infrastructure/Api/Worker, Inbox/Outbox, idempotência e adapter `FakePaymentProvider`.
- Decisão: Adotar `net10.0` em todos os projetos, EF Core 10 + Npgsql 10, Swashbuckle 6.8.1 (compatível com `Microsoft.OpenApi 1.6.22`), `FluentValidation 11.11.0`, Serilog, override explícito de `System.Security.Cryptography.Xml 10.0.9` para silenciar vulnerabilidade transitiva e migrations geradas via `dotnet-ef 9.0.2` (rodar com `DOTNET_ROOT=/usr/lib/dotnet`).
- Evidência: `dotnet build PaymentHub.slnx` resulta em 0 erros / 0 warnings em 9 projetos; `dotnet test` executa 49 testes unitários; `docker compose config` valida o compose.
- Impacto para próximos agentes: A migration inicial (`src/PaymentHub.Infrastructure.Postgres/Migrations/*_InitialSchema.cs`) deve ser aplicada com `dotnet ef database update` antes de subir a API. Não usar `Microsoft.OpenApi 2.0.0` até que Swashbuckle atualize para acomodar o novo namespace; até lá mantenha Swashbuckle 6.8.x. Ao rodar `dotnet ef` em ambientes com runtimes múltiplos, defina `DOTNET_ROOT` antes do comando.

### 2026-06-16 - `Money` exige ValueConverter para Postgres

- Contexto: Ao rodar `dotnet ef migrations add InitialSchema` o tooling reportou que `Payment.Amount` (tipo `Money`) não pode ser mapeado.
- Decisão: Criar `MoneyToLongConverter` (em `PaymentHub.Infrastructure.Postgres.Configurations`) para mapear `Money` para `long` (valor em centavos) e configurar a coluna `amount_in_cents` no `PaymentConfiguration`.
- Evidência: Migration gerada com sucesso; testes unitários de `Payment` continuam passando usando `Money.Of(...)`.
- Impacto para próximos agentes: Sempre que criar um value object novo, adicionar o conversor em `Configurations/` para que o EF Core consiga mapear. Manter `Money.Of` como porta de entrada para evitar criar instâncias inválidas.

### 2026-06-16 - `dotnet new sln` gera `slnx` no SDK 10

- Contexto: O comando `dotnet new sln` no SDK 10.0.109 cria `PaymentHub.slnx` (XML) em vez de `.sln` legado, e a maioria das referências esperava `.sln`.
- Decisão: Manter `PaymentHub.slnx` na raiz e usar `dotnet build PaymentHub.slnx` / `dotnet test` (sem args) para descobrir a solução. Em pipelines legadas, executar `dotnet sln migrate` para gerar `.sln` se necessário.
- Evidência: `dotnet build PaymentHub.slnx` resolve 9 projetos; `dotnet test` descobre automaticamente o projeto de teste.
- Impacto para próximos agentes: Documentar nos runbooks a referência `slnx` e atualizar scripts CI para usar `slnx` (ou rodar `dotnet sln migrate` antes).

### 2026-06-16 - Workers e API compartilham DbContext, mas usam serviços diferentes

- Contexto: API e Worker precisam consultar/alterar o mesmo banco, mas cada um deve expor apenas os serviços necessários.
- Decisão: API registra `HttpApplicationWebhookDispatcher` (HttpClient real com HMAC); Worker registra `NoopApplicationWebhookDispatcher` que apenas loga o evento. A separação por processo impede vazamento de dispatcher HTTP em ambiente que não deve entregar webhooks (ex.: worker de leitura).
- Evidência: `Program.cs` do Worker injeta `NoopApplicationWebhookDispatcher` e usa `services.AddHttpClient` apenas para o cliente nomeado; `Program.cs` da API injeta `HttpApplicationWebhookDispatcher`.
- Impacto para próximos agentes: Para evoluir para um serviço dedicado de dispatch, criar novo projeto `PaymentHub.Dispatcher` reaproveitando `IApplicationWebhookDispatcher`. Não tentar acoplar Worker e API no mesmo processo.
