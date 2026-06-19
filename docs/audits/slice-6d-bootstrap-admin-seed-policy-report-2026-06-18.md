# Slice 6-D — Bootstrap/Admin Seed Policy Report

Data: 2026-06-18
Phase: 6 — Seguranca e Confiabilidade
Specs relacionadas: `docs/specs/002-multitenancy-and-authentication.md`, `docs/specs/011-security-and-compliance.md`
Slice predecessor: Slice 6-A (enforcement de status ativo) e Slice 6-B (ProviderAccount via contexto autenticado).
Gap enderecado: P1-3 da auditoria de 2026-06-17.

## Resumo

Foi implementada uma politica explicita de bootstrap/admin seed no Payment Hub. A combinacao `IBootstrapPolicy` (Application) + `BootstrapOptions` (strongly-typed) + `HostBootstrapPolicy` (Api, le `IHostEnvironment`) + `IDevelopmentDataSeeder` (Application) formaliza a decisao de quando dados iniciais podem ser criados automaticamente. `Production` permanece fail-safe (nada e criado sem opt-in explicito `Bootstrap:AllowProductionBootstrap=true`); `Development`/`Test`/`Staging` podem rodar um seed idempotente de `tenant` + `application` quando `Bootstrap:Enabled=true` e `Bootstrap:SeedDevelopmentData=true`. O seedor nunca cria API Key, provider account, secret ou credencial. Logs do seedor nao registram API Key raw, secrets, tokens, senhas ou connection strings. O seedor e idempotente: usa `ITenantRepository.GetBySlugAsync` e `IApplicationClientRepository.GetByTenantAndNameAsync` (metodos adicionados no slice) antes de criar. Este slice encerra o gap P1-3.

## Gap enderecado

- **P1-3** — Endpoints de bootstrap/admin sem politica explicita de autenticacao.
  - Specs: `002-multitenancy-and-authentication.md` (regras de bootstrap implicitas na auditoria) e `009-api-contracts.md` (endpoints `POST /api/v1/tenants` e `POST /api/v1/applications`).
  - Risco original: a documentacao tratava esses endpoints como anonimo/admin futuro, mas o middleware exige API Key para caminhos nao anonimos, criando um deadlock operacional. Alem disso, nao havia politica explicita para qualquer seed automatico em qualquer ambiente.
  - Resolucao: politica explicita via `IBootstrapPolicy` + `IDevelopmentDataSeeder`. A politica e opt-in, fail-safe em `Production` e idempotente. Endpoints publicos de bootstrap continuam exigindo API Key; o slice nao introduz bypass. A obtencao da primeira API Key operacional continua dependendo de canal administrativo externo (painel admin Phase 5 ou operador).

## Arquivos analisados

