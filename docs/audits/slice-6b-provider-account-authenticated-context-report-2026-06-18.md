# Slice 6-B — ProviderAccount Authenticated Context Report

Data: 2026-06-18
Phase: 6 — Seguranca e Confiabilidade
Spec relacionada: `docs/specs/002-multitenancy-and-authentication.md`, `docs/specs/009-api-contracts.md`, `docs/specs/011-security-and-compliance.md`
Slice predecessor: gap P1-2 da auditoria de 2026-06-17; segue o Slice 6-A (enforcement de status ativo no middleware).

## Resumo

`POST /api/v1/provider-accounts` foi endurecido para que `ProviderAccount` seja criado exclusivamente a partir do contexto autenticado (`ITenantContext`, populado pelo `ApiKeyAuthenticationMiddleware` apos validar API Key, escopo, status do tenant e status da application). Os campos `tenantId` e `applicationId` foram removidos do DTO de request, de modo que valores divergentes enviados no body nao tem mais efeito por design (campo inexistente, binding do ASP.NET Core os ignora silenciosamente).

O controller deriva `tenantId` e `applicationId` de `ITenantContext` (com `try/catch` em `InvalidOperationException` retornando `401 Unauthorized` quando o contexto nao foi resolvido) e passa esses valores explicitamente ao handler. O handler agora trata `Guid.Empty` como entrada invalida e nao persiste `ProviderAccount` algum. Resposta continua sem expor `ApiKey`, `Secret` ou `EncryptedCredentials`.

## Gap enderecado

- **P1-2** — `RegisterProviderAccountHandler` usa `tenantId`/`applicationId` do body em vez do contexto autenticado.
  - Spec: `002-multitenancy-and-authentication.md` (regras de isolamento) e `009-api-contracts.md` (endpoint `POST /api/v1/provider-accounts`).
  - Risco original: uma API Key autenticada para `tenant-a / app-a` podia tentar registrar provider account em `tenant-b / app-b` apenas enviando os IDs conhecidos no body, ja que o handler aceitava `request.TenantId`/`request.ApplicationId` sem qualquer cross-check com o contexto.
  - Resolucao: tenant/application passam a ser parametros explicitos do handler, injetados pelo controller a partir de `ITenantContext`. Body nao contem mais esses campos.

## Arquivos analisados

- `src/PaymentHub.Api/Controllers/ProviderAccountsController.cs` — endpoint alvo; passava o body inteiro ao handler sem derivar contexto.
- `src/PaymentHub.Application/Tenants/RegisterProviderAccountHandler.cs` — handler alvo; usava `request.TenantId`/`request.ApplicationId` para construir o `ProviderAccount` e validava a existencia da application via `IApplicationClientRepository.ExistsAsync`.
- `src/PaymentHub.Application/Tenants/Dtos.cs` — `RegisterProviderAccountRequestDto` continha `TenantId`/`ApplicationId` como campos publicos.
- `src/PaymentHub.Application/Abstractions/Context/ITenantContext.cs` — interface ja existente; `HttpTenantContext` resolve a partir de `HttpContext.Items["tenantId"]` / `["applicationId"]` populados pelo middleware.
- `src/PaymentHub.Api/Auth/ApiKeyAuthenticationMiddleware.cs` — middleware ja popula o contexto apos validar API Key, escopo, status do tenant e status da application (Slice 6-A).
- `src/PaymentHub.Api/Auth/HttpTenantContext.cs` — implementacao de `ITenantContext`; lanca `InvalidOperationException` quando o contexto nao esta populado.
- `src/PaymentHub.Domain/Entities/ProviderAccount.cs` — entidade alvo; construtor exige `tenantId`/`applicationId` nao-empty (ja validava).
- `src/PaymentHub.Infrastructure.Postgres/Repositories/Repositories.cs` — `IProviderAccountRepository.AddAsync` persiste o `ProviderAccount` no escopo recebido.
- `tests/PaymentHub.UnitTests/Api/ApiKeyAuthenticationMiddlewareTests.cs` — suite de middleware ja validada pelo Slice 6-A.
- `docs/audits/spec-adherence-audit-2026-06-17.md` e `docs/audits/payment-hub-current-state-audit-2026-06-17.md` — auditorias que classificaram o gap como P1.
- `docs/specs/002-multitenancy-and-authentication.md`, `009-api-contracts.md`, `011-security-and-compliance.md` — specs que documentam o contrato de autenticacao e a expectativa de derivacao de contexto.

