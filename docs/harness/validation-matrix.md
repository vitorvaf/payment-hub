# Matriz de Validacao

Registro de validacoes executadas por phase e slice. Atualizar sempre que uma validacao for executada.

Formato de status: `PASS` | `FAIL` | `SKIPPED` | `PENDING`

---

## Como usar

1. Ao iniciar um slice, adicione as linhas correspondentes com status `PENDING`.
2. Ao executar cada validacao, preencha o resultado real e atualize o status.
3. Falhas devem ser investigadas antes de considerar o slice concluido.
4. Apos conclusao da phase, registre o resultado consolidado no relatorio de auditoria.

---

## Phase 0 — Produto, Arquitetura e Fronteiras

| Phase | Slice | Tipo | Comando ou Acao | Resultado esperado | Resultado real | Status | Data |
|-------|-------|------|-----------------|--------------------|---------------|--------|------|
| 0 | Bootstrap | Build | `dotnet build PaymentHub.slnx` | 0 erros, 0 warnings | 0 erros, 0 warnings | `PASS` | 2026-06-17 |
| 0 | Bootstrap | Unit | `dotnet test PaymentHub.slnx` | Todos os testes passando | 64 testes passando | `PASS` | 2026-06-17 |

---

## Phase 1 — Core Domain MVP e API

| Phase | Slice | Tipo | Comando ou Acao | Resultado esperado | Resultado real | Status | Data |
|-------|-------|------|-----------------|--------------------|---------------|--------|------|
| 1 | Domain | Build | `dotnet build PaymentHub.slnx` | 0 erros | 0 erros, 0 warnings | `PASS` | 2026-06-17 |
| 1 | Domain | Unit | `dotnet test PaymentHub.slnx` | >= 70 testes passando | 70 passando | `PASS` | 2026-06-17 |
| 1 | Checkout | Unit | `dotnet test --filter CreateCheckoutHandlerTests` | Todos passando | Passando | `PASS` | 2026-06-17 |
| 1 | Middleware | Unit | `dotnet test --filter ApiKeyAuthenticationMiddlewareTests` | Todos passando | 11 passando | `PASS` | 2026-06-17 |
| 1 | Integration | Integration | `dotnet test PaymentHub.IntegrationTests` | >= 1 teste passando | 0 testes descobertos | `FAIL` | 2026-06-17 |

---

## Phase 2 — Primeiro Adapter de Provider