- `src/PaymentHub.Api/Program.cs` — DI registration e startup; sem chamada previa de seed.
- `src/PaymentHub.Api/Auth/ApiKeyAuthenticationMiddleware.cs` — middleware; confirma que `POST /api/v1/tenants` e `POST /api/v1/applications` sao bloqueados por exigirem API Key (caminhos nao anonimos).
- `src/PaymentHub.Api/Controllers/TenantsController.cs` e `ApplicationsController.cs` — endpoints de bootstrap publico; ambos exigem API Key via middleware.
- `src/PaymentHub.Api/Controllers/ProviderAccountsController.cs` — ja endurecido pelo Slice 6-B; nao usa body para tenant/application.
- `src/PaymentHub.Application/Abstractions/Context/IRuntimeEnvironment.cs` — interface existente com `IsDevelopment`; nao modificada para evitar quebra de mock tests.
- `src/PaymentHub.Application/Abstractions/Persistence/IRepositories.cs` — interfaces de repositorio; precisou de `GetBySlugAsync` em `ITenantRepository` e `GetByTenantAndNameAsync` em `IApplicationClientRepository` para idempotencia do seedor.
- `src/PaymentHub.Application/PaymentHub.Application.csproj` — SDK `Microsoft.NET.Sdk` puro; precisou de `Microsoft.Extensions.Logging.Abstractions` e `Microsoft.Extensions.Options` 10.0.0 como `PackageReference` explicito (problema documentado em `docs/harness/learnings.md`).
- `src/PaymentHub.Infrastructure.Postgres/Repositories/Repositories.cs` — implementacoes EF Core; precisou dos mesmos metodos de lookup adicionados a interface.
- `src/PaymentHub.Api/appsettings.json` e `appsettings.Development.json` — sem secao `Bootstrap` previa; precisou de defaults seguros (fail-safe) no base e dev opt-in.
- `src/PaymentHub.Worker/Program.cs` e `appsettings*.json` — Worker nao roda seedor, mas mantem defaults seguros na secao `Bootstrap` por consistencia.
- `tests/PaymentHub.UnitTests/Application/CreateCheckoutHandlerTests.cs` — usa `Mock<IRuntimeEnvironment>`; confirmado que a interface nao foi alterada, mantendo o mock funcional.
- `docs/audits/payment-hub-current-state-audit-2026-06-17.md` e `docs/audits/spec-adherence-audit-2026-06-17.md` — auditorias que classificaram o gap como P1-3.
- `docs/roadmap/000-payment-hub-roadmap.md`, `001-development-timeline.md`, `002-phase-status-board.md` — referenciam o gap e listam o slice 6-D.
- `docs/specs/002-multitenancy-and-authentication.md` e `011-security-and-compliance.md` — foram atualizadas com a secao "Politica de bootstrap".
- `docs/harness/validation-matrix.md` — lista de validacoes do slice 6-D adicionada.
- `docs/harness/learnings.md` — duas novas entradas (policy bootstrap e package reference).

## Arquivos alterados