## Arquivos alterados

| Arquivo | Tipo | Resumo |
| ------- | ---- | ------ |
| `src/PaymentHub.Application/Tenants/Dtos.cs` | Modificado | Remove `TenantId` e `ApplicationId` de `RegisterProviderAccountRequestDto`. |
| `src/PaymentHub.Application/Tenants/RegisterProviderAccountHandler.cs` | Modificado | Handler recebe `Guid tenantId, Guid applicationId` explicitamente; remove dependencia de `IApplicationClientRepository` (existencia ja garantida pelo middleware); rejeita `Guid.Empty`; atualiza `RegisterProviderAccountValidator` removendo as regras de `TenantId`/`ApplicationId`. |
| `src/PaymentHub.Api/Controllers/ProviderAccountsController.cs` | Modificado | Injeta `ITenantContext`; le `tenantId`/`applicationId` via propriedade; retorna `401 Unauthorized` quando o contexto lanca `InvalidOperationException`. |
| `tests/PaymentHub.UnitTests/Application/RegisterProviderAccountHandlerTests.cs` | Criado | 10 testes unitarios do handler. |
| `tests/PaymentHub.UnitTests/Api/ProviderAccountsControllerTests.cs` | Criado | 5 testes unitarios do controller. |
| `docs/specs/002-multitenancy-and-authentication.md` | Modificado | Adiciona regras explicitas de derivacao de tenant/application a partir de `ITenantContext` em endpoints autenticados. |
| `docs/specs/009-api-contracts.md` | Modificado | `POST /api/v1/provider-accounts`: documenta que `tenantId`/`applicationId` sao derivados exclusivamente do contexto autenticado e removidos do body. |
| `docs/specs/011-security-and-compliance.md` | Modificado | Adiciona linha na tabela de contratos sobre derivacao de tenant/application via `ITenantContext`. |
| `docs/roadmap/000-payment-hub-roadmap.md` | Modificado | Marca gap P1-2 como `[RESOLVIDO 2026-06-18]`; adiciona nota sobre o Slice 6-B. |
| `docs/roadmap/001-development-timeline.md` | Modificado | Marca Slice 6-B como `[CONCLUIDO 2026-06-18]`. |
| `docs/roadmap/002-phase-status-board.md` | Modificado | Phase 6 cai de 3 para 2 gaps P1 proprios; atualiza tabela de gaps, bloco A e indicadores (70 -> 85 testes, 4 -> 3 gaps P1). |
| `docs/audits/payment-hub-current-state-audit-2026-06-17.md` | Modificado | Marca P1-2 como resolvido pelo Slice 6-B. |
| `docs/audits/spec-adherence-audit-2026-06-17.md` | Modificado | Substitui P1-2 pela descricao original + correcao; atualiza matriz de aderencia e gaps de teste/seguranca. |
| `docs/harness/validation-matrix.md` | Modificado | 9 novas linhas de validacao para Slice 6-B com status `PASS`. |
| `docs/harness/learnings.md` | Modificado | Entrada sobre derivacao obrigatoria via `ITenantContext`. |

Nenhum arquivo de migration foi criado ou modificado — a tabela `provider_accounts` ja existia desde a migration inicial (`20260616232151_InitialSchema.cs`) e o shape do banco nao mudou.

## Comportamento anterior

`ProviderAccountsController.Register` recebia o body e o passava integralmente ao handler. `RegisterProviderAccountRequestDto` tinha `TenantId` e `ApplicationId` publicos. `RegisterProviderAccountHandler.HandleAsync` usava esses campos para construir o `ProviderAccount`:

```csharp
var account = new ProviderAccount(
    Guid.NewGuid(),
    request.TenantId,
    request.ApplicationId,
    ...);
```

