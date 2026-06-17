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
| 1 | Domain | Unit | `dotnet test PaymentHub.slnx` | >= 64 testes passando | 64 passando | `PASS` | 2026-06-17 |
| 1 | Checkout | Unit | `dotnet test --filter CreateCheckoutHandlerTests` | Todos passando | Passando | `PASS` | 2026-06-17 |
| 1 | Middleware | Unit | `dotnet test --filter ApiKeyAuthenticationMiddlewareTests` | Todos passando | Passando | `PASS` | 2026-06-17 |
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
| 6 | 6-A | Unit | Tenant inativo retorna 403 | Teste passa | `PENDING` | `PENDING` | — |
| 6 | 6-A | Unit | Application inativa retorna 403 | Teste passa | `PENDING` | `PENDING` | — |
| 6 | 6-B | Unit | ProviderAccount usa contexto autenticado | Teste passa | `PENDING` | `PENDING` | — |
| 6 | 6-C | Unit | WebhookSecret criptografado no banco | Nao visievel em texto claro | `PENDING` | `PENDING` | — |
| 6 | 6-D | Unit | AuditLog gravado em acao admin | Log presente no banco | `PENDING` | `PENDING` | — |
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