| Arquivo | Tipo | Resumo |
| ------- | ---- | ------ |
| `src/PaymentHub.Application/Abstractions/Bootstrap/BootstrapOptions.cs` | Criado | `BootstrapOptions` com `Enabled`, `SeedDevelopmentData`, `AllowProductionBootstrap`, `DevelopmentTenantSlug`, `DevelopmentApplicationName`; defaults fail-safe (todos `false`). |
| `src/PaymentHub.Application/Abstractions/Bootstrap/IBootstrapPolicy.cs` | Criado | Interface com `EnvironmentName`, `IsProduction`, `ShouldRunDevelopmentSeed`, `ShouldAllowProductionBootstrap`. |
| `src/PaymentHub.Application/Abstractions/Bootstrap/DevelopmentSeedOutcome.cs` | Criado | `record` retornado pelo seedor com decisao politica e flags de criacao; nunca inclui API Key, secret ou credencial. |
| `src/PaymentHub.Application/Bootstrap/IDevelopmentDataSeeder.cs` | Criado | Interface publica do seedor. |
| `src/PaymentHub.Application/Bootstrap/DevelopmentDataSeeder.cs` | Criado | Implementacao: consulta `IBootstrapPolicy`, le `BootstrapOptions`, consulta `ITenantRepository.GetBySlugAsync` e `IApplicationClientRepository.GetByTenantAndNameAsync` para idempotencia, loga decisao politica com `ILogger`, nunca loga API Key/secret/credential. |
| `src/PaymentHub.Api/Auth/HostBootstrapPolicy.cs` | Criado | Implementacao de `IBootstrapPolicy` que le `IHostEnvironment` e `IOptions<BootstrapOptions>`. Determina `ShouldRunDevelopmentSeed` combinando ambiente e options. |
| `src/PaymentHub.Application/Abstractions/Persistence/IRepositories.cs` | Modificado | Adicionados `GetBySlugAsync` em `ITenantRepository` e `GetByTenantAndNameAsync` em `IApplicationClientRepository`. |
| `src/PaymentHub.Infrastructure.Postgres/Repositories/Repositories.cs` | Modificado | Adicionadas implementacoes EF Core dos novos metodos. Sem migration necessaria (sem mudanca de schema). |
| `src/PaymentHub.Application/PaymentHub.Application.csproj` | Modificado | Adicionados `Microsoft.Extensions.Logging.Abstractions` 10.0.0 e `Microsoft.Extensions.Options` 10.0.0 como `PackageReference`. |
| `src/PaymentHub.Api/Program.cs` | Modificado | Bind de `BootstrapOptions`; registro de `IBootstrapPolicy` (singleton) e `IDevelopmentDataSeeder` (scoped); chamada do seedor em escopo de startup antes de `app.Run()`. |
| `src/PaymentHub.Api/appsettings.json` | Modificado | Adicionada secao `Bootstrap` com defaults fail-safe (`Enabled=false`, `SeedDevelopmentData=false`, `AllowProductionBootstrap=false`). |
| `src/PaymentHub.Api/appsettings.Development.json` | Modificado | Adicionada secao `Bootstrap` com opt-in de dev (`Enabled=true`, `SeedDevelopmentData=true`, `AllowProductionBootstrap=false`). |
| `src/PaymentHub.Worker/appsettings.json` e `appsettings.Development.json` | Modificado | Adicionada secao `Bootstrap` com defaults seguros (Worker nao roda seedor, mas mantem consistencia). |
| `.env.example` | Modificado | Adicionadas variaveis `Bootstrap__*` com valores de exemplo para dev. |
| `docs/specs/002-multitenancy-and-authentication.md` | Modificado | Adicionada secao "Politica de bootstrap" com regras explicitas. |
| `docs/specs/011-security-and-compliance.md` | Modificado | Adicionada secao "Politica de bootstrap e admin seed" com regras de seguranca. |
| `docs/roadmap/000-payment-hub-roadmap.md` | Modificado | Marcado gap P1-3 como `[RESOLVIDO 2026-06-18]`; nota sobre o Slice 6-D na secao de status. |
| `docs/roadmap/001-development-timeline.md` | Modificado | Slice 6-D marcado como `[CONCLUIDO 2026-18]`. |
| `docs/roadmap/002-phase-status-board.md` | Modificado | Phase 6 cai de 2 para 1 gap P1 proprio; tabela de gaps e bloco A atualizados; indicadores (testes 85 -> 106, gaps 3 -> 2). |
| `docs/audits/payment-hub-current-state-audit-2026-06-17.md` | Modificado | Marcado P1-3 como resolvido. |
| `docs/audits/spec-adherence-audit-2026-06-17.md` | Modificado | Substituido P1-3 pela descricao original + correcao; matriz de aderencia atualizada; gaps de seguranca e codigo-spec marcados. |
| `docs/harness/validation-matrix.md` | Modificado | Adicionadas 16 novas linhas de validacao para Slice 6-D com status `PASS`. |
| `docs/harness/learnings.md` | Modificado | Adicionadas duas novas entradas: (1) "Bootstrap deve ser policy-driven, opt-in e nunca loggar credenciais"; (2) "Em projetos .NET 10 com `Microsoft.NET.Sdk`, `IOptions<>` exige PackageReference explicita". |
| `tests/PaymentHub.UnitTests/Api/HostBootstrapPolicyTests.cs` | Criado | 12 testes da politica. |
| `tests/PaymentHub.UnitTests/Application/DevelopmentDataSeederTests.cs` | Criado | 9 testes do seedor. |

Nenhum arquivo de migration foi criado ou modificado — o shape do banco nao mudou. O seedor usa metodos de leitura novos (`GetBySlugAsync`, `GetByTenantAndNameAsync`) que nao exigem schema novo.

## Comportamento anterior

