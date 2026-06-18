# Spec Adherence Audit - 2026-06-17

## Resumo executivo

O `main` esta majoritariamente aderente ao desenho do MVP: checkout hospedado, multi-tenant por API Key, Postgres com Inbox/Outbox, status canonico, idempotencia de checkout, webhooks de provider persistidos antes do processamento e ausencia de campos de cartao/CVV no dominio principal.

Nao foram identificados achados P0 comprovados nesta auditoria. Os principais riscos restantes sao P1: validacao insuficiente de status ativo de tenant/application em fluxos autenticados, cadastro de provider account aceitando tenant/application do corpo em vez do contexto autenticado, bootstrap/admin endpoints divergentes da documentacao, worker de outbox registrado com dispatcher no-op e armazenamento do `webhook_secret` em texto claro.

Tambem ha gaps P2 relevantes em testes de integracao, validacao de assinatura de webhooks externos, audit logs de acoes administrativas e constraints relacionais no banco.

## Escopo da auditoria

Foram auditados:

- Specs formais em `docs/specs/`.
- ADRs em `docs/adr/`.
- Documentacao de arquitetura, banco, API e harness.
- Projetos `PaymentHub.Api`, `PaymentHub.Application`, `PaymentHub.Domain`, `PaymentHub.Infrastructure.Postgres`, `PaymentHub.Providers` e `PaymentHub.Worker`.
- Testes unitarios e projeto de testes de integracao.
- Migracao inicial e snapshot do EF Core.

Fora do escopo:

- Alteracoes em codigo de producao, testes, migracoes, Docker ou arquitetura.
- Execucao real contra providers externos.
- Auditoria dinamica de seguranca em ambiente implantado.

## Resultado geral

Status geral: ⚠️ Parcial

O repositorio implementa bem o fluxo essencial do MVP, mas ainda nao deve ser considerado plenamente aderente as specs para uso operacional completo. A maior parte das divergencias esta em bordas de autorizacao, operacao dos workers, protecao de segredos e cobertura de integracao.

## Matriz de aderencia

| Área | Spec | Código | Testes | Status | Observações |
| --- | --- | --- | --- | --- | --- |
| Escopo MVP | `000-mvp-scope.md`, ADR-0003 | Checkout hospedado, sem cartao/CVV, sem split/wallet/recorrencia | Coberto indiretamente por dominio e busca estatica | ✅ Aderente | Nao ha campos de cartao/CVV nas entidades principais. |
| Multi-tenancy | `001-multi-tenancy.md` | Tenant/application presentes nas entidades e repositorios | Cobertura unitaria parcial | ⚠️ Parcial | Isolamento existe, mas falta enforcement de status ativo e ha fluxo que aceita tenant/application do body. |
| API Key | `002-api-authentication.md`, ADR-0004 | Middleware valida Bearer, tenant e application headers contra hash | Testes unitarios do middleware | ⚠️ Parcial | Nao valida status de tenant/application; endpoints de bootstrap/admin divergem da doc. |
| Modelo de dominio | `003-domain-model.md` | Entidades e enums principais implementados | Testes de `Payment`, `WebhookEvent`, `OutboxEvent` e mappers | ✅ Aderente | Status canonico e invariantes centrais existem. |
| Checkout | `004-checkout-flow.md` | `CreateCheckoutHandler` cria payment, tentativa e idempotencia | Testes unitarios de handler | ⚠️ Parcial | Idempotencia coberta; falta teste API/e2e e bloqueio por tenant/app inativo. |
| Providers | `006-provider-integration.md`, ADR-0005 | Router e adapters fake/Stripe/MercadoPago/AbacatePay skeleton | Testes do fake e mapper | ⚠️ Parcial | Credenciais sao criptografadas, mas providers reais ainda sao skeleton e assinatura externa nao e validada. |
| Webhooks de provider | `007-webhook-processing.md` | Controller persiste inbox; worker processa e cria outbox | Testes unitarios do handler | ⚠️ Parcial | Deduplicacao existe; assinatura externa e teste e2e faltam. |
| Banco | `010-database-contract.md`, ADR-0002 | Tabelas, indices e unique keys principais existem | Sem testes de integracao | ⚠️ Parcial | Poucas FKs no banco; projeto de integracao nao possui testes descobertos. |
| Seguranca | `011-security-and-compliance.md` | API keys hashed; provider credentials protegidas por Data Protection | Testes parciais de middleware e signer | ⚠️ Parcial | `webhook_secret` fica em texto claro; falta validacao de assinatura de provider. |
| Observabilidade e auditoria | `012-observability-and-audit.md` | Health checks, logs basicos e entidade `AuditLog` | Sem testes especificos de audit log | ⚠️ Parcial | Repositorio de audit log existe, mas handlers administrativos nao gravam auditoria. |
| Testes | `013-testing-strategy.md` | Suite unitaria ativa | `dotnet test` passa, mas IntegrationTests nao descobre testes | ⚠️ Parcial | Estrategia pede integracao DB/API/workers ainda ausente. |
| Arquitetura | `overview.md`, `mvp-decisions.md` | Camadas separadas e Postgres Inbox/Outbox | Build/test validam compilacao | ⚠️ Parcial | Worker usa dispatcher no-op para outbox no host dedicado. |

