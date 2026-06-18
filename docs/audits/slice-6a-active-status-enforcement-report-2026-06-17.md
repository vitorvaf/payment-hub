# Slice 6-A — Active Status Enforcement Report

Data: 2026-06-17
Phase: 6 — Seguranca e Confiabilidade
Spec relacionada: `docs/specs/002-multitenancy-and-authentication.md`, `docs/specs/011-security-and-compliance.md`
Slice predecessor: gap P1-1 da auditoria de 2026-06-17.

## Resumo

O `ApiKeyAuthenticationMiddleware` foi endurecido para consultar `Tenant.Status` e `ApplicationClient.Status` apos validar a API Key e os headers de `X-Tenant-Id` / `X-Application-Id`. Tenant ou application inativos retornam `403 Forbidden` com mensagem generica; tenant ou application inexistentes continuam retornando `401 Unauthorized` para nao vazar existencia. A API Key nunca aparece em logs, mensagens de erro ou payloads de resposta.

Este slice resolve o gap P1-1 documentado em `docs/audits/spec-adherence-audit-2026-06-17.md`.

## Gap enderecado

- **P1-1** — Tenant/application inativos nao bloqueiam fluxos autenticados.
  - Spec: `002-multitenancy-and-authentication.md` ("Tenant suspenso/desativado ou application inativa deve impedir criacao de checkout").
  - Risco original: tenant suspenso ou application inativa poderiam continuar consumindo a API desde que a API Key permanecesse `Active`.

## Arquivos analisados

- `src/PaymentHub.Api/Auth/ApiKeyAuthenticationMiddleware.cs` — middleware alvo; validava apenas hash da API Key, escopo (tenant/application da chave vs headers) e headers `X-Tenant-Id` / `X-Application-Id`.
- `src/PaymentHub.Api/Auth/HttpTenantContext.cs` — leitura do contexto apos middleware.
- `src/PaymentHub.Domain/Entities/Tenant.cs` — entidade com `TenantStatus` (`Active`/`Suspended`/`Disabled`) e metodos `Suspend()`/`Activate()` ja existentes.
- `src/PaymentHub.Domain/Entities/ApplicationClient.cs` — entidade com `ApplicationStatus` (`Active`/`Suspended`/`Disabled`); **nao tinha** `Suspend()`/`Activate()` antes deste slice (paridade com `Tenant` foi adicionada).
- `src/PaymentHub.Domain/Enums/TenantStatus.cs` e `ApplicationStatus.cs` — enums persistidos como string via `HasConversion<string>()`.
- `src/PaymentHub.Infrastructure.Postgres/Configurations/EntityConfigurations.cs` — colunas `tenants.status` e `application_clients.status` ja existentes e indexadas; sem migration nova necessaria.
- `src/PaymentHub.Application/Abstractions/Persistence/IRepositories.cs` — interfaces `ITenantRepository.GetByIdAsync` e `IApplicationClientRepository.GetByTenantAndIdAsync` ja expostas.
- `tests/PaymentHub.UnitTests/Api/ApiKeyAuthenticationMiddlewareTests.cs` — suite previa com 6 testes de escopo/headers.
- `docs/audits/spec-adherence-audit-2026-06-17.md` e `docs/audits/payment-hub-current-state-audit-2026-06-17.md` — auditoria que classificou o gap como P1.

> **Nota sobre nomenclatura:** a auditoria usa `Inactive` para o terceiro valor do enum, mas o codigo usa `Disabled` (`src/PaymentHub.Domain/Enums/TenantStatus.cs:7`). O slice compara com `Status != Active`, entao a diferenca e cosmetic. Nao foi renomeado para evitar fora de escopo.

## Arquivos alterados