| Phase | Slice | Tipo | Comando ou Acao | Resultado esperado | Resultado real | Status | Data |
|-------|-------|------|-----------------|--------------------|---------------|--------|------|
| 2 | Fake | Unit | `dotnet test --filter FakePaymentProvider` | Todos passando | Passando | `PASS` | 2026-06-17 |
| 2 | Fake | Unit | `dotnet test --filter PaymentStatusMapperTests` | Todos passando | 13 testes (9 Fake/Stripe/MercadoPago + 4 AbacatePay novos) | `PASS` | 2026-06-27 |
| 2 | AbacatePay | Unit | `dotnet test --filter AbacatePayClientTests` | Todos passando | 40 passando (Bearer header, envelope, status code 400/401/403/404/429/5xx, timeout, network, simulation gating) | `PASS` | 2026-06-27 (Slice 2-A) |
| 2 | AbacatePay | Unit | `dotnet test --filter AbacatePayProviderAdapterTests` | Todos passando | 17 passando (unprotect, mapeamento de status, payload PIX, ProviderPaymentId, ausencia de leak) | `PASS` | 2026-06-27 (Slice 2-A) |
| 2 | AbacatePay | Build | `dotnet build PaymentHub.slnx` | 0 erros, 0 warnings | 0 erros, 0 warnings em 9 projetos | `PASS` | 2026-06-27 (Slice 2-A) |
| 2 | AbacatePay | Full Suite | `dotnet test PaymentHub.slnx` | >= 348 testes (291 baseline + 57 AbacatePay), zero regressao | 348 passando | `PASS` | 2026-06-27 (Slice 2-A) |
| 2 | AbacatePay | Provider Regression | `dotnet test --filter "FullyQualifiedName~Provider"` | >= 72 testes, Fake/Stripe/MercadoPago intactos | 72 passando | `PASS` | 2026-06-27 (Slice 2-A) |
| 2 | AbacatePay | Integration Regression | `dotnet test PaymentHub.IntegrationTests` | 10 testes Slice 1-IT preservados | 10 passando | `PASS` | 2026-06-27 (Slice 2-A) |
| 2 | AbacatePay | Architecture | `scripts/agent-architecture-check.sh` | Camadas Clean preservadas; Provider e Application sem dependências proibidas | Passou | `PASS` | 2026-06-27 (Slice 2-A) |
| 2 | AbacatePay | Docs | `scripts/agent-docs-check.sh` | harness e OpenCode integros | Passou | `PASS` | 2026-06-27 (Slice 2-A) |
| 2 | AbacatePay | Diff | `git diff --check` | Sem warnings | Sem warnings | `PASS` | 2026-06-27 (Slice 2-A) |
| 2 | AbacatePay | Sandbox end-to-end | Webhook AbacatePay sandbox real (chave de fato) | Status canonico via HMAC | NAO executado neste slice: webhooks externos completos foram para Slice 2-B. Cobertura de mapper e status cobre o dominio sem chamada externa real. | `SKIPPED` | 2026-06-27 (Slice 2-B pendente) |
| 2 | 2-B | Build | `dotnet build PaymentHub.slnx` | 0 erros, 0 warnings | 0 erros, 0 warnings em 9 projetos | `PASS` | 2026-06-29 (Slice 2-B) |
| 2 | 2-B | Full Suite | `dotnet test PaymentHub.slnx` | >= 418 testes (348 Slice 2-A baseline + 70 novos do Slice 2-B), zero regressao | 418 passando | `PASS` | 2026-06-29 (Slice 2-B) |
| 2 | 2-B | AbacatePay Coverage | `dotnet test --filter "FullyQualifiedName~AbacatePay"` | HMAC verifier + normalizer + adapter + handler | 125 passando | `PASS` | 2026-06-29 (Slice 2-B) |
| 2 | 2-B | Webhook Coverage | `dotnet test --filter "FullyQualifiedName~Webhook"` | Handler + controller + adapter passam | 193 passando | `PASS` | 2026-06-29 (Slice 2-B) |
| 2 | 2-B | Provider Coverage | `dotnet test --filter "FullyQualifiedName~Provider"` | Fake/Stripe/MercadoPago + AbacatePay intactos | 135 passando | `PASS` | 2026-06-29 (Slice 2-B) |
| 2 | 2-B | HMAC Failure Path | Adapter retorna `IsValid=false` quando body adulterado | Teste passa | Passou (5 cenarios: signature mismatch, tampered body, malformed base64, missing secret, missing header) | `PASS` | 2026-06-29 (Slice 2-B) |
| 2 | 2-B | Normalizer Failure Path | Payload vazio/malformed/null/unsupported retorna invalid | Teste passa | Passou (5 cenarios) | `PASS` | 2026-06-29 (Slice 2-B) |
| 2 | 2-B | Provider Account Routing | Handler resolve ProviderAccount por (tenantId, applicationId) e desprotege webhookSecret | Teste passa | Passou (3 cenarios: routing feliz, ProviderAccount ausente, credentials sem webhookSecret) | `PASS` | 2026-06-29 (Slice 2-B) |
| 2 | 2-B | Fail-Fast 401 | AbacatePay sem `X-Webhook-Signature` retorna 401 antes de persistir | Teste passa | Passou (case-insensitive + fallback `X-Provider-Signature` + prioridade) | `PASS` | 2026-06-29 (Slice 2-B) |
| 2 | 2-B | No Secret Leak | `ErrorMessage`/`LastError`/`OutboxEvent.LastError` NAO contem `webhookSecret`/signature/body bruto | Teste passa | Passou (3 cenarios explicitos + varios implícitos em todos os testes) | `PASS` | 2026-06-29 (Slice 2-B) |
| 2 | 2-B | Architecture | `scripts/agent-architecture-check.sh` | Camadas Clean preservadas | Passou | `PASS` | 2026-06-29 (Slice 2-B) |
| 2 | 2-B | Docs | `scripts/agent-docs-check.sh` | harness e OpenCode integros | Passou | `PASS` | 2026-06-29 (Slice 2-B) |
| 2 | 2-B | Diff | `git diff --check` | Sem warnings | Sem warnings | `PASS` | 2026-06-29 (Slice 2-B) |
| 2 | 2-B | Docs | `docs/audits/slice-2b-abacatepay-webhooks-report-2026-06-29.md` | Relatorio final + Q1-Q5 respondidas + gaps remanescentes | Criado | `PASS` | 2026-06-29 (Slice 2-B) |
| 7 | 3-IT | Build | `dotnet build PaymentHub.slnx` | 0 erros, 0 warnings | 0 erros, 0 warnings em 9 projetos | `PASS` | 2026-06-29 (Slice 3-IT) |
| 7 | 3-IT | E2E AbacatePay Checkout | `dotnet test --filter "FullyQualifiedName~AbacatePayCheckoutE2ETests"` | 4 testes P1: checkout happy path + webhook valido + idempotencia + assinatura ausente | 4 passando (9.6s) | `PASS` | 2026-06-29 (Slice 3-IT) |
| 7 | 3-IT | E2E Full | `dotnet test PaymentHub.slnx --filter "FullyQualifiedName~EndToEnd"` | >= 4 testes | 4 passando | `PASS` | 2026-06-29 (Slice 3-IT) |
| 7 | 3-IT | Integration Suite | `dotnet test tests/PaymentHub.IntegrationTests/PaymentHub.IntegrationTests.csproj` | >= 14 testes (10 Slice 1-IT + 4 Slice 3-IT) | 14 passando (10.0s) | `PASS` | 2026-06-29 (Slice 3-IT) |
| 7 | 3-IT | Full Suite | `dotnet test PaymentHub.slnx` | >= 422 testes (418 baseline + 4 E2E), zero regressao | 422 passando | `PASS` | 2026-06-29 (Slice 3-IT) |
| 7 | 3-IT | HMAC E2E | Adapter verifica HMAC sobre body preservado por roundtrip Postgres | Teste passa | Passou (apos Slice 3-IT fix `jsonb -> text` em `webhook_events.raw_payload`) | `PASS` | 2026-06-29 (Slice 3-IT) |
| 7 | 3-IT | Attempt Insert | EF Core emite INSERT (nao UPDATE) para novo `PaymentAttempt` | Teste passa | Passou (apos Slice 3-IT fix `_payments.AddAttemptAsync(attempt, ct)`) | `PASS` | 2026-06-29 (Slice 3-IT) |
| 7 | 3-IT | Idempotency | Webhook duplicado (mesmo `eventId`) retorna mesmo `webhookId` e nao duplica `WebhookEvent` | Teste passa | Passou | `PASS` | 2026-06-29 (Slice 3-IT) |
| 7 | 3-IT | Fail-Fast 401 | AbacatePay sem `X-Webhook-Signature` retorna 401 antes de persistir `WebhookEvent` | Teste passa | Passou | `PASS` | 2026-06-29 (Slice 3-IT) |
| 7 | 3-IT | No Leak | Outbox payload NAO contem `apiKey`/`webhookSecret`/body bruto | Teste passa | Passou | `PASS` | 2026-06-29 (Slice 3-IT) |
| 7 | 3-IT | Outbound Fake Transport | `AbacatePayFakeHttpHandler` captura POST + Bearer + metadata; `ApplicationWebhookCaptureHandler` captura webhook dispatch | Teste passa | Passou | `PASS` | 2026-06-29 (Slice 3-IT) |
| 7 | 3-IT | Architecture | `scripts/agent-architecture-check.sh` | Camadas Clean preservadas | Passou | `PASS` | 2026-06-29 (Slice 3-IT) |
| 7 | 3-IT | Docs | `scripts/agent-docs-check.sh` | harness e OpenCode integros | Passou | `PASS` | 2026-06-29 (Slice 3-IT) |
| 7 | 3-IT | Diff | `git diff --check` | Sem warnings | Sem warnings | `PASS` | 2026-06-29 (Slice 3-IT) |
| 7 | 3-IT | Docs | `docs/audits/slice-3-it-e2e-api-postgres-outbox-provider-report-2026-06-29.md` | Relatorio final + Q1-Q7 respondidas + 2 producoes bugs encontrados (jsonb + EF tracking) | Criado | `PASS` | 2026-06-29 (Slice 3-IT) |
| 7 | 3-IT | **MUST-NOT-REGRESS** | `EntityConfigurations.cs` linha 151 + `Migrations/20260629205545_ChangeRawPayloadToText.cs` | `webhook_events.raw_payload` deve ser `text`, NAO `jsonb` | `text` (auditado) | `PASS` | 2026-06-29 (Slice 3-IT) — Anti-Regression Rule 1 |
| 7 | 3-IT | **MUST-NOT-REGRESS** | `WebhookHandlers.cs:204-218` | `ProcessAsync` deve chamar `_payments.AddAttemptAsync(attempt, ct)` explicitamente | Sim (auditado) | `PASS` | 2026-06-29 (Slice 3-IT) — Anti-Regression Rule 2 |