O middleware ja garantia que a API Key estava vinculada ao tenant/application correto, mas o handler nao reusava `ITenantContext`. A unica defesa contra cross-tenant registration era a checagem `_apps.ExistsAsync(request.TenantId, request.ApplicationId)`, que apenas confirmava que a application existia — nao que ela correspondia ao caller autenticado. O controller nao injetava nem consultava `ITenantContext`.

A resposta ja nao expunha `ApiKey`/`Secret` (campos ja estavam fora de `ProviderAccountResponseDto`), mas o problema era a origem dos IDs usados na construcao da entidade.

## Comportamento novo

`RegisterProviderAccountRequestDto` nao tem mais `TenantId`/`ApplicationId`. ASP.NET Core ignora silenciosamente chaves extras no JSON quando o DTO nao declara propriedades correspondentes (comportamento padrao do `System.Text.Json`).

`RegisterProviderAccountHandler.HandleAsync` recebe `Guid tenantId, Guid applicationId` como parametros explicitos:

```csharp
public async Task<ProviderAccountResponseDto> HandleAsync(
    Guid tenantId,
    Guid applicationId,
    RegisterProviderAccountRequestDto request,
    CancellationToken cancellationToken)
{
    if (tenantId == Guid.Empty)
        throw new InvalidOperationException("Authenticated tenant id is required.");
    if (applicationId == Guid.Empty)
        throw new InvalidOperationException("Authenticated application id is required.");
    ...
}
```

`ProviderAccountsController.Register` injeta `ITenantContext`, le os valores e mapeia falhas de contexto para `401`:

```csharp
Guid tenantId;
Guid applicationId;
try
{
    tenantId = _tenantContext.TenantId;
    applicationId = _tenantContext.ApplicationId;
}
catch (InvalidOperationException)
{
    return Unauthorized(new { error = "unauthorized", message = "Unauthorized" });
}

var result = await _handler.HandleAsync(tenantId, applicationId, request, cancellationToken);
```

Caminhos de falha:

1. Body com `tenantId`/`applicationId` no JSON: ignorados (campos nao declarados no DTO).
2. API Key ausente / invalida / escopo divergente: middleware retorna `401` antes do controller ser chamado.
3. Tenant ou application inativos: middleware retorna `403` antes do controller ser chamado.
4. `ITenantContext` lanca `InvalidOperationException` (caso degenerado, nao esperado em producao apos o middleware): controller retorna `401`.
5. `tenantId`/`applicationId` recebidos como `Guid.Empty`: handler lanca `InvalidOperationException`, nenhum `ProviderAccount` e persistido.
6. Validacao de payload falha: controller retorna `400` antes de consultar contexto (nao vaza contexto).

## Decisoes tecnicas

1. **Remocao dos campos do DTO (regra preferencial).** O slice permitia manter os campos deprecated e ignora-los; a equipe optou por remove-los. Justificativa: o endpoint e apenas server-to-server e autenticado, sem dependencia externa legitima nesses campos; manter campos deprecated induz a erro em integradores e adiciona superficie de ataque desnecessaria.

2. **Controller chama `ITenantContext`, handler recebe IDs explicitos.** Segue o mesmo padrao de `CheckoutsController` (que ja chama `_tenantContext.TenantId`/`ApplicationId` antes de chamar `CreateCheckoutHandler.HandleAsync(tenantId, applicationId, ...)`). Handler continua livre de dependencia em `ITenantContext`, o que facilita testes e mantem a fronteira handler/repositorio.

3. **Remocao da checagem `_apps.ExistsAsync(tenantId, applicationId)` no handler.** O middleware ja garante que tenant/application estao ativos; o handler confia no caller autenticado. Defense in depth foi preservado atraves do middleware e da rejeicao de `Guid.Empty`.

4. **Mapeamento de `InvalidOperationException` para `401` no controller.** `HttpTenantContext` lanca `InvalidOperationException("Tenant id not resolved.")` quando o contexto nao esta populado. O controller captura essa excecao e devolve `401 Unauthorized` com mensagem generica para nao vazar estado interno.

