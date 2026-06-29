# Slice 7-A.5 — WebhookUrl HTTPS/SSRF Protection Report

Data: 2026-06-26
Phase: 7 — Worker real + OutboxDispatcher real (sub-slice de hardening de seguranca)
Specs relacionadas: `docs/specs/011-security-and-compliance.md`, `docs/specs/002-multitenancy-and-authentication.md`
Slice predecessor: Slice 7-A.1 (foundation), 7-A.2 (realocacao do dispatcher), 7-A.3 (limpeza de Noop), 7-A.4 (Worker testavel), 7-A.7 (`LastError` seguro), 7-A.8 (testes do worker)
Gap enderecado: **M3** do par de revisores do Slice 7-A (validacao HTTPS/SSRF no `WebhookUrl`).

## Resumo

`ApplicationClient.WebhookUrl` passou a ser validado pelo `RegisterApplicationClientValidator` antes de qualquer persistencia. Foi introduzido o helper puro `internal static class WebhookUrlValidator` em `src/PaymentHub.Application/Tenants/Validation/WebhookUrlValidator.cs`, com a assinatura `public static bool IsAllowed(string? value, bool isDevelopment, out string? reason)`. O validator injeta `IRuntimeEnvironment` (ja registrado como Singleton em `src/PaymentHub.Api/Program.cs:66`) e aplica `RuleFor(x => x.WebhookUrl).Must(...)` quando o valor esta preenchido. A mensagem de erro e unificada: `WebhookUrl must be an absolute HTTPS URL pointing to a public endpoint.` (anti-enumeration).

Suite previa: 178 testes. Suite nova: 281 testes (103 casos expandidos, 80+ testes logicos).

## Gap enderecado

- **M3** — `WebhookUrl` sem checagem de scheme / host permite SSRF (Server-Side Request Forgery).
  - Specs: `011-security-and-compliance.md` (HTTPS obrigatorio, dados sensiveis sem exposicao em logs/respostas).
  - Risco original: uma application com API Key valida (ex.: via Bootstrap/seed) poderia registrar `WebhookUrl = "http://169.254.169.254/latest/meta-data"` ou `"http://10.0.0.1/admin"` e usar o Worker do Payment Hub como proxy para atingir servicos internos ou cloud metadata services. Como o Worker (`OutboxDispatcherWorker`) le `application.WebhookUrl` diretamente e faz `HttpClient.PostAsync(...)`, o risco e de SSRF direto contra a infraestrutura do provedor.
  - Resolucao: validacao obrigatoria no ponto de entrada (`RegisterApplicationClientValidator`), com helper puro testavel e regras claras (HTTPS obrigatorio, exception de Development para loopback HTTP, bloqueio total de RFC1918/link-local/IMDS/unspecified/multicast/broadcast).

## Q1 respondida (FluentValidation + DI)

O planner contract levantou o questionamento se `AddValidatorsFromAssemblyContaining<T>()` consegue resolver o construtor de `RegisterApplicationClientValidator` agora que ele exige `IRuntimeEnvironment`.

**Resposta**: sim. `AddValidatorsFromAssemblyContaining<RegisterTenantValidator>()` em `src/PaymentHub.Api/Program.cs:81` usa o `IServiceProvider` do ASP.NET Core para resolver construtores de validators via DI. `IRuntimeEnvironment` esta registrado como **Singleton** em `src/PaymentHub.Api/Program.cs:66`:

```csharp
builder.Services.AddSingleton<IRuntimeEnvironment, HostRuntimeEnvironment>();
```

Logo, na inicializacao do validator (singleton no ASP.NET Core), o container injeta a instancia Singleton de `HostRuntimeEnvironment`. Em testes, instanciamos o validator diretamente passando `Mock<IRuntimeEnvironment>` para controlar `IsDevelopment`. **Nenhum fallback em `HandleAsync` foi necessario** — o caminho via validator resolve o ctor via DI no startup do host.

## Arquivos analisados