## Achados por prioridade

### P0 - Crítico

Nenhum achado P0 comprovado nesta auditoria.

### P1 - Alto

1. ~~Tenant/application inativos nao bloqueiam fluxos autenticados.~~ `[RESOLVIDO 2026-06-17 pelo Slice 6-A]`
   - Specs: `001-multi-tenancy.md`, `002-api-authentication.md`, `004-checkout-flow.md`.
   - Evidencia original: `ApiKeyAuthenticationMiddleware` validava API Key e escopo, mas nao carregava `Tenant.Status` nem `ApplicationClient.Status`; `CreateCheckoutHandler` usava existencia de tenant/app sem verificar status.
   - Correcao: middleware agora consulta `ITenantRepository.GetByIdAsync` e `IApplicationClientRepository.GetByTenantAndIdAsync`; retorna `403 Forbidden` quando `TenantStatus != Active` ou `ApplicationStatus != Active`. Tenant ou application inexistente continua retornando `401` para nao vazar existencia.
   - Risco original: tenant suspenso ou application inativa podiam continuar criando checkouts se a API Key permanecesse ativa.
   - Ver `docs/audits/slice-6a-active-status-enforcement-report-2026-06-17.md` para detalhes do slice.

2. `RegisterProviderAccountHandler` usa tenant/application do corpo, nao do contexto autenticado.
   - Specs: `001-multi-tenancy.md`, `002-api-authentication.md`, `011-security-and-compliance.md`.
   - Evidencia: `ProviderAccountsController` encaminha o request body ao handler; o handler valida apenas se a application informada existe.
   - Risco: uma chamada autenticada para uma application pode tentar registrar provider account em outro escopo conhecido.

3. Endpoints de criacao de tenant/application divergem entre spec e middleware.
   - Specs: `009-api-contract.md` e ADR-0004.
   - Evidencia: a documentacao trata `POST /api/v1/tenants` e `POST /api/v1/applications` como anonimo/admin futuro, mas o middleware exige API Key para caminhos nao anonimos.
   - Risco: bootstrap operacional inconsistente; a primeira criacao via API fica sem caminho claro.

4. Worker dedicado de outbox usa dispatcher no-op.
   - Specs: `007-webhook-processing.md`, `010-database-contract.md`, `012-observability-and-audit.md`.
   - Evidencia: `PaymentHub.Worker` registra `NoopApplicationWebhookDispatcher`; `OutboxDispatcherWorker` marca evento como enviado apos o dispatcher retornar sucesso.
   - Risco: eventos internos podem ser considerados enviados sem entrega HTTP real quando o host worker e usado para processar outbox.

5. `ApplicationClient.WebhookSecret` e persistido em texto claro.
   - Specs: `011-security-and-compliance.md`, `012-observability-and-audit.md`.
   - Evidencia: migracao cria coluna `application_clients.webhook_secret` como texto; nao foi encontrado mecanismo de protecao equivalente ao de provider credentials.
   - Risco: vazamento de banco permite forjar webhooks internos assinados para aplicacoes clientes.

### P2 - Médio

1. Assinatura de webhooks externos nao e validada nos adapters reais.
   - Evidencia: adapters de Stripe, MercadoPago e AbacatePay fazem parsing/canonicalizacao, mas nao rejeitam payloads por assinatura invalida.
   - Observacao: como os adapters reais ainda sao skeleton, este gap e medio, mas deve virar alto antes de producao.

2. Projeto de testes de integracao nao possui testes descobertos.
   - Evidencia: `dotnet test PaymentHub.slnx` informa que `PaymentHub.IntegrationTests` nao contem testes.
   - Impacto: specs de banco, API middleware, checkout e workers nao estao cobertas em nivel e2e/integracao.

3. Acoes administrativas sensiveis nao gravam `AuditLog`.
   - Evidencia: entidade, DbSet e repositorio existem, mas nao ha chamadas de gravacao nos handlers de tenant, application ou provider account.
   - Impacto: auditoria operacional incompleta para criacao/rotacao/revogacao e configuracao sensivel.

4. Integridade referencial no banco e parcial.
   - Evidencia: migracao inicial cria FK apenas de `payment_attempts.payment_id` para `payments.id`.
   - Impacto: tenant/application/provider/payment podem ficar inconsistentes se houver escrita fora dos repositorios ou falhas de aplicacao.

5. Documentacao de arquitetura usa contrato antigo de assinatura de webhook interno.
   - Evidencia: `docs/architecture/overview.md` descreve `HMAC-SHA256(payload, WebhookSecret)`, enquanto a implementacao usa headers com timestamp e assinatura sobre `timestamp.payload`.
   - Impacto: integradores podem implementar verificacao divergente se seguirem a arquitetura em vez da doc de API atual.

### P3 - Baixo

1. Algumas docs mantem linhas longas e detalhes operacionais densos.
   - Impacto: menor legibilidade, sem impacto funcional direto.

2. Provider accounts podem crescer em regras de default/ambiente.
   - Evidencia: ha indice composto por tenant/application/provider/environment, mas nao unicidade para `is_default`.
   - Impacto: depende da decisao de produto; hoje o router pode resolver pelo default da application ou provider informado.

## Gaps entre specs e código

- Specs esperam bloqueio por tenant/application inativo; codigo verifica existencia e API Key, mas nao status ativo.
- Specs tratam endpoints de tenant/application como anonimo/admin futuro; codigo exige API Key por middleware para estes caminhos.
- Specs de webhooks externos mencionam validacao de assinatura quando suportada; codigo ainda nao implementa validacao nos adapters reais.
- Specs de auditoria esperam registro de acoes sensiveis; codigo possui infraestrutura, mas nao grava eventos administrativos.
- Specs/arquitetura esperam entrega de webhooks internos; worker dedicado atual pode marcar como enviado usando dispatcher no-op.

## Gaps entre código e specs

- `ApplicationClient.WebhookSecret` existe e e usado para assinar webhooks internos, mas a spec de seguranca nao define claramente protecao em repouso para esse segredo.
- O modelo EF permite ausencia de FKs para varias referencias por ID; a spec de banco lista tabelas/indices, mas nao explicita quais FKs sao obrigatorias no MVP.
- O fluxo real de bootstrap depende de uma politica de admin/seed nao formalizada nas specs.

## Gaps de testes

- `PaymentHub.IntegrationTests` nao possui testes descobertos.
- Faltam testes de integracao para migrations, indices unicos e constraints.
- Faltam testes API/e2e para checkout autenticado, replay de idempotency key, conflito de idempotencia, tenant/app inativo e provider indisponivel.
- Faltam testes de worker para `WebhookProcessorWorker` e `OutboxDispatcherWorker` com banco real ou fixture de integracao.
- Faltam testes de autorizacao para impedir body tenant/application divergente do contexto autenticado.
- Faltam testes de audit log para acoes administrativas.
- Faltam testes de assinatura invalida/ausente para webhooks de providers reais quando estes adapters forem ativados.

## Gaps de segurança

- `webhook_secret` de application fica persistido sem protecao em repouso equivalente a provider credentials.
- Provider webhook signatures ainda nao sao validadas nos adapters reais.
- Cadastro de provider account nao vincula explicitamente o tenant/application do request ao contexto autenticado.
- Status de tenant/application nao e enforceado em autenticacao/checkout.
- Bootstrap/admin endpoints precisam de politica explicita para evitar tanto deadlock operacional quanto exposicao indevida.

## Gaps de documentação

- `docs/architecture/overview.md` esta defasado no detalhe de assinatura HMAC dos webhooks internos.
- A documentacao precisa explicitar o fluxo de bootstrap/admin para tenant, application, API Key e provider account.
- A spec de seguranca deve decidir se `ApplicationClient.WebhookSecret` precisa de criptografia, hashing, KMS ou rotacao.
- A spec de banco deve explicitar quais FKs sao obrigatorias e quais referencias permanecem apenas logicas por decisao de MVP.

## Recomendações

1. Corrigir primeiro os P1 de autorizacao e operacao: status ativo, contexto autenticado em provider accounts, bootstrap/admin policy e dispatcher real do worker.
2. Proteger `webhook_secret` em repouso ou formalizar uma decisao de risco com rotacao e mitigacoes.
3. Adicionar testes de integracao minimos para banco, middleware, checkout e workers.
4. Implementar validacao de assinatura nos providers reais antes de qualquer uso fora de sandbox.
5. Atualizar docs de arquitetura e specs para refletir o contrato real de assinatura e o fluxo de bootstrap.
6. Definir explicitamente FKs obrigatorias no contrato de banco e alinhar migracoes.

## Próximos passos sugeridos

1. Slice de seguranca: enforcement de `TenantStatus.Active` e `ApplicationStatus.Active` no middleware e/ou handlers.
2. Slice de autorizacao: provider account deve derivar tenant/application do `ITenantContext` ou rejeitar divergencia.
3. Slice operacional: substituir `NoopApplicationWebhookDispatcher` no worker por dispatcher HTTP real ou separar worker de inbox/outbox por configuracao explicita.
4. Slice de testes: criar primeira fixture de integracao com Postgres para migrations e indices criticos.
5. Slice documental: atualizar `overview.md`, specs de seguranca e API de bootstrap/admin.

## Comandos executados

- `git status --short`
- `sed -n '1,220p' AGENTS.md`
- `sed -n '1,220p' docs/harness/workflow.md`
- `sed -n '1,220p' docs/harness/validation.md`
- `sed -n '1,220p' docs/harness/security.md`
- `sed -n '1,220p' docs/specs/000-mvp-scope.md`
- `sed -n '1,260p' docs/specs/001-multi-tenancy.md`
- `sed -n '1,260p' docs/specs/002-api-authentication.md`
- `sed -n '1,260p' docs/specs/003-domain-model.md`
- `sed -n '1,260p' docs/specs/004-checkout-flow.md`
- `sed -n '1,260p' docs/specs/006-provider-integration.md`
- `sed -n '1,260p' docs/specs/007-webhook-processing.md`
- `sed -n '1,260p' docs/specs/010-database-contract.md`
- `sed -n '1,260p' docs/specs/011-security-and-compliance.md`
- `sed -n '1,260p' docs/specs/012-observability-and-audit.md`
- `sed -n '1,260p' docs/specs/013-testing-strategy.md`
- `sed -n '1,260p' docs/database/schema.md`
- `sed -n '1,260p' docs/architecture/overview.md`
- `sed -n '1,260p' docs/architecture/mvp-decisions.md`
- `sed -n '1,220p' docs/adr/ADR-0001-use-dotnet-10-and-ef-core-10.md`
- `sed -n '1,220p' docs/adr/ADR-0004-api-key-server-to-server.md`
- `sed -n '1,220p' docs/adr/ADR-0005-provider-status-canonicalization.md`
- `rg "AddForeignKey|ForeignKey|table.ForeignKey" src/PaymentHub.Infrastructure.Postgres/Migrations`
- `rg -i "cvv|card|wallet|split|rabbit|kafka|service bus|recurr|recorr|antifraude" src docs tests`
- `rg "AuditLog|IAuditLog|AddAsync\(new AuditLog" src tests`
- `rg --files docs/adr tests src/PaymentHub.Infrastructure.Postgres/Migrations`
- `sed -n '1,260p' src/PaymentHub.Infrastructure.Postgres/Migrations/20260616232151_InitialSchema.cs`
- `sed -n '260,520p' src/PaymentHub.Infrastructure.Postgres/Migrations/20260616232151_InitialSchema.cs`

Validacoes executadas neste ciclo de auditoria:

- `dotnet restore PaymentHub.slnx`: sucesso; projetos ja estavam atualizados.
- `dotnet build PaymentHub.slnx`: sucesso, 0 warnings, 0 errors.
- `dotnet test PaymentHub.slnx --filter ProcessWebhookEventHandlerTests`: sucesso, 9 testes.
- `dotnet test PaymentHub.slnx`: sucesso, 64 testes unitarios; projeto `PaymentHub.IntegrationTests` sem testes descobertos.

## Arquivos analisados

- `AGENTS.md`
- `README.md`
- `docs/harness/project-context.md`
- `docs/harness/workflow.md`
- `docs/harness/validation.md`
- `docs/harness/security.md`
- `docs/harness/learnings.md`
- `docs/specs/000-mvp-scope.md`
- `docs/specs/001-multi-tenancy.md`
- `docs/specs/002-api-authentication.md`
- `docs/specs/003-domain-model.md`
- `docs/specs/004-checkout-flow.md`
- `docs/specs/006-provider-integration.md`
- `docs/specs/007-webhook-processing.md`
- `docs/specs/010-database-contract.md`
- `docs/specs/011-security-and-compliance.md`
- `docs/specs/012-observability-and-audit.md`
- `docs/specs/013-testing-strategy.md`
- `docs/database/schema.md`
- `docs/architecture/overview.md`
- `docs/architecture/mvp-decisions.md`
- `docs/adr/ADR-0001-use-dotnet-10-and-ef-core-10.md`
- `docs/adr/ADR-0002-use-postgres-inbox-outbox-in-mvp.md`
- `docs/adr/ADR-0003-hosted-checkout-only.md`
- `docs/adr/ADR-0004-api-key-server-to-server.md`
- `docs/adr/ADR-0005-provider-status-canonicalization.md`
- `src/PaymentHub.Api/Program.cs`
- `src/PaymentHub.Api/Middleware/ApiKeyAuthenticationMiddleware.cs`
- `src/PaymentHub.Api/Middleware/HttpTenantContext.cs`
- `src/PaymentHub.Api/Controllers/ApplicationsController.cs`
- `src/PaymentHub.Api/Controllers/CheckoutsController.cs`
- `src/PaymentHub.Api/Controllers/HealthController.cs`
- `src/PaymentHub.Api/Controllers/PaymentsController.cs`
- `src/PaymentHub.Api/Controllers/ProviderAccountsController.cs`
- `src/PaymentHub.Api/Controllers/ProviderWebhooksController.cs`
- `src/PaymentHub.Api/Controllers/TenantsController.cs`
- `src/PaymentHub.Application/Abstractions/*`
- `src/PaymentHub.Application/Checkouts/*`
- `src/PaymentHub.Application/Payments/*`
- `src/PaymentHub.Application/ProviderAccounts/*`
- `src/PaymentHub.Application/Tenants/*`
- `src/PaymentHub.Application/Webhooks/*`
- `src/PaymentHub.Domain/Entities/*`
- `src/PaymentHub.Domain/Enums/*`
- `src/PaymentHub.Domain/Services/*`
- `src/PaymentHub.Infrastructure.Postgres/PaymentHubDbContext.cs`
- `src/PaymentHub.Infrastructure.Postgres/Configurations/EntityConfigurations.cs`
- `src/PaymentHub.Infrastructure.Postgres/Migrations/20260616232151_InitialSchema.cs`
- `src/PaymentHub.Infrastructure.Postgres/Migrations/PaymentHubDbContextModelSnapshot.cs`
- `src/PaymentHub.Infrastructure.Postgres/Repositories/Repositories.cs`
- `src/PaymentHub.Infrastructure.Postgres/Security/*`
- `src/PaymentHub.Infrastructure.Postgres/Outbox/*`
- `src/PaymentHub.Providers/*`
- `src/PaymentHub.Worker/Program.cs`
- `src/PaymentHub.Worker/WebhookProcessorWorker.cs`
- `src/PaymentHub.Worker/OutboxDispatcherWorker.cs`
- `tests/PaymentHub.UnitTests/Api/ApiKeyAuthenticationMiddlewareTests.cs`
- `tests/PaymentHub.UnitTests/Application/CreateCheckoutHandlerTests.cs`
- `tests/PaymentHub.UnitTests/Application/ProcessWebhookEventHandlerTests.cs`
- `tests/PaymentHub.UnitTests/Domain/*`
- `tests/PaymentHub.UnitTests/Infrastructure/*`
- `tests/PaymentHub.IntegrationTests/PaymentHub.IntegrationTests.csproj`

---

## Nota sobre nomes de specs (adicionado em 2026-06-17 na correcao de bootstrap)

Esta auditoria foi produzida antes da estrutura de specs ser consolidada. Os nomes de specs usados ao longo deste documento refletem os nomes existentes no momento da auditoria, alguns dos quais nao correspondem aos nomes atuais dos arquivos em `docs/specs/`.

O conteudo dos achados permanece valido. Para navegar para as specs atuais, use o mapeamento abaixo.

| Nome usado nesta auditoria | Nome atual em `docs/specs/` | Observacao |
| -------------------------- | --------------------------- | ---------- |
| `001-multi-tenancy.md` | `002-multitenancy-and-authentication.md` | Renomeado e consolidado com autenticacao. |
| `002-api-authentication.md` | `002-multitenancy-and-authentication.md` | Consolidado no mesmo arquivo de multitenancy. |
| `004-checkout-flow.md` | `005-checkout-creation.md` | Renomeado. |
| `006-provider-integration.md` | `008-provider-adapters.md` | Renomeado; foco em adapter contract. |
| `007-webhook-processing.md` | `006-provider-webhooks.md` + `007-inbox-outbox-workers.md` | Dividido em dois arquivos: webhooks externos e workers/outbox. |
| `009-api-contract.md` | `009-api-contracts.md` | Correcao ortografica no nome (faltava 's'). |
| `003-domain-model.md` | `003-domain-model.md` | Inalterado. |
| `010-database-contract.md` | `010-database-contract.md` | Inalterado. |
| `011-security-and-compliance.md` | `011-security-and-compliance.md` | Inalterado. |
| `012-observability-and-audit.md` | `012-observability-and-audit.md` | Inalterado. |
| `013-testing-strategy.md` | `013-testing-strategy.md` | Inalterado. |

Para o indice completo de specs atuais, ver `docs/specs/000-spec-index.md`.