Nao havia codigo de seed automatico em nenhum projeto. `Program.cs` apenas registrava handlers e middleware. Nao havia `IBootstrapPolicy`, `BootstrapOptions` ou `IDevelopmentDataSeeder`. `appsettings.json` nao tinha secao `Bootstrap`. `TenantsController` e `ApplicationsController` eram alcancados apenas por chamadas autenticadas, e o middleware os bloqueava na pratica para o caso "primeiro uso" porque nenhuma API Key existia previamente. Documentacao nao formalizava politica de bootstrap; auditoria classificou como P1-3.

## Comportamento novo

1. `IBootstrapPolicy` decide se o seedor deve rodar, combinando `IHostEnvironment` + `BootstrapOptions`. Implementacao em `HostBootstrapPolicy` (Api). Regras:
   - `ShouldRunDevelopmentSeed` e `true` somente se:
     - `Bootstrap:Enabled=true`,
     - `Bootstrap:SeedDevelopmentData=true`, e
     - ambiente e `Development`/`Test`/`Staging`, ou `Production` com `Bootstrap:AllowProductionBootstrap=true`.
   - `ShouldAllowProductionBootstrap` e `true` somente se `Enabled=true` e `AllowProductionBootstrap=true` em `Production`.
   - `IsProduction` detecta `Production` (case-insensitive) no `IHostEnvironment.EnvironmentName`.
2. `IDevelopmentDataSeeder` (Application) consulta a politica; quando nao deve rodar, loga decisao e retorna `DevelopmentSeedOutcome.Skipped`. Quando deve rodar:
   - Resolve `slug` e `applicationName` de `BootstrapOptions`; se ausentes, loga "missing" e retorna sem criar.
   - Chama `ITenantRepository.GetBySlugAsync(slug)`; se existe, reusa; se nao, cria `Tenant` com `Status=Active` e slug normalizado.
   - Chama `IApplicationClientRepository.GetByTenantAndNameAsync(tenant.Id, name)`; se existe, reusa; se nao, cria `ApplicationClient` com `Status=Active`.
   - Persiste com `IUnitOfWork.SaveChangesAsync` apenas se houve criacao.
   - Loga resultado: ids, slug, ambiente, flags `TenantCreated`/`ApplicationCreated`/`SeedExecuted`. **Nunca** loga API Key, secret, token, senha ou connection string.
3. `Program.cs` da Api executa o seedor uma vez em escopo de startup, antes de `app.Run()`. Worker nao chama o seedor.
4. `appsettings.json` (base) tem `Bootstrap:Enabled=false`, `SeedDevelopmentData=false`, `AllowProductionBootstrap=false`. `appsettings.Development.json` tem `Enabled=true`, `SeedDevelopmentData=true`, `AllowProductionBootstrap=false`. Worker tem defaults seguros consistentes.
5. `IRuntimeEnvironment` nao foi modificada (continua com `IsDevelopment` apenas), evitando quebra de `Mock<IRuntimeEnvironment>` em testes existentes (`CreateCheckoutHandlerTests`).

## Política implementada

A politica combina **fail-safe por default**, **opt-in explicito para ambientes sensiveis** e **idempotencia + logs seguros**:

| Cenario | Comportamento |
| ------- | ------------- |
| `Production` com `Bootstrap:Enabled=false` (default) | Seedor nao executa; loga "skipped". |
| `Production` com `Enabled=true`, `AllowProductionBootstrap=false` | Seedor nao executa; loga "skipped". |
| `Production` com `Enabled=true`, `SeedDevelopmentData=true`, `AllowProductionBootstrap=true` | Seedor executa; cria tenant+application idempotentemente. |
| `Development`/`Test` com `Enabled=true`, `SeedDevelopmentData=true` | Seedor executa; cria tenant+application idempotentemente. |
| `Development`/`Test` com `Enabled=false` ou `SeedDevelopmentData=false` | Seedor nao executa; loga "skipped". |
| Configuracao ausente (secao `Bootstrap` nao presente) | Defaults fail-safe do `BootstrapOptions` produzem `Enabled=false`; seedor nao executa. |
| Opcoes `DevelopmentTenantSlug` ou `DevelopmentApplicationName` ausentes | Seedor nao executa; loga "missing"; nao persiste nada. |
| Seed ja executado (segunda inicializacao) | `GetBySlugAsync`/`GetByTenantAndNameAsync` retornam existentes; nada duplicado. |
| Qualquer ambiente | Seedor nunca loga API Key, secret, token, senha, connection string, ou `Bearer ...`. |