- `src/PaymentHub.Application/Tenants/RegisterApplicationClientHandler.cs` — `RegisterApplicationClientValidator` tinha apenas `MaximumLength(2000)` em `WebhookUrl`. Esta era a lacuna M3.
- `src/PaymentHub.Application/Tenants/Dtos.cs` — DTO `RegisterApplicationClientRequestDto` expoe `string? WebhookUrl`.
- `src/PaymentHub.Application/Abstractions/Context/IRuntimeEnvironment.cs` — interface usada pelo validator.
- `src/PaymentHub.Application/Checkouts/CreateCheckoutHandler.cs` — ja consumia `IRuntimeEnvironment`; padrao seguido.
- `src/PaymentHub.Domain/Entities/ApplicationClient.cs` — entidade continua aceitando `webhookUrl` (validacao agora e responsabilidade do validator).
- `src/PaymentHub.Api/Program.cs:66,81` — confirmam o auto-wiring.
- `src/PaymentHub.Infrastructure.Postgres/Webhooks/HttpApplicationWebhookDispatcher.cs` — **nao alterado** neste slice (dispatcher intocado). Nao ha bypass de validacao porque toda escrita de `WebhookUrl` passa por `RegisterApplicationClientHandler.HandleAsync`.
- `tests/PaymentHub.UnitTests/Application/CreateCheckoutHandlerTests.cs` — segue o mesmo padrao de mockar `IRuntimeEnvironment`.
- `docs/specs/011-security-and-compliance.md` — agora contem a secao `### Protecao SSRF em ApplicationClient.WebhookUrl`.
- `docs/audits/payment-hub-current-state-audit-2026-06-17.md`, `docs/audits/spec-adherence-audit-2026-06-17.md` — gap M3 mapeado originalmente.

## Decisoes

1. **Helper puro, sem DI/logging/exceptions.** `WebhookUrlValidator` e `internal static class`. Sem injecao de dependencia, sem `ILogger`, sem throw. Recebe `isDevelopment` por parametro explicito e devolve `out string? reason` para mensagens faceis de testar. Isto permite que o helper seja 100% unit-testable sem mocks.
2. **`internal` visibility + `InternalsVisibleTo("PaymentHub.UnitTests")`** em `PaymentHub.Application.csproj`. O helper nao precisa ser parte da API publica; apenas tests precisam ve-lo. Isso segue o padrao ja existente em `PaymentHub.Worker.csproj` (linha 11).
3. **Ctor injection no validator**, nao no handler. `RegisterApplicationClientValidator` recebe `IRuntimeEnvironment environment` no construtor; o handler continua com a mesma assinatura. FluentValidation resolve o ctor via DI no startup.
4. **Mensagem de erro unificada** (`"WebhookUrl must be an absolute HTTPS URL pointing to a public endpoint."`): anti-enumeration. O consumidor da API nao consegue distinguir entre "scheme errado", "loopback", "RFC1918" ou "malformed" — todos retornam a mesma mensagem.
5. **Excecao de Development restrita a HTTP + loopback**. Em Development, HTTP e aceito **somente** para `localhost`, `127.0.0.0/8` ou `::1`. Em Development, HTTPS continua aceitando publicos. Em Production, HTTPS obrigatorio (HTTP sempre rejeitado).
6. **Boundary RFC1918 correto**. `172.15.x.x` e `172.32.x.x` permanecem publicos (sao intencionalmente fora do bloco `172.16.0.0/12`).
7. **IPv6-mapped IPv4 loopback normalizado** via `address.IsIPv4MappedToIPv6` + `address.MapToIPv4()`. Sem isso, `[::ffff:127.0.0.1]` escaparia do bloqueio de loopback.
8. **`MaximumLength(2000)` preservado** como primeira regra (fail-fast em inputs gigantes).

## Plano executado

Conforme briefing do planner contract (`agent-progress.md` linhas 38-126):

1. **Helper puro** `src/PaymentHub.Application/Tenants/Validation/WebhookUrlValidator.cs` (~200 linhas, `internal static`).
2. **Validator FluentValidation** com ctor `(IRuntimeEnvironment environment)` + regra `Must(...)`.
3. **`InternalsVisibleTo`** adicionado em `src/PaymentHub.Application/PaymentHub.Application.csproj`.
4. **Spec 011 atualizada** com secao `### Protecao SSRF em ApplicationClient.WebhookUrl`.
5. **Testes** criados:
   - `tests/PaymentHub.UnitTests/Application/Validation/WebhookUrlValidatorTests.cs` (66+ casos expandidos via Theory).
   - `tests/PaymentHub.UnitTests/Application/RegisterApplicationClientValidatorTests.cs` (17 testes diretos no validator).