| Arquivo | Tipo | Resumo |
| ------- | ---- | ------ |
| `src/PaymentHub.Api/Auth/ApiKeyAuthenticationMiddleware.cs` | Modificado | Injeta `ILogger`, `ITenantRepository` e `IApplicationClientRepository`; carrega Tenant e Application apos validar API Key; retorna 403 generico para entidade inativa, 401 para entidade inexistente; mensagens de erro nao vazam IDs nem a palavra API Key. |
| `src/PaymentHub.Domain/Entities/ApplicationClient.cs` | Modificado | Adiciona `Suspend()` e `Activate()` para paridade com `Tenant` e para suportar testes (mantem `Status` `private set`). |
| `tests/PaymentHub.UnitTests/Api/ApiKeyAuthenticationMiddlewareTests.cs` | Modificado | 11 testes: caminho anonimo, header ausente, API Key invalida, mismatch de tenant/application, tenant inativo, application inativa, tenant/application ativos, tenant/application inexistente, e dois testes anti-leak (resposta 401 e 403 nao vazam chave, IDs nem status). |
| `docs/roadmap/000-payment-hub-roadmap.md` | Modificado | Anota gap P1-1 como `[RESOLVIDO 2026-06-17]`; nota sobre Slice 6-A na secao de status. |
| `docs/roadmap/001-development-timeline.md` | Modificado | Marca Slice 6-A como `[CONCLUIDO 2026-06-17]`. |
| `docs/roadmap/002-phase-status-board.md` | Modificado | Reduz gaps P1 proprios da Phase 6 de 4 para 3; indicadores de saude atualizados (70 testes, 4 gaps P1). |
| `docs/harness/validation-matrix.md` | Modificado | Registra 8 novas linhas de validacao para Slice 6-A com status `PASS`. |
| `docs/audits/payment-hub-current-state-audit-2026-06-17.md` | Modificado | Marca linha P1-1 com strikethrough e `[RESOLVIDO 2026-06-17]`. |
| `docs/audits/spec-adherence-audit-2026-06-17.md` | Modificado | Substitui P1-1 pelo texto original + correcao + referencia ao slice. |
| `docs/harness/learnings.md` | Modificado | Adiciona entrada sobre o enforcement de status ativo. |

Arquivos criados:

- `docs/audits/slice-6a-active-status-enforcement-report-2026-06-17.md` (este documento).

Nenhum arquivo de migration foi criado ou modificado — os enums `TenantStatus` e `ApplicationStatus` ja existem desde a migration inicial (`20260616232151_InitialSchema.cs`) e as colunas correspondentes ja sao indexadas em `EntityConfigurations.cs:21` (`tenants`) e `EntityConfigurations.cs:41` (`application_clients`).

## Comportamento anterior

O middleware validava apenas:

1. `Authorization: Bearer <key>` presente e bem-formado.
2. Hash da chave corresponde a um `ApiKey.Active`.
3. `X-Tenant-Id` e `X-Application-Id` parseiam como GUID nao vazio.
4. Tenant/application do header combinam com `apiKey.TenantId` / `apiKey.ApplicationId`.

Nao havia consulta a `Tenant` nem a `ApplicationClient`. Tenant com `Status = Suspended` ou `Disabled`, ou application com `Status != Active`, continuavam obtendo acesso completo desde que a API Key estivesse `Active`.

Mensagens de erro anteriores detalhavam a falha (`"Missing or invalid X-Tenant-Id header"`, `"API key does not match tenant or application"`, etc.).

## Comportamento novo

Apos os passos 1-4 acima, o middleware consulta as entidades:

1. `ITenantRepository.GetByIdAsync(tenantId)`:
   - Retorna `null` -> `401 Unauthorized` (defesa contra tenant removido; mensagem generica para nao vazar existencia).
   - Retorna tenant com `Status != Active` -> `403 Forbidden` com mensagem generica.
2. `IApplicationClientRepository.GetByTenantAndIdAsync(tenantId, applicationId)`:
   - Retorna `null` -> `401 Unauthorized`.
   - Retorna application com `Status != Active` -> `403 Forbidden`.