5. **JSON enum sem `JsonStringEnumConverter` global.** O projeto nao configura `JsonStringEnumConverter` no `Program.cs` (verificado: nao ha `AddJsonOptions` customizado). Isso significa que `providerCode`/`environment` sao desserializados como inteiros em payloads reais. O teste `Register_ShouldIgnoreTenantAndApplicationFieldsInBodyWhenPresent` usa `JsonStringEnumConverter` apenas no contexto de teste para construir o DTO a partir de um JSON ilustrativo; o teste principal de derivacao via controller nao depende dessa desserializacao.

6. **Sem breaking change publico real.** O endpoint `POST /api/v1/provider-accounts` e apenas server-to-server, autenticado por API Key, e nao tem consumidores publicos. A remocao de campos do body e documentada como hardening em `009-api-contracts.md` e nao ha versao anterior documentada desses campos como contrato.

7. **Mensagens de erro nao vazam `tenantId`/`applicationId` ou status.** O handler usa mensagens genericas ("Authenticated tenant id is required." / "Authenticated application id is required.") sem expor GUIDs. O controller, ao detectar falha de contexto, devolve apenas `{ error: "unauthorized", message: "Unauthorized" }`.

8. **`MockBehavior.Strict` no repositorio dos testes.** Garante que o handler nao faz queries desnecessarias (`GetByIdAsync`, `ExistsAsync`, etc.). Apenas `AddAsync` e esperado. Ajuda a detectar regressoes silenciosas.

## Testes adicionados/alterados

Cobertura nova em dois arquivos:

### `tests/PaymentHub.UnitTests/Application/RegisterProviderAccountHandlerTests.cs` (10 testes)

| # | Cenario | Esperado |
| - | ------- | -------- |
| 1 | `RegisterProviderAccountRequestDto` nao expoe `TenantId` ou `ApplicationId` | Propriedades ausentes na `Type` (defesa em profundidade compile-time) |
| 2 | `ProviderAccountResponseDto` nao expoe `ApiKey`, `Secret` ou `EncryptedCredentials` | Propriedades ausentes na `Type` |
| 3 | Handler usa `tenantId` do caller e persiste no escopo correto | `account.TenantId`/`ApplicationId` == parametros |
| 4 | Handler aplica `Name`, `Environment`, `ProviderCode`, `IsDefault` do request | Valores refletidos na entidade persistida e na resposta |
| 5 | Handler protege credenciais antes de persistir | `_protector.Protect` chamado uma vez; payload contem `apiKey`/`secret` |
| 6 | Handler persiste usando apenas IDs do caller (sem fallback de body) | `account.TenantId`/`ApplicationId` nao sao `Guid.Empty` |
| 7 | Handler lanca e nao persiste quando `tenantId == Guid.Empty` | `InvalidOperationException`; `_accounts.AddAsync` nunca chamado; `_uow.SaveChangesAsync` nunca chamado |
| 8 | Handler lanca e nao persiste quando `applicationId == Guid.Empty` | Idem |
| 9 | Handler persiste `Id` unico e timestamps | `result.Id != Guid.Empty`; `CreatedAt`/`UpdatedAt` coerentes |
| 10 | Resposta reflete o escopo correto | `TenantId`/`ApplicationId` da resposta batem com os parametros; `Active = true` |

### `tests/PaymentHub.UnitTests/Api/ProviderAccountsControllerTests.cs` (5 testes)

| # | Cenario | Esperado |
| - | ------- | -------- |
| 1 | Controller chama handler com tenant/application do contexto autenticado | `_handler.HandleAsync(tenantId, applicationId, ...)` chamado uma vez |
| 2 | Body com `tenantId`/`applicationId` extras e divergentes nao afeta a operacao | Handler chamado com IDs do contexto; nunca chamado com IDs do body |
| 3 | `ITenantContext.TenantId` lanca `InvalidOperationException` | Controller retorna `401 Unauthorized`; handler nao chamado |
| 4 | `ITenantContext.ApplicationId` lanca `InvalidOperationException` | Controller retorna `401 Unauthorized`; handler nao chamado |
| 5 | Validacao FluentValidation falha | Controller retorna `400 BadRequest`; contexto nao consultado; handler nao chamado |