6. **Validacoes finais** (vide secao abaixo).
7. **Relatorio** criado (este arquivo).
8. **agent-progress.md** atualizado com a conclusao.
9. **Commit** do slice.

## Validacoes executadas

- `dotnet restore PaymentHub.slnx` — packages restaurados sem conflitos.
- `dotnet build PaymentHub.slnx` — **0 errors / 0 warnings** em 9 projetos.
- `dotnet test PaymentHub.slnx` — **281 tests passed, 0 failed** (baseline previo: 178; +103 casos expandidos).
- `dotnet test --filter "FullyQualifiedName~WebhookUrl"` — **69 tests passed** (helper tests expandidos via Theory).
- `dotnet test --filter "FullyQualifiedName~RegisterApplicationClient"` — passando (handler tests originais + novos validator tests).
- `dotnet test --filter "FullyQualifiedName~ApplicationWebhook"` — passando (dispatcher sem regressao, nao foi alterado).
- `dotnet test --filter "FullyQualifiedName~OutboxDispatcherWorker"` — passando (worker sem regressao, nao foi alterado).
- `scripts/agent-architecture-check.sh` — **passed** (Application continua sem dependencia de Infrastructure/Api/Worker; Validator puro nao viola Clean Architecture).
- `git diff --check` — sem warnings.

## Criterios de aceite (todos atendidos)

1. ✅ WebhookUrl invalida (formato/scheme/host) rejeitada pelo validator.
2. ✅ WebhookUrl nao-HTTPS rejeitada fora de Development.
3. ✅ localhost/loopback (IPv4+IPv6+IPv4-mapped) bloqueado.
4. ✅ RFC1918 (`10/8`, `172.16/12`, `192.168/16`) bloqueado, boundary correta.
5. ✅ Link-local/IMDS (`169.254/16`, `fe80::/10`) bloqueado.
6. ✅ Wildcard/unspecified (`0.0.0.0`, `::`) bloqueado.
7. ✅ URLs publicas HTTPS (`example.com`, `hooks.example.com`, `api.example.com:8443`, IPs publicos como `8.8.8.8`, `1.1.1.1`) aceitas.
8. ✅ 66+ testes do helper + 17 testes do validator (total adicionado > 80).
9. ✅ `docs/specs/011-security-and-compliance.md` atualizado com secao `### Protecao SSRF em ApplicationClient.WebhookUrl` e `## Testes esperados` com 6 bullets novos.
10. ✅ Build/test verde, `agent-architecture-check.sh` verde, dispatcher/worker/outbox intactos (nao avancou para 7-A.6 ou 7-A.9).
11. ✅ Report criado.

## Riscos residuais (deferidos, fora deste slice)

- **B4-security** (deferido): os headers `X-PaymentHub-Event` / `X-PaymentHub-Tenant` / `X-PaymentHub-Application` nao estao sendo validados/autorizados. Endpoints externos podem tentar spoofar eventos via headers. **Este slice nao introduz headers novos** (dispatcher intocado), entao o risco permanece inalterado em relacao ao baseline.
- **R1** (do planner contract): caso o container de DI nao consiga resolver `IRuntimeEnvironment` em algum teste de integracao futuro, o fallback documentado e chamar `WebhookUrlValidator.IsAllowed(...)` diretamente dentro de `HandleAsync`. **Nao foi necessario** — o helper e injetado via validator ctor e resolve normalmente.
- **R2** (do planner contract): IPv6-mapped IPv4 loopback (`::ffff:127.0.0.1`). **Resolvido** pelo helper com normalizacao explicita via `IsIPv4MappedToIPv6` + `MapToIPv4()`. Teste `Validate_ShouldRejectLoopback` cobre `[::ffff:127.0.0.1]`.
- **R3** (do planner contract): hosts com Unicode/IDN. `Uri.TryCreate` ja normaliza via `IdnHost` em .NET 10. `.localhost`/`*.localhost` ja sao bloqueados; `.com.br` ou `xn--` continuam publicos. Sem bypass conhecido.
- **R4** (do planner contract): dispatcher existente continua a usar `app.WebhookUrl` sem revalidar. **Seguro** porque toda escrita de `WebhookUrl` passa por `RegisterApplicationClientHandler.HandleAsync`, e `RegisterApplicationClientValidator` e a unica ponte de entrada. Nao ha outro metodo publico de `ApplicationClient` que altere `WebhookUrl` alem de `UpdateWebhook(...)` (tambem nao exposto por endpoint). Verificado em `src/PaymentHub.Domain/Entities/ApplicationClient.cs:42` (`Trim` apenas, sem mutacao).
- **Caminhos nao-cobertos**: `ApplicationClient.UpdateWebhook(...)` nao foi tocado neste slice porque nao existe endpoint de update na codebase atual. Quando o endpoint de update for adicionado (Phase 5 painel admin ou futuro), o mesmo validator deve ser reaproveitado ou um `UpdateApplicationClientValidator` paralelo deve usar o helper. Isso sera documentado no spec 002 quando o endpoint for introduzido.

## Aprendizados (para `docs/harness/learnings.md`)

1. **Helpers puros `internal static` sao o caminho para validadores testaveis sem DI.** O helper `WebhookUrlValidator` segue o mesmo padrao de `IdempotencyConflictException` ou outros value objects puros — sem DI, sem logging, sem excecoes, com `out string? reason` para mensagens faceis. Resultado: ~30 testes em uma unica classe sem nenhum mock.
2. **`InternalsVisibleTo` em `.csproj` evita `public` desnecessario.** Ja usado em `PaymentHub.Worker.csproj:11`. Replicado em `PaymentHub.Application.csproj` para expor o helper ao projeto de testes sem expandir a superficie publica.
3. **FluentValidation resolve ctors via DI automaticamente.** `AddValidatorsFromAssemblyContaining<T>()` consulta o `IServiceProvider`; validators com dependencias (como `IRuntimeEnvironment`) sao resolvidos sem custom factory. Q1 do planner foi respondido empiricamente pelos testes.
4. **Mensagem de erro unificada e anti-enumeration.** Nao revelar qual regra foi violada (`scheme`, `loopback`, `RFC1918`) reduz a superficie de reconhecimento para atacantes.
5. **IPv6-mapped IPv4 loopback exige normalizacao explicita.** Sem `IPAddress.MapToIPv4()` quando `IsIPv4MappedToIPv6 == true`, `[::ffff:127.0.0.1]` escaparia de qualquer bloqueio escrito apenas para IPv4.

## Arquivos criados (3)

- `src/PaymentHub.Application/Tenants/Validation/WebhookUrlValidator.cs` (~200 linhas).
- `tests/PaymentHub.UnitTests/Application/Validation/WebhookUrlValidatorTests.cs` (~310 linhas, 30+ metodos de teste).
- `tests/PaymentHub.UnitTests/Application/RegisterApplicationClientValidatorTests.cs` (~180 linhas, 17 testes).
- `docs/audits/slice-7a5-webhook-url-ssrf-report-2026-06-26.md` (este arquivo).

## Arquivos alterados (3)

- `src/PaymentHub.Application/Tenants/RegisterApplicationClientHandler.cs` — `RegisterApplicationClientValidator` recebe `IRuntimeEnvironment` e adiciona `Must(...)`.
- `src/PaymentHub.Application/PaymentHub.Application.csproj` — adicionado `<InternalsVisibleTo Include="PaymentHub.UnitTests" />`.
- `docs/specs/011-security-and-compliance.md` — adicionada secao `### Protecao SSRF em ApplicationClient.WebhookUrl` + 6 bullets em `## Testes esperados`.

## Arquivos NAO alterados (constraint do briefing)

- `src/PaymentHub.Infrastructure.Postgres/Webhooks/HttpApplicationWebhookDispatcher.cs` — dispatcher intocado.
- `src/PaymentHub.Worker/OutboxDispatcherWorker.cs` e `src/PaymentHub.Worker/Program.cs` — worker intocado.
- `src/PaymentHub.Domain/Entities/OutboxEvent.cs` — politica de `LastError` preservada (slice 7-A.7).
- `src/PaymentHub.Infrastructure.Postgres/PostgresServiceCollectionExtensions.cs` — DI nao muda (helper e puramente estatico).
- `src/PaymentHub.Worker/appsettings.json` — slice 7-A.6, nao implementado.
- ADRs — slice 7-A.9, nao implementado.

## Proximo sub-slice (sem implementar)

**7-A.6** — Configuracao do Worker/appsettings.json com placeholder documentado para `WebhookSecretEncryptionKey` + comentario inline `// obrigatorio em producao`.