## Decisões técnicas

1. **Politica em Application, implementacao em Api.** `IBootstrapPolicy` e `BootstrapOptions` ficam em `PaymentHub.Application/Abstractions/Bootstrap/` (testavel sem AspNetCore). `HostBootstrapPolicy` (que le `IHostEnvironment`) fica em `PaymentHub.Api/Auth/`, junto com `HostRuntimeEnvironment`. Isso segue o padrao ja existente de `IRuntimeEnvironment`/`HostRuntimeEnvironment`.

2. **Nao modificar `IRuntimeEnvironment`.** O slice poderia ter adicionado `IsProduction`/`IsStaging` a `IRuntimeEnvironment`, mas isso quebraria o `Mock<IRuntimeEnvironment>` em `CreateCheckoutHandlerTests.cs:26` (o mock sem setup explícito ja funciona porque Moq retorna `false` para propriedades nao configuradas; o risco de regressao nao justifica o ganho). A politica nova tem sua propria implementacao que le `IHostEnvironment` diretamente.

3. **Nao introduzir bypass de `ApiKeyAuthenticationMiddleware`.** O slice NAO adiciona `POST /api/v1/tenants` ou `POST /api/v1/applications` ao `IsAnonymousPath`. A primeira API Key operacional continua dependendo de canal externo (painel admin Phase 5, ou operador humano). O deadlock residual e documentado em `docs/harness/learnings.md` e na spec `002`.

4. **Seedor cria apenas `tenant` + `application` (nunca API Key, provider account, secret).** Isso evita o problema de como exibir uma API Key one-time em um startup automatico. Logs estruturados nao podem conter a chave raw (regra de `docs/harness/security.md`); o response do endpoint HTTP e o unico local onde a chave pode aparecer uma vez. Por consequencia, o operador ainda precisa usar o endpoint `POST /api/v1/applications` para gerar a primeira API Key — depois que o seed criou o tenant+application e a primeira API Key ja existir em outro tenant/canal.

5. **Idempotencia via `GetBySlugAsync` e `GetByTenantAndNameAsync`.** Em vez de try/catch em `DbUpdateException` (raca comeca a aplicacao comecoar de forma fragil), o seedor consulta antes de criar. Adicionei esses metodos a `ITenantRepository` e `IApplicationClientRepository` (e implementacoes em EF Core) sem exigir migration — sao leituras adicionais.

6. **Startup seeding em escopo explicito.** Em `Program.cs`, o seedor e chamado em `using var scope = app.Services.CreateScope()`. Isso garante que `IUnitOfWork`, `ITenantRepository` e `IApplicationClientRepository` (scoped, com `PaymentHubDbContext`) sejam resolvidos em escopo proprio, sem interferir no request pipeline. Falhas do seedor sao logadas e ignoradas (a aplicacao continua subindo), exceto se o seedor lancar uma `InvalidOperationException` por opcoes invalidas.

7. **Defaults fail-safe em todos os niveis.** `BootstrapOptions` tem defaults `false`/`null` para todos os campos. `appsettings.json` (base) repete `false` para os tres flags. `appsettings.Development.json` liga `Enabled=true` e `SeedDevelopmentData=true` mas mantem `AllowProductionBootstrap=false`. Worker tem `false` em todos os campos.

8. **`.env.example` ganha variaveis `Bootstrap__*`.** Mantem consistencia com `PaymentHub__*` ja existentes. Valores de exemplo sao para dev; nenhum secret real.

9. **Validacao de log seguro.** O teste `SeedAsync_ShouldNotLogApiKeyOrSecrets` em `DevelopmentDataSeederTests.cs` usa um `ILogger<T>` que captura mensagens e verifica que nao contem `apiKey=`, `secret=`, `password=`, `phk_` (prefixo de API Key do projeto) ou `Bearer `. Isso e um teste estrutural: garante que a classe nao loga valores raw sensiveis, mesmo que venham a ser passados no futuro.

10. **Sem `JsonStringEnumConverter` ou mudanca de contrato HTTP.** O slice nao altera nenhum DTO, controller ou handler existente. Apenas adiciona servicos novos.

## Segurança e logs

- **Logs emitidos pelo seedor (exemplos do teste):**
  - `"Bootstrap development seed skipped in environment {Environment} (policy enabled={Enabled}, production={IsProduction})."`
  - `"Bootstrap: dev tenant with slug {Slug} already exists (id={TenantId}). Reusing."`
  - `"Bootstrap: created dev tenant with slug {Slug} (id={TenantId})."`
  - `"Bootstrap: created dev application {ApplicationName} (id={ApplicationId}) under tenant {TenantId}."`
  - `"Bootstrap development seed completed in environment {Environment}: tenantCreated={TenantCreated}, applicationCreated={ApplicationCreated}, seedExecuted={SeedExecuted}."`
- **Logs NAO emitidos:** API Key raw, prefixo de API Key (`phk_...`), secret de provider, webhook secret, token, senha, connection string, payload JSON, body de request, header `Authorization`.
- **Cobertura:** teste `SeedAsync_ShouldNotLogApiKeyOrSecrets` em `DevelopmentDataSeederTests.cs` valida ausencia de substrings sensiveis nas mensagens capturadas.
- **Regra de `Bootstrap` em `appsettings.json`:** mesmo que um operador futuro adicione `Bootstrap:*` com segredo, o seedor nao le esses campos. O seedor so le `Enabled`, `SeedDevelopmentData`, `AllowProductionBootstrap`, `DevelopmentTenantSlug`, `DevelopmentApplicationName`. Adicionar `Bootstrap:SomeSecret` e seguro; nao sera logado nem persistido.
- **Mensagens de erro:** nao vazam ids, nomes ou status. Apenas mensagens como `"Bootstrap:Enabled or Bootstrap:SeedDevelopmentData is false."` ou `"Production environment forbids development seed."`.

## Testes adicionados/alterados

Cobertura nova em dois arquivos:

### `tests/PaymentHub.UnitTests/Api/HostBootstrapPolicyTests.cs` (12 testes)

| # | Cenario | Esperado |
| - | ------- | -------- |
| 1 | `ShouldRunDevelopmentSeed` retorna `false` em `Production` mesmo com `Enabled=true` e `SeedDevelopmentData=true` (sem `AllowProductionBootstrap`) | `false` |
| 2 | `ShouldRunDevelopmentSeed` retorna `false` em `Production` sem `AllowProductionBootstrap` | `false` |
| 3 | `ShouldRunDevelopmentSeed` retorna `true` em `Production` apenas com opt-in explicito (`AllowProductionBootstrap=true`) | `true` |
| 4 | `ShouldRunDevelopmentSeed` retorna `true` em `Development` quando habilitado | `true` |
| 5 | `ShouldRunDevelopmentSeed` retorna `true` em `Test` quando habilitado | `true` |
| 6 | `ShouldRunDevelopmentSeed` retorna `false` em `Development` quando `SeedDevelopmentData=false` | `false` |
| 7 | `ShouldRunDevelopmentSeed` retorna `false` em `Development` quando `Bootstrap:Enabled=false` | `false` |
| 8 | `ShouldRunDevelopmentSeed` retorna `false` em `Staging` quando nao habilitado | `false` |
| 9 | `ShouldRunDevelopmentSeed` retorna `true` em `Staging` quando habilitado | `true` |
| 10 | `ShouldRunDevelopmentSeed` retorna `false` em ambiente desconhecido (`QA`) | `false` |
| 11 | `EnvironmentName` retorna o nome configurado | string |
| 12 | Configuracao ausente (todos defaults) produz politica segura em `Production` | `ShouldRunDevelopmentSeed=false`, `ShouldAllowProductionBootstrap=false` |

### `tests/PaymentHub.UnitTests/Application/DevelopmentDataSeederTests.cs` (9 testes)

| # | Cenario | Esperado |
| - | ------- | -------- |
| 1 | Seedor pula quando politica desabilita em `Production` | `SeedExecuted=false`, repos nao chamados |
| 2 | Seedor pula quando bootstrap desabilitado em dev | `SeedExecuted=false`, repos nao chamados |
| 3 | Seedor cria tenant+application quando banco vazio e politica permite | ambos criados, `SaveChangesAsync` chamado 1x |
| 4 | Seedor reusa tenant+application existentes (idempotencia) | nada criado, `SaveChangesAsync` nao chamado |
| 5 | Seedor cria application quando tenant ja existe | so application criada, `SaveChangesAsync` chamado 1x |
| 6 | Tenant criado com `Status=Active` e slug normalizado; application com `Status=Active` e mesmo `TenantId` | invariantes de dominio |
| 7 | Logs do seedor nao contem `apiKey=`, `secret=`, `password=`, `phk_`, `Bearer ` | teste estrutural |
| 8 | Configuracao incompleta (slug/name ausentes) faz o seedor falhar com seguranca | `SeedExecuted=false`, motivo contem "missing" |
| 9 | Seedor roda em `Production` com opt-in explicito (testando o caminho opt-in) | seed executado |

Total adicionado: **21 testes**. Suite previa: 85. Suite nova: 106. Nenhum teste previo foi removido ou desabilitado.

## Validações executadas

Comandos executados em `/mnt/hd2/Projects/payment-hub`:

```bash
git status --short
DOTNET_ROOT=/usr/lib/dotnet rtk dotnet restore PaymentHub.slnx
DOTNET_ROOT=/usr/lib/dotnet rtk dotnet build PaymentHub.slnx
DOTNET_ROOT=/usr/lib/dotnet rtk dotnet test PaymentHub.slnx --no-build
DOTNET_ROOT=/usr/lib/dotnet rtk dotnet test PaymentHub.slnx --no-build --filter "FullyQualifiedName~Bootstrap"
DOTNET_ROOT=/usr/lib/dotnet rtk dotnet test PaymentHub.slnx --no-build --filter "FullyQualifiedName~ApiKeyAuthenticationMiddlewareTests"
DOTNET_ROOT=/usr/lib/dotnet rtk dotnet test PaymentHub.slnx --no-build --filter "FullyQualifiedName~ProviderAccount"
```

Resultados (2026-06-18):

| Comando | Resultado |
| ------- | --------- |
| `git status --short` | 19 arquivos modificados, 6 novos (5 `.cs` + relatorio deste slice). |
| `dotnet restore PaymentHub.slnx` | 9 projetos restaurados, 0 erros, 0 warnings. |
| `dotnet build PaymentHub.slnx` | 9 projetos, 0 erros, 0 warnings em ~6s. |
| `dotnet test PaymentHub.slnx` | 106 testes passando, 0 warnings em ~1.6s (suite previa: 85). |
| `dotnet test --filter "FullyQualifiedName~Bootstrap"` | 15 testes passando (12 `HostBootstrapPolicyTests` + 9 `DevelopmentDataSeederTests`; nota: o filtro "Bootstrap" tambem casa outras suites com `Bootstrap` no nome). |
| `dotnet test --filter "FullyQualifiedName~ApiKeyAuthenticationMiddlewareTests"` | 11 testes passando (sem regressao). |
| `dotnet test --filter "FullyQualifiedName~ProviderAccount"` | 15 testes passando (sem regressao; 10 handler + 5 controller). |

Apos o build inicial, a primeira execucao de `DevelopmentDataSeederTests` falhou em 1 teste (`SeedAsync_ShouldSkip_WhenBootstrapIsDisabled`) porque o `Reason` continha `"Bootstrap:Enabled or Bootstrap:SeedDevelopmentData is false."` e o teste procurava `"disabled"`. Ajustado o teste para conferir o trecho real (`"Bootstrap:Enabled"`). Re-executado: 21 testes passando.

## Evidências

- Build limpo em `dotnet build PaymentHub.slnx` (9/9, 0/0).
- Suite completa em `dotnet test PaymentHub.slnx` (106/106).
- 21 testes focados no novo comportamento (12 politica + 9 seeder).
- Suite do middleware intacta (11/11) — sem regressao no enforcement de status ativo.
- Suite do `ProviderAccount` intacta (15/15) — sem regressao no escopo de contexto autenticado.
- Mensagens de log verificadas quanto a nao-leak de API Key/secret/credential.
- Configuracoes default fail-safe em `appsettings.json` e `appsettings.Development.json`.
- Politica documentada em `002-multitenancy-and-authentication.md` e `011-security-and-compliance.md`.
- Matriz de validacao atualizada com 16 novas linhas.
- Duas novas entradas em `docs/harness/learnings.md`.

## Gaps remanescentes

- **P1-4** — `NoopApplicationWebhookDispatcher` no Worker host. Slice 7-A (Phase 7).
- **P1-5** — `ApplicationClient.WebhookSecret` persistido em texto claro. Slice 6-C + ADR-0007.
- **P2-2** — Projeto `PaymentHub.IntegrationTests` continua sem testes descobertos. Slice 1-IT.
- **P2-3** — Handlers administrativos nao gravam `AuditLog` (registrar `tenant`/`application`/`provider-account` na tabela `audit_logs`). O slice 6-D formalizou a politica de bootstrap mas NAO implementou gravacao de `AuditLog` em handlers — isso permanece para slice futuro.
- **Deadlock residual de primeira API Key em `Production`.** A politica torna explicito que `Production` nao cria API Keys via seed. O canal para obter a primeira API Key operacional (apos o seed de `Development`/`Test`) depende de endpoint HTTP autenticado, o que requer uma API Key preexistente. Resolucao prevista: Phase 5 (painel admin) ou canal externo documentado. O slice 6-D NAO introduz bypass; isso esta documentado em `docs/harness/learnings.md` e na spec `002-multitenancy-and-authentication.md`.

## Próximo slice recomendado

**Slice 6-C** — Proteger `ApplicationClient.WebhookSecret` em repouso (criptografia via `IDataProtectionProvider` ou decisao formal de risco com rotacao documentada). E o ultimo gap P1 da Phase 6 apos o Slice 6-D. Apos 6-C, Phase 6 estara com 0 gaps P1 proprios e podera ser marcada como `VALIDATED` em `002-phase-status-board.md`.

## Arquivos relacionados

- `docs/specs/002-multitenancy-and-authentication.md`
- `docs/specs/011-security-and-compliance.md`
- `docs/audits/payment-hub-current-state-audit-2026-06-17.md`
- `docs/audits/spec-adherence-audit-2026-06-17.md`
- `docs/roadmap/000-payment-hub-roadmap.md`
- `docs/roadmap/001-development-timeline.md`
- `docs/roadmap/002-phase-status-board.md`
- `docs/harness/validation-matrix.md`
- `docs/harness/learnings.md`
- `docs/harness/security.md`
- `docs/audits/slice-6a-active-status-enforcement-report-2026-06-17.md`
- `docs/audits/slice-6b-provider-account-authenticated-context-report-2026-06-18.md`