Apenas apos todas as verificacoes o middleware popula `HttpContext.Items["tenantId"]`, `["applicationId"]` e `["apiKeyId"]` e chama `apiKey.Touch(...)`. Em qualquer falha de autorizacao, nenhum desses valores e definido, e o proximo handler nao e executado.

Mensagens de erro agora sao genericas:

- `401`: `{ "error": "unauthorized", "message": "Unauthorized" }`.
- `403`: `{ "error": "forbidden", "message": "Client application is not allowed to access this resource." }`.

Logs estruturados (`ILogger<ApiKeyAuthenticationMiddleware>`) registram apenas `TenantId`, `ApplicationId`, `ApiKeyId` e `Status` na rejeicao. A chave apresentada nunca e incluida.

## Decisoes tecnicas

1. **Reuso de repositorios existentes.** `ITenantRepository.GetByIdAsync` e `IApplicationClientRepository.GetByTenantAndIdAsync` ja existiam para outros fluxos; nenhuma nova query foi adicionada.

2. **Migration desnecessaria.** As colunas `tenants.status` e `application_clients.status` existem desde a migration inicial (`20260616232151_InitialSchema.cs`) e estao indexadas. Nenhum campo novo foi exigido por este slice.

3. **`ApplicationClient.Suspend()` / `Activate()`.** Adicionados para paridade com `Tenant` e para suportar testes sem reflexao. Mudanca puramente comportamental do dominio; nenhum impacto em schema.

4. **Mensagens genericas.** Escolhido retornar 401 (nao 403) para tenant/application inexistente. Objetivo: nao permitir que um atacante diferencie "tenant foi removido" de "tenant existe mas voce nao tem chave". Decisao alinhada com a spec (`002-multitenancy-and-authentication.md`) que lista apenas 401 para "API Key invalida".

5. **Tenant checado antes de application.** Ordem segue hierarquia (tenant e outer scope). Nao afeta o status HTTP final porque ambos retornam 403 com a mesma mensagem, mas evita uma query a `application` quando o tenant ja e sabidamente inativo.

6. **`LoggerAbstractions` ja estava disponivel** via `Microsoft.Extensions.Logging.Abstractions 10.0.0` em `PaymentHub.UnitTests.csproj:14`, entao `NullLogger<T>` foi usado sem novo pacote.

7. **Sem mudanca em `CreateCheckoutHandler`.** O handler continua lendo `ITenantContext` apos o middleware garantir contexto. Validar status no handler seria duplicacao; o middleware e o ponto central de autenticacao/autorizacao, conforme orientacao do prompt do slice.

## Testes adicionados/alterados

Cobertura nova em `tests/PaymentHub.UnitTests/Api/ApiKeyAuthenticationMiddlewareTests.cs` (11 testes no total, 5 existentes ajustados a nova assinatura + 6 novos):

| # | Cenario | Esperado |
| - | ------- | -------- |
| 1 | API Key ausente | 401 + sem contexto populado |
| 2 | API Key invalida | 401 + sem contexto populado |
| 3 | API Key valida + tenant inativo | 403 + `tenantId`/`applicationId`/`apiKeyId` nao definidos |
| 4 | API Key valida + application inativa | 403 + contexto nao populado |
| 5 | API Key valida + tenant/application ativos | 200 + contexto populado + `next` chamado |
| 6 | API Key nao vaza em body 401 | chave nao aparece no JSON |
| 7 | API Key/IDs/status nao vazam em body 403 | chave, IDs, nomes e `Status` nao aparecem no JSON |
| 8 | Tenant inexistente com API Key valida | 401 + contexto nao populado |
| 9 | Application inexistente com tenant ativo | 401 + contexto nao populado |
| 10 | Mismatch de tenant/application nos headers | 401 (caso previo preservado) |
| 11 | Caminho anonimo (`/health`) | 200 sem consultar repositorios |

`MockBehavior.Strict` foi usado nos repositorios para garantir que o middleware nao faz queries desnecessarias (e.g., para o caso anonimo).

## Validacoes executadas

Comandos executados em `/mnt/hd2/Projects/payment-hub`:

```bash
git status --short
dotnet restore PaymentHub.slnx
dotnet build PaymentHub.slnx
dotnet test PaymentHub.slnx --no-build
dotnet test PaymentHub.slnx --no-build --filter "FullyQualifiedName~ApiKeyAuthenticationMiddlewareTests"
```

Resultados (2026-06-17):

| Comando | Resultado |
| ------- | --------- |
| `git status --short` | 3 arquivos modificados antes do docs/docs updates. |
| `dotnet restore PaymentHub.slnx` | `All projects are up-to-date for restore.` (sem mudanca de dependencias). |
| `dotnet build PaymentHub.slnx` | `9 projects, 0 errors, 0 warnings` em ~7s. |
| `dotnet test PaymentHub.slnx` | `70 tests passed, 0 warnings` em ~1.3s. |
| `dotnet test --filter ApiKeyAuthenticationMiddlewareTests` | `11 tests passed`. |

Antes do slice: 64 testes. Apos: 70 testes (+6). Nenhum teste previo foi removido ou desabilitado.

## Evidencias

- Build limpo em `dotnet build PaymentHub.slnx`.
- Suite completa em `dotnet test PaymentHub.slnx` (70/70).
- Teste focado de middleware em `dotnet test --filter ApiKeyAuthenticationMiddlewareTests` (11/11).
- Mudancas isoladas em middleware de autenticacao + entidade `ApplicationClient` (paridade de metodos de status). Sem alteracao em providers, workers, checkout, dispatcher, webhooks, outbox, contratos HTTP, migrations, ou `Program.cs`.
- Mensagens de erro testadas quanto a nao-leak de API Key, IDs e termos sensiveis.

## Gaps remanescentes

- **P1-2** — `RegisterProviderAccountHandler` ainda aceita tenant/application do body. Slice 6-B.
- **P1-3** — Endpoints de bootstrap/admin sem politica de autenticacao. Slice 6-D + ADR-0006.
- **P1-5** — `ApplicationClient.WebhookSecret` persistido em texto claro. Slice 6-C + ADR-0007.
- **P1-4** — `NoopApplicationWebhookDispatcher` no Worker host. Slice 7-A (Phase 7).
- **P2-2** — Projeto `PaymentHub.IntegrationTests` continua sem testes descobertos. Slice 1-IT.
- **P2-5** — Documentacao de arquitetura ainda usa formato antigo de HMAC. Slice documental.

Slice 6-A nao cobre testes de integracao HTTP porque nao ha fixture de integracao ainda (gap P2-2). Cobertura atual via testes unitarios do middleware; quando a fixture existir, poderao ser adicionados testes `WebApplicationFactory<Program>` para o mesmo cenario. Esta dependencia esta registrada em `docs/harness/learnings.md`.

## Proximo slice recomendado

**Slice 6-B** — Corrigir `RegisterProviderAccountHandler` para derivar `TenantId`/`ApplicationId` do `ITenantContext` autenticado, nao do body. Reduz risco de um application de um tenant tentar registrar provider account em outro escopo. O proximo P1 com superficie de ataque maior.

Apos Slice 6-B, recomenda-se Slice 6-C (protecao de `WebhookSecret` em repouso) por ser prerequisito de Slice 7-A, e por fim Slice 6-D (politica de bootstrap + AuditLog).

## Arquivos relacionados

- `docs/specs/002-multitenancy-and-authentication.md`
- `docs/specs/011-security-and-compliance.md`
- `docs/audits/spec-adherence-audit-2026-06-17.md`
- `docs/audits/payment-hub-current-state-audit-2026-06-17.md`
- `docs/roadmap/000-payment-hub-roadmap.md`
- `docs/roadmap/001-development-timeline.md`
- `docs/roadmap/002-phase-status-board.md`
- `docs/harness/validation-matrix.md`
- `docs/harness/learnings.md`
- `docs/harness/slice-template.md`
- `docs/harness/security.md`