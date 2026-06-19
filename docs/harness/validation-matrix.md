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
| 2 | Fake | Unit | `dotnet test --filter PaymentStatusMapperTests` | Todos passando | Passando | `PASS` | 2026-06-17 |
| 2 | AbacatePay | Unit | `dotnet test --filter AbacatePayAdapterTests` | Todos passando | `PENDING` | `PENDING` | — |
| 2 | AbacatePay | Integration | Webhook AbacatePay sandbox | 202 Accepted e status canonico | `PENDING` | `PENDING` | — |

---

## Phase 3 — Webhooks Externos e Internos

| Phase | Slice | Tipo | Comando ou Acao | Resultado esperado | Resultado real | Status | Data |
|-------|-------|------|-----------------|--------------------|---------------|--------|------|
| 3 | Webhook | Unit | `dotnet test --filter ProcessWebhookEventHandlerTests` | Todos passando | 9 passando | `PASS` | 2026-06-17 |
| 3 | Outbox | Unit | Dispatcher HTTP real registrado | Worker entrega HTTP sem noop | `PENDING` | `PENDING` | — |
| 3 | Workers | Integration | Workers com banco real | Inbox e Outbox processados | `PENDING` | `PENDING` | — |

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
| 6 | 6-C | Unit | WebhookSecret criptografado no banco | Nao visievel em texto claro | `PENDING` | `PENDING` | — |
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
| 7 | 7-A | Unit | Dispatcher HTTP real envia POST para webhook URL | 2xx marca OutboxEvent como Sent | `PENDING` | `PENDING` | — |
| 7 | 7-A | Unit | Dispatcher HTTP nao-2xx gera retry | RetryPolicy aplicada | `PENDING` | `PENDING` | — |
| 7 | Workers | Integration | `OutboxDispatcherWorker` com banco real | OutboxEvent marcado como Sent apos HTTP 200 | `PENDING` | `PENDING` | — |
| 7 | Workers | Integration | `WebhookProcessorWorker` com banco real | WebhookEvent processado e OutboxEvent criado | `PENDING` | `PENDING` | — |

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