Total adicionado: **15 testes**. Suite previa: 70. Suite nova: 85. Nenhum teste previo foi modificado ou removido.

## Validacoes executadas

Comandos executados em `/mnt/hd2/Projects/payment-hub`:

```bash
git status --short
dotnet restore PaymentHub.slnx
dotnet build PaymentHub.slnx
dotnet test PaymentHub.slnx
dotnet test PaymentHub.slnx --filter "FullyQualifiedName~RegisterProviderAccount|FullyQualifiedName~ProviderAccountsController"
dotnet test PaymentHub.slnx --filter "FullyQualifiedName~ApiKeyAuthenticationMiddlewareTests"
```

Resultados (2026-06-18):

| Comando | Resultado |
| ------- | --------- |
| `git status --short` | working tree limpo antes da execucao; arquivos do slice alterados. |
| `dotnet restore PaymentHub.slnx` | dependencias restauradas sem mudancas. |
| `dotnet build PaymentHub.slnx` | 9 projetos, 0 erros, 0 warnings em ~9s. |
| `dotnet test PaymentHub.slnx` | 85 testes passando, 0 warnings em ~5s (suite previa: 70). |
| `dotnet test --filter "RegisterProviderAccount|ProviderAccountsController"` | 15 testes passando. |
| `dotnet test --filter "ApiKeyAuthenticationMiddlewareTests"` | 11 testes passando (sem regressao). |

Antes do slice: 70 testes. Apos: 85 testes (+15). Nenhum teste previo foi removido ou desabilitado.

## Evidencias

- Build limpo em `dotnet build PaymentHub.slnx` (9/9, 0/0).
- Suite completa em `dotnet test PaymentHub.slnx` (85/85).
- 15 testes focados no novo comportamento.
- Suite do middleware intacta (11/11) — sem regressao no enforcement de status ativo.
- Mensagens de erro do handler e do controller verificadas quanto a nao-leak de GUIDs e termos sensiveis.
- DTOs verificados via reflexao (`GetProperty` retorna `null`) garantindo ausencia de campos `TenantId`/`ApplicationId` no request e `ApiKey`/`Secret`/`EncryptedCredentials` na resposta.

## Gaps remanescentes

- **P1-3** — Endpoints de bootstrap/admin sem politica explicita. Slice 6-D + ADR-0006.
- **P1-4** — `NoopApplicationWebhookDispatcher` no Worker host. Slice 7-A (Phase 7).
- **P1-5** — `ApplicationClient.WebhookSecret` persistido em texto claro. Slice 6-C + ADR-0007.
- **P2-2** — Projeto `PaymentHub.IntegrationTests` continua sem testes descobertos. Slice 1-IT.
- **P2-3** — Handlers administrativos nao gravam `AuditLog`. Slice 6-D.

Este slice nao cobriu testes de integracao HTTP porque nao ha fixture de integracao ainda (gap P2-2). Cobertura via testes unitarios de handler e controller; quando a fixture existir, poderao ser adicionados testes `WebApplicationFactory<Program>` para validar o mesmo cenario com middleware, autenticao e serializacao JSON reais.

## Proximo slice recomendado

**Slice 6-C** — Protecao de `ApplicationClient.WebhookSecret` em repouso (criptografia, KMS ou decisao formal de risco com rotacao documentada). Apos 6-C, **Slice 6-D** — politica de bootstrap/admin + gravacao de `AuditLog` em handlers administrativos.

## Arquivos relacionados

- `docs/specs/002-multitenancy-and-authentication.md`
- `docs/specs/009-api-contracts.md`
- `docs/specs/011-security-and-compliance.md`
- `docs/audits/spec-adherence-audit-2026-06-17.md`
- `docs/audits/payment-hub-current-state-audit-2026-06-17.md`
- `docs/audits/slice-6a-active-status-enforcement-report-2026-06-17.md`
- `docs/roadmap/000-payment-hub-roadmap.md`
- `docs/roadmap/001-development-timeline.md`
- `docs/roadmap/002-phase-status-board.md`
- `docs/harness/validation-matrix.md`
- `docs/harness/learnings.md`
- `docs/harness/security.md`