---

## Phase 3 — Webhooks Externos e Internos

| Phase | Slice | Tipo | Comando ou Acao | Resultado esperado | Resultado real | Status | Data |
|-------|-------|------|-----------------|--------------------|---------------|--------|------|
| 3 | Webhook | Unit | `dotnet test --filter ProcessWebhookEventHandlerTests` | Todos passando | 9 passando | `PASS` | 2026-06-17 |
| 3 | Outbox | Unit | Dispatcher HTTP real registrado | Worker entrega HTTP sem noop | `HttpApplicationWebhookDispatcher` realocado para `Infrastructure.Postgres/Webhooks/`; DI centralizado em `AddPaymentHubPostgres`; `IHttpClientFactory` nomeado | `PASS` | 2026-06-26 (Slice 7-A.1/.2/.3) |
| 3 | Workers | Integration | Workers com banco real | Inbox e Outbox processados | `PENDING` | `PENDING` | — (Slice 1-IT) |

---

## Phase 6 — Seguranca e Confiabilidade

| Phase | Slice | Tipo | Comando ou Acao | Resultado esperado | Resultado real | Status | Data |
|-------|-------|------|-----------------|--------------------|---------------|--------|------|
| 6 | 6-A | Build | `dotnet build PaymentHub.slnx` | 0 erros, 0 warnings | 0 erros, 0 warnings | `PASS` | 2026-06-17 |
| 6 | 6-A | Unit | `dotnet test --filter ApiKeyAuthenticationMiddlewareTests` | Todos passando | 11 testes passando | `PASS` | 2026-06-17 |
| 6 | 6-A | Unit | Tenant inativo retorna 403 | Teste passa | Passou | `PASS` | 2026-06-17 |
| 6 | 6-A | Unit | Application inativa retorna 403 | Teste passa | Passou | `PASS` | 2026-06-17 |
| 6 | 6-A | Unit | API Key ausente/invalida continua retornando 401 | Teste passa | Passou | `PASS` | 2026-06-17 |
| 6 | 6-A | Unit | Tenant/application ativos prosseguem normalmente | Teste passa | Passou | `PASS` | 2026-06-17 |
| 6 | 6-A | Unit | API Key nao vaza em logs/responses | Teste de leak | Passou | `PASS` | 2026-06-17 |
| 6 | 6-A | Unit | Tenant/application inexistentes retornam 401 sem leak | Teste passa | Passou | `PASS` | 2026-06-17 |
| 6 | 6-B | Build | `dotnet build PaymentHub.slnx` | 0 erros, 0 warnings | 0 erros, 0 warnings | `PASS` | 2026-06-18 |
| 6 | 6-B | Unit | `dotnet test PaymentHub.slnx` | Todos os testes passando | 85 testes passando | `PASS` | 2026-06-18 |
| 6 | 6-B | Unit | `ProviderAccount` e criado com `tenantId`/`applicationId` do contexto | Teste passa | Passou | `PASS` | 2026-06-18 |
| 6 | 6-B | Unit | Body com `tenantId`/`applicationId` divergente nao afeta a operacao | Teste passa | Passou | `PASS` | 2026-06-18 |
| 6 | 6-B | Unit | Contexto ausente (tenant/application `Guid.Empty`) nao cria `ProviderAccount` | Teste passa | Passou | `PASS` | 2026-06-18 |
| 6 | 6-B | Unit | Resposta de `POST /api/v1/provider-accounts` nao expoe `ApiKey`, `Secret` ou `EncryptedCredentials` | Teste passa | Passou | `PASS` | 2026-06-18 |
| 6 | 6-B | Unit | Repositorio recebe `ProviderAccount` no escopo correto | Teste passa | Passou | `PASS` | 2026-06-18 |
| 6 | 6-B | Unit | Caminho feliz continua funcionando | Teste passa | Passou | `PASS` | 2026-06-18 |
| 6 | 6-B | Unit | `ApiKeyAuthenticationMiddleware` continua passando 11 testes (sem regressao) | Teste passa | 11 testes passando | `PASS` | 2026-06-18 |
| 6 | 6-C | Build | `dotnet build PaymentHub.slnx` | 0 erros, 0 warnings | 0 erros, 0 warnings | `PASS` | 2026-06-25 |
| 6 | 6-C | Unit | `dotnet test PaymentHub.slnx` | Todos os testes passando | 133 testes passando | `PASS` | 2026-06-25 |
| 6 | 6-C | Unit | `dotnet test --filter "FullyQualifiedName~WebhookSecret"` | Cobre protector, handler, seeder e dispatcher | 25 testes passando | `PASS` | 2026-06-25 |
| 6 | 6-C | Unit | `dotnet test --filter "FullyQualifiedName~Bootstrap"` | Sem regressao | 15 testes passando | `PASS` | 2026-06-25 |
| 6 | 6-C | Unit | `dotnet test --filter "FullyQualifiedName~ApiKeyAuthenticationMiddlewareTests"` | Sem regressao | 11 testes passando | `PASS` | 2026-06-25 |
| 6 | 6-C | Unit | `dotnet test --filter "FullyQualifiedName~ProviderAccount"` | Sem regressao | 15 testes passando | `PASS` | 2026-06-25 |
| 6 | 6-C | Unit | `IWebhookSecretProtector` roundtrip de plaintext | Teste passa | Passou | `PASS` | 2026-06-25 |
| 6 | 6-C | Unit | `IWebhookSecretProtector.Protect` nao retorna plaintext | Teste passa | Passou | `PASS` | 2026-06-25 |
| 6 | 6-C | Unit | `IWebhookSecretProtector.Protect` produz ciphertexts diferentes por chamada | Teste passa | Passou | `PASS` | 2026-06-25 |
| 6 | 6-C | Unit | `IWebhookSecretProtector.Unprotect` falha em payload com purpose incorreto | Teste passa | Passou | `PASS` | 2026-06-25 |
| 6 | 6-C | Unit | `IWebhookSecretProtector` lanca `InvalidOperationException` se chave vazia | Teste passa | Passou | `PASS` | 2026-06-25 |
| 6 | 6-C | Unit | `RegisterApplicationClientHandler` protege `webhookSecret` antes de persistir | Teste passa | Passou | `PASS` | 2026-06-25 |
| 6 | 6-C | Unit | `RegisterApplicationClientHandler` nao expoe secret em DTO | Teste passa | Passou | `PASS` | 2026-06-25 |
| 6 | 6-C | Unit | `RegisterApplicationClientHandler` retorna `hasWebhookSecret=true` quando secret presente | Teste passa | Passou | `PASS` | 2026-06-25 |
| 6 | 6-C | Unit | `RegisterApplicationClientHandler` continua retornando API Key | Teste passa | Passou | `PASS` | 2026-06-25 |
| 6 | 6-C | Unit | `DevelopmentDataSeeder` protege segredo de dev antes de persistir | Teste passa | Passou | `PASS` | 2026-06-25 |
| 6 | 6-C | Unit | `DevelopmentDataSeeder` nao loga raw webhook secret | Teste passa | Passou | `PASS` | 2026-06-25 |
| 6 | 6-C | Unit | `HttpApplicationWebhookDispatcher` desprotege segredo antes de assinar | Teste passa | Passou | `PASS` | 2026-06-25 |
| 6 | 6-C | Unit | `HttpApplicationWebhookDispatcher` nao envia request se `Unprotect` falhar | Teste passa | Passou | `PASS` | 2026-06-25 |
| 6 | 6-C | Unit | `HttpApplicationWebhookDispatcher` nao inclui signature quando nao ha secret | Teste passa | Passou | `PASS` | 2026-06-25 |
| 6 | 6-D | Build | `dotnet build PaymentHub.slnx` | 0 erros, 0 warnings | 0 erros, 0 warnings | `PASS` | 2026-06-18 |
| 6 | 6-D | Unit | `dotnet test --filter "FullyQualifiedName~Bootstrap"` | Todos passando | 15 testes passando | `PASS` | 2026-06-18 |
| 6 | 6-D | Unit | `dotnet test PaymentHub.slnx` | >= 106 testes passando | 106 testes passando | `PASS` | 2026-06-18 |
| 6 | 6-D | Unit | `HostBootstrapPolicy.ShouldRunDevelopmentSeed` retorna `false` em `Production` mesmo com `Enabled=true` e `SeedDevelopmentData=true` | Teste passa | Passou | `PASS` | 2026-06-18 |
| 6 | 6-D | Unit | `HostBootstrapPolicy.ShouldRunDevelopmentSeed` retorna `false` em `Production` sem `AllowProductionBootstrap=true` | Teste passa | Passou | `PASS` | 2026-06-18 |
| 6 | 6-D | Unit | `HostBootstrapPolicy.ShouldRunDevelopmentSeed` retorna `true` em `Production` apenas com opt-in explicito | Teste passa | Passou | `PASS` | 2026-06-18 |
| 6 | 6-D | Unit | `HostBootstrapPolicy.ShouldRunDevelopmentSeed` retorna `true` em `Development`/`Test` quando habilitado | Teste passa | Passou | `PASS` | 2026-06-18 |
| 6 | 6-D | Unit | `HostBootstrapPolicy.ShouldRunDevelopmentSeed` retorna `false` quando `Bootstrap:Enabled=false` | Teste passa | Passou | `PASS` | 2026-06-18 |
| 6 | 6-D | Unit | Configuracao ausente produz politica segura | Teste passa | Passou | `PASS` | 2026-06-18 |
| 6 | 6-D | Unit | `DevelopmentDataSeeder` nao executa em `Production` sem opt-in | Teste passa | Passou | `PASS` | 2026-06-18 |
| 6 | 6-D | Unit | `DevelopmentDataSeeder` e idempotente (segunda execucao nao duplica) | Teste passa | Passou | `PASS` | 2026-06-18 |
| 6 | 6-D | Unit | `DevelopmentDataSeeder` cria tenant/application com `Status=Active` | Teste passa | Passou | `PASS` | 2026-06-18 |
| 6 | 6-D | Unit | `DevelopmentDataSeeder` nao loga API Key, secrets, senhas, tokens ou `Bearer` | Teste passa | Passou | `PASS` | 2026-06-18 |
| 6 | 6-D | Unit | `DevelopmentDataSeeder` falha com seguranca quando configuracao esta incompleta | Teste passa | Passou | `PASS` | 2026-06-18 |
| 6 | 6-D | Unit | `ApiKeyAuthenticationMiddleware` continua passando 11 testes (sem regressao) | Teste passa | 11 testes passando | `PASS` | 2026-06-18 |
| 6 | 6-D | Unit | `ProviderAccount` continua passando 15 testes (sem regressao) | Teste passa | 15 testes passando | `PASS` | 2026-06-18 |
| 6 | 6-D | Manual | Criar tenant via API sem API Key previa | Retorno esperado sem deadlock operacional | `PENDING` | `PENDING` | — |

---

## Phase 7 — Workers, Outbox e Processamento Assincrono

| Phase | Slice | Tipo | Comando ou Acao | Resultado esperado | Resultado real | Status | Data |
|-------|-------|------|-----------------|--------------------|---------------|--------|------|
| 7 | 7-A | Build | `dotnet build PaymentHub.slnx` | 0 erros, 0 warnings | 0 erros, 0 warnings | `PASS` | 2026-06-26 |
| 7 | 7-A | Unit | `dotnet test PaymentHub.slnx` | >= 200 testes sem regressao | 281 testes passando | `PASS` | 2026-06-26 |
| 7 | 7-A | Unit | Dispatcher HTTP real envia POST para webhook URL | 2xx marca OutboxEvent como Sent | `HttpApplicationWebhookDispatcher.DispatchAsync` chama `_apps.GetByTenantAndIdAsync(...)`; 2xx -> `MarkSent`; coberto por `HttpApplicationWebhookDispatcherTests` | `PASS` | 2026-06-26 |
| 7 | 7-A | Unit | Dispatcher HTTP nao-2xx gera retry | RetryPolicy aplicada | `LastError = WebhookDispatcherCategory.HttpFailure + statusCode`; `MarkRetryWithStatus`; coberto por testes | `PASS` | 2026-06-26 |
| 7 | 7-A | Unit | Dispatcher aborta HTTP quando `Unprotect` falha | `LastError = UnprotectFailure`; nenhum request enviado | Coberto por `OutboxDispatcherWorkerWithRealDispatcherTests` | `PASS` | 2026-06-26 |
| 7 | 7-A | Unit | Dispatcher marca Failed sem retry quando `WebhookUrl` ausente | `MissingWebhookUrl`; sem retry | Coberto por testes | `PASS` | 2026-06-26 |
| 7 | 7-A | Unit | `OutboxEvent.LastError` nao persiste `ex.Message`/body HTTP | Apenas `(category, statusCode)` em `LastError` | Coberto por testes; `MarkRetryWithStatus` e `MarkFailedWithStatus` sao os unicos metodos publicos | `PASS` | 2026-06-26 |
| 7 | 7-A | Unit | `OutboxDispatcherWorker` usa `IOutboxRepository` + `IOutboxEventStore` + `IClock` | Nao acessa `DbContext` direto; `DateTime.UtcNow` removido | Coberto por `OutboxDispatcherWorkerWithRealDispatcherTests` | `PASS` | 2026-06-26 |
| 7 | 7-A | Unit | `RegisterApplicationClientValidator` rejeita `WebhookUrl` nao-HTTPS / loopback / RFC1918 / link-local / IMDS | Cobertura completa de vetores SSRF | `WebhookUrlValidatorTests` (66+ casos) + `RegisterApplicationClientValidatorTests` (17 testes) | `PASS` | 2026-06-26 |
| 7 | 7-A | Unit | Worker falha no startup se `PaymentHub:WebhookSecretEncryptionKey` ausente | `InvalidOperationException` no startup | Fail-fast em `Worker/Program.cs:53-56` em scope anonimo | `PASS` | 2026-06-26 |
| 7 | 7-A | Architecture | `scripts/agent-architecture-check.sh` | Worker nao depende de Api | Passed | `PASS` | 2026-06-26 |
| 7 | 7-A | Docs | `docs/adr/ADR-0007-webhook-secret-protection.md` + `docs/adr/ADR-0010-real-outbox-dispatcher-location.md` | ADRs `ACCEPTED` + indice atualizado | Criados e indexados | `PASS` | 2026-06-26 |
| 7 | 7-A | Docs | `docs/roadmap/000-payment-hub-roadmap.md` | P1-4 marcado como resolvido | Resolvido | `PASS` | 2026-06-26 |
| 7 | 7-A | Docs | `docs/roadmap/002-phase-status-board.md` | Phase 7 com 0 gaps P1 proprios | 0 gaps P1 proprios | `PASS` | 2026-06-26 |
| 7 | 7-A | Docs | `feature_list.md` PH-WORKER-001 | Concluido | Concluido | `PASS` | 2026-06-26 |
| 7 | Workers | Integration | `OutboxDispatcherWorker` com banco real | OutboxEvent marcado como Sent apos HTTP 200 | `PENDING` | `PENDING` | — (Slice 7-IT) |
| 7 | Workers | Integration | `WebhookProcessorWorker` com banco real | WebhookEvent processado e OutboxEvent criado | `PENDING` | `PENDING` | — (Slice 7-IT) |
| 7 | 1-IT | Integration | `dotnet test tests/PaymentHub.IntegrationTests` | Migrations + repositorios principais + Outbox via Testcontainers | 10 testes passando (Postgres 16-alpine, container compartilhado por run + `TRUNCATE CASCADE` entre testes) | `PASS` | 2026-06-26 (Slice 1-IT) |
| 1 | 1-IT | Integration | `Migrations_ShouldApplySuccessfully_OnEmptyPostgresDatabase` | `MigrateAsync` em banco vazio + `INFORMATION_SCHEMA` | Tabelas `tenants`, `application_clients`, `provider_accounts`, `api_keys`, `payments`, `payment_attempts`, `webhook_events`, `outbox_events`, `audit_logs`, `idempotency_keys` presentes | `PASS` | 2026-06-26 (Slice 1-IT) |
| 1 | 1-IT | Integration | `DbContext_ShouldPersistTenantAndApplicationClient_AndReloadCorrectly` | Roundtrip de `Tenant`/`ApplicationClient` com `Guid`/status preservados | Passou | `PASS` | 2026-06-26 (Slice 1-IT) |
| 1 | 1-IT | Integration | `Tenant_AndApplication_UniqueIndex_ShouldPreventDuplicateSlug` | Indice unico `tenants.slug` recusa duplicata (`DbUpdateException`) | Passou | `PASS` | 2026-06-26 (Slice 1-IT) |
| 6 | 1-IT | Integration | `ApplicationClient_ShouldPersistProtectedWebhookSecret_AndAllowInternalUnprotect` | Plaintext nao persistido + `Unprotect` recupera original + `HasWebhookSecret` | Passou | `PASS` | 2026-06-26 (Slice 1-IT) |
| 6 | 1-IT | Integration | `ApplicationClient_WithoutWebhookSecret_ShouldReportHasWebhookSecretFalse` | `HasWebhookSecret` consistente apos reload | Passou | `PASS` | 2026-06-26 (Slice 1-IT) |
| 1 | 1-IT | Integration | `ProviderAccountRepository_ShouldPersistAndLoadByTenantAndApplication` | `GetDefaultAsync` + `GetByCodeAsync` retornam conta persistida | Passou | `PASS` | 2026-06-26 (Slice 1-IT) |
| 1 | 1-IT | Integration | `ProviderAccountRepository_ShouldRespectsTenantScope` | Conta de outro tenant nao vaza | Passou | `PASS` | 2026-06-26 (Slice 1-IT) |
| 7 | 1-IT | Integration | `OutboxEvent_ShouldPersistPendingProcessingAndSentStates` | Transicoes `Pending` -> `Processing` -> `Sent` via `IOutboxEventStore` | Passou | `PASS` | 2026-06-26 (Slice 1-IT) |
| 7 | 1-IT | Integration | `OutboxEvent_SafeRetry_ShouldPersistCategoryWithoutExceptionMessage` | `MarkRetryWithCategory` grava apenas categoria em `LastError` | Passou | `PASS` | 2026-06-26 (Slice 1-IT) |
| 7 | 1-IT | Integration | `OutboxRepository_ShouldReturnOnlyDispatchablePendingEvents` | Query retorna apenas `Pending` com `NextRetryAt <= now` | Passou (5 estados cobertos; `Processing` orfao documentado como gap) | `PASS` | 2026-06-26 (Slice 1-IT) |
| 7 | 1-IT | Docs | `docs/audits/slice-1-it-postgres-integration-tests-report-2026-06-26.md` | Relatorio final + Q1-Q5 respondidas + gaps remanescentes | Criado | `PASS` | 2026-06-26 (Slice 1-IT) |
| 7 | 1-IT | Architecture | `scripts/agent-architecture-check.sh` | IntegrationTests respeita camadas (Domain/Application/Infrastructure.Postgres) | Passed | `PASS` | 2026-06-26 (Slice 1-IT) |

---

## Phase 4 — Multi-Provider

| Phase | Slice | Tipo | Comando ou Acao | Resultado esperado | Resultado real | Status | Data |
|-------|-------|------|-----------------|--------------------|---------------|--------|------|
| 4 | Stripe | Unit | `dotnet test --filter StripeAdapterTests` | Todos passando | `PENDING` | `PENDING` | — |
| 4 | MercadoPago | Unit | `dotnet test --filter MercadoPagoAdapterTests` | Todos passando | `PENDING` | `PENDING` | — |
| 4 | Routing | Integration | Checkout com provider explicito Stripe | Checkout criado com Stripe | `PENDING` | `PENDING` | — |

---

## Phase 8 — Conciliacao Financeira

| Phase | Slice | Tipo | Comando ou Acao | Resultado esperado | Resultado real | Status | Data |
|-------|-------|------|-----------------|--------------------|---------------|--------|------|
| 8 | Spec | Manual | Spec `015-financial-reconciliation.md` criada | Spec aceita | `PENDING` | `PENDING` | — |

---

## Phase 9 — Relatorios, Metricas e Observabilidade

| Phase | Slice | Tipo | Comando ou Acao | Resultado esperado | Resultado real | Status | Data |
|-------|-------|------|-----------------|--------------------|---------------|--------|------|
| 9 | Health | Manual | `GET /health` | 200 OK | `PENDING` | `PENDING` | — |
| 9 | Metrics | Manual | OpenTelemetry exporta metricas de checkout | Metricas visiveis | `PENDING` | `PENDING` | — |
| 9 | 9-O1 | Build | `dotnet build PaymentHub.slnx` | 0 erros, 0 warnings | 0 erros, 0 warnings em 9 projetos | `PASS` | 2026-07-01 (Slice 9-O1) |
| 9 | 9-O1 | Unit | `dotnet test --filter FullyQualifiedName~CorrelationId` | Helper + middleware + accessor | 21 testes passando | `PASS` | 2026-07-01 (Slice 9-O1) |
| 9 | 9-O1 | Unit | `dotnet test --filter FullyQualifiedName~SafeLog` | `Id`/`Length`/`Flag`/`Category` | 11 testes passando | `PASS` | 2026-07-01 (Slice 9-O1) |
| 9 | 9-O1 | Unit | `dotnet test --filter FullyQualifiedName~PaymentHubMetrics` | Tag whitelist + counters + histograms | 10 testes passando | `PASS` | 2026-07-01 (Slice 9-O1) |
| 9 | 9-O1 | Unit | `dotnet test --filter FullyQualifiedName~NoLeak` | Reflection audit | 2 testes passando | `PASS` | 2026-07-01 (Slice 9-O1) |
| 9 | 9-O1 | Unit | `dotnet test PaymentHub.slnx` | >= 547 testes (489 baseline medido em HEAD~1 + 58 novos em 9-O1 / +44 metodos), zero regressao | 547 testes passando | `PASS` | 2026-07-01 (Slice 9-O1) |
| 9 | 9-O1 | Anti-Leak Gate | `scripts/agent-docs-check.sh` | Regex gate catches `Log*(<token>` para `apiKey`/`webhookSecret`/`rawPayload`/`signature`/`Authorization`/`body` | Gate ativo e verde | `PASS` | 2026-07-01 (Slice 9-O1) |
| 9 | 9-O1 | Architecture | `scripts/agent-architecture-check.sh` | Domain NAO referencia Application; Worker NAO tem IHttpContextAccessor | Passou | `PASS` | 2026-07-01 (Slice 9-O1) |
| 9 | 9-O1 | Diff | `git diff --check` | Sem warnings | Sem warnings | `PASS` | 2026-07-01 (Slice 9-O1) |
| 9 | 9-O1 | Anti-Regression | `webhook_events.raw_payload` continua `text` (Slice 3-IT) | Auditado | `text` | `PASS` | 2026-07-01 (Slice 9-O1) |
| 9 | 9-O1 | Anti-Regression | `provider_accounts.webhook_events` continua `text` (Slice 2-C) | Auditado | `text` | `PASS` | 2026-07-01 (Slice 9-O1) |
| 9 | 9-O1 | Anti-Regression | `outbox_events.payload` continua `jsonb` (Slice 7-IT) | Auditado | `jsonb` | `PASS` | 2026-07-01 (Slice 9-O1) |
| 9 | 9-O1 | Anti-Regression | `webhookSecret` continua sem coluna propria (Slice 2-C) | Auditado | Sem coluna | `PASS` | 2026-07-01 (Slice 9-O1) |
| 9 | 9-O1 | Anti-Regression | `OutboxEvent.LastError` continua apenas categoria enum (Slice 7-A.7) | Auditado em `OutboxEvent.MarkRetryWithStatus`/`MarkFailedWithStatus` | Apenas enum | `PASS` | 2026-07-01 (Slice 9-O1) |
| 9 | 9-O1 | Migration | `20260701000001_AddObservabilityColumns` | Adiciona `correlation_id VARCHAR(64) NULL` em `webhook_events` e `outbox_events`, sem `webhookSecret`, sem `jsonb` | Migration criada; diff manual | `PASS` | 2026-07-01 (Slice 9-O1) |
| 9 | 9-O1 | E2E | `dotnet test --filter FullyQualifiedName~CorrelationIdE2ETests` | Cobre checkout->response header + inbound webhook propagation | 2 testes adicionados (NÃO EXECUTADOS por falta de Docker nesta sessao) | `PENDING` | 2026-07-01 (Slice 9-O1 — E2E pendente requer Docker) |
| 9 | 9-O1 | Docs | `docs/audits/slice-9-o1-observability-minimal-report-2026-07-01.md` | Relatorio final + Q1-Q4 respondidas + gaps remanescentes | A criar | `PENDING` | 2026-07-01 (Slice 9-O1 — audit report) |

---

## Comandos de validacao padrao

Os comandos abaixo devem ser executados em todo ciclo de validacao:

```bash
# Restaurar dependencias
dotnet restore /mnt/hd2/Projects/payment-hub/PaymentHub.slnx

# Build completo
dotnet build /mnt/hd2/Projects/payment-hub/PaymentHub.slnx

# Todos os testes
dotnet test /mnt/hd2/Projects/payment-hub/PaymentHub.slnx

# Verificar status do git (nenhum arquivo sensivel commitado)
git status --short
git diff --stat HEAD
```

---

## Arquivos relacionados

- `docs/harness/definition-of-ready.md`
- `docs/harness/definition-of-done.md`
- `docs/harness/phase-template.md`
- `docs/harness/slice-template.md`
- `docs/audits/spec-adherence-audit-2026-06-17.md`
