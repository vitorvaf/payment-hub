# Payment Hub — Timeline de Desenvolvimento

## Objetivo

Registrar a ordem recomendada de implementacao das fases, com prioridade, esforco, risco e dependencias explicitadas.

Legenda de prioridade:

- `P0` — bloqueia o MVP; sem isso nao ha produto
- `P1` — necessario para MVP confiavel e operacional
- `P2` — importante pos-MVP; entrega valor incremental
- `P3` — evolucao futura de produto; depende de decisao de mercado

---

## Tabela de timeline

| Ordem | Phase | Objetivo | Prioridade | Esforco | Risco | Status | Dependencias |
|-------|-------|---------|-----------|---------|-------|--------|-------------|
| 1 | Phase 0 | Produto, arquitetura e fronteiras | P0 | M | LOW | `IMPLEMENTED` | — |
| 2 | Phase 1 | Core domain MVP e API | P0 | L | MEDIUM | `IMPLEMENTED` | Phase 0 |
| 3 | Phase 2 | Primeiro adapter de provider (AbacatePay) | P0 | M | MEDIUM | `IMPLEMENTED` (Slice 2-A CONCLUIDO 2026-06-27; Slice 2-B CONCLUIDO 2026-06-29) | Phase 1 |
| 4 | Phase 3 | Webhooks externos e internos | P0 | M | MEDIUM | `IMPLEMENTING` (Slice 7-A CONCLUIDO 2026-06-26; Slice 2-B CONCLUIDO 2026-06-29 completa webhooks externos AbacatePay) | Phase 1 |
| 5 | Phase 7 | Workers, Outbox e processamento assincrono | P1 | M | MEDIUM | `IMPLEMENTING` (0 gaps P1 proprios desde 2026-06-26; base de integracao entregue 2026-06-26 via Slice 1-IT; suite E2E do dispatcher entregue 2026-06-30 via Slice 7-IT) | Phase 3, Phase 6 |
| 6 | Phase 6 | Seguranca e confiabilidade | P1 | M | HIGH | `IMPLEMENTING` (0 gaps P1 proprios desde 2026-06-25) | Phase 1 |
| 7 | Phase 4 | Multi-provider (Stripe + MercadoPago) | P1 | L | MEDIUM | `SPEC_DRAFTED` | Phase 2, Phase 3 |
| 8 | Phase 9 | Relatorios, metricas e observabilidade | P2 | L | LOW | `SPEC_DRAFTED` | Phase 6, Phase 7 |
| 9 | Phase 5 | Painel admin | P2 | XL | MEDIUM | `NOT_STARTED` | Phase 1, Phase 6 |
| 10 | Phase 8 | Conciliacao financeira | P2 | XL | HIGH | `NOT_STARTED` | Phase 4, Phase 7 |
| 11 | Phase 10 | Evolucoes futuras de produto (backlog) | P3 | XL | HIGH | `NOT_STARTED` | Phases 0-9 |

---

## Timeline Decision

### Phase 7 antes de Phase 6 (Ordem 5 e 6 na tabela)

**Decisao:** Phase 7 (Workers, Outbox e Processamento Assincrono) foi posicionada na ordem 5, antes de Phase 6 (Seguranca e Confiabilidade, ordem 6).

**Justificativa:** O gap P1-4 (`NoopApplicationWebhookDispatcher` registrado no Worker host) esta tecnicamente dentro do escopo de Phase 7, mas sua correc¸ao e prerequisito para que o ciclo de webhook interno seja considerado minimamente seguro. Corrigir o dispatcher antes de enrijecer a seguranca evita que a Phase 6 seja validada com um componente no-op no caminho critico.

**Risco associado:** Phase 7 depende de Phase 3, que ainda tem o mesmo gap P1-4. Isso significa que ao iniciar Phase 7, o gap P1-4 ja existe no baseline e e o primeiro alvo (Slice 7-A). Se Slice 7-A for adiado dentro de Phase 7, o gap continua exposto durante toda a Phase 7. Mitigacao: Slice 7-A deve ser o primeiro slice de Phase 7, sem excecao.

**Alternativa considerada:** Colocar Phase 6 antes de Phase 7. Vantagem: todos os gaps de autorizacao e segredos seriam resolvidos primeiro. Desvantagem: o ciclo de outbox permanece com dispatcher no-op durante toda a Phase 6, tornando qualquer teste de outbox irreal. A escolha foi priorizar o dispatcher real para que os testes de Phase 6 possam validar o ciclo completo.

**Esta decisao esta refletida em:**

- Tabela de timeline (ordem 5 = Phase 7, ordem 6 = Phase 6).
- `docs/roadmap/002-phase-status-board.md` (Bloco A de trabalho lista Slice 7-A em segundo lugar, imediatamente apos Slice 6-A).
- `docs/audits/specs-bootstrap-report-2026-06-17.md` (timeline sugerida).

---

## Notas de sequenciamento

### Por que Phase 7 antes de Phase 6?

Phase 7 (Workers) depende de Phase 3 (Outbox baseline) e entrega o dispatcher HTTP real que resolve um gap P1 critico.
Phase 6 (Seguranca) pode evoluir em paralelo, mas o dispatcher real precisa existir antes de considerar o ciclo de vida de outbox como seguro.
Ver secao "Timeline Decision" acima para detalhes da decisao e riscos.

### Por que Phase 6 antes de Phase 4?

Phase 4 (Multi-provider) cria novas surfaces de ataque (novos adapters, novos webhooks externos). Enrijecer autorizacao e protecao de secrets antes de adicionar providers reduz risco acumulado.

### Por que Phase 9 antes de Phase 5?

Observabilidade (Phase 9) entrega instrumentacao que o painel admin (Phase 5) pode consumir. Alem disso, Phase 9 tem esforco menor e risco menor, gerando valor operacional mais rapido.

### Por que Phase 8 no final do bloco P2?

Conciliacao financeira (Phase 8) depende de dados reais de providers em producao. Sem Phase 4 (multi-provider funcional) e Phase 7 (workers confiaveis), os dados de referencia para conciliacao sao incompletos.

---

## Slices recomendados para retomada imediata (2026-06-17)

Com base no estado atual (`IMPLEMENTING` em Phases 2, 3, 6, 7) e nos achados P1 da auditoria:

1. **Slice 6-A**: Enforcement de `TenantStatus.Active` e `ApplicationStatus.Active` no middleware. `[CONCLUIDO 2026-06-17]`
2. **Slice 7-A**: Substituir `NoopApplicationWebhookDispatcher` por dispatcher HTTP real no Worker host. `[CONCLUIDO 2026-06-26 — sub-slices 7-A.1 a 7-A.9]`
3. **Slice 6-B**: Corrigir `RegisterProviderAccountHandler` para derivar tenant/application do `ITenantContext`. `[CONCLUIDO 2026-06-18]`
4. **Slice 6-C**: Proteger `ApplicationClient.WebhookSecret` em repouso. `[CONCLUIDO 2026-06-25]`
5. **Slice 6-D**: Politica de bootstrap/admin seed. `[CONCLUIDO 2026-06-18]`
6. **Slice 1-IT**: Criar primeira fixture de integracao com Postgres (migrations + indices). `[CONCLUIDO 2026-06-26]`
7. **Slice 2-A**: Implementar adapter AbacatePay funcional com validacao de assinatura de webhook. `[CONCLUIDO 2026-06-27 — client HTTP com Bearer Token, mapper estendido, Adapter refatorado, DI/options/HttpClient registrados, 57 testes AbacatePay + 348 totais, arquitetura/docs-checks verdes; webhooks externos/HMAC ficam em Slice 2-B]`
8. **Slice 2-B**: Webhooks externos AbacatePay com HMAC-SHA256 (Base64) + normalizacao de eventos `transparent.*` + fail-fast no controller + roteamento por metadata no handler. `[CONCLUIDO 2026-06-29 — 9 testes adapter + 14 testes normalizer + 10 testes verifier + 9 testes handler AbacatePay + 9 testes controller + 418 totais; arquitetura/docs-checks verdes]`
9. **Slice 3-IT**: Testes E2E do API + Postgres (Testcontainers) + adapter AbacatePay real + fakes de transporte outbound, cobrindo o fluxo de checkout e webhooks externos (4 testes P1: `CreateCheckout_WithAbacatePayFake_PersistsPaymentAndOutbox`, `ProviderWebhook_ValidSignature_UpdatesPaymentAndEnqueuesOutbox`, `ProviderWebhook_DuplicateAbacatePayEvent_IsIdempotent`, `ProviderWebhook_MissingSignature_Rejected401WithoutPersist`). `[CONCLUIDO 2026-06-29 — 14 testes integracao (10 Slice 1-IT preservados + 4 novos), 422 totais; arquitetura/docs-checks verdes; 2 producao bugs encontrados e corrigidos (jsonb -> text em `webhook_events.raw_payload`; `_payments.AddAttemptAsync(attempt, ct)` explicito no `ProcessWebhookEventHandler`)]`
10. **Slice 2-C**: Endpoints `PUT`/`GET /api/v1/provider-accounts/{id}/webhook` + 4 colunas non-sensitive em `provider_accounts` + `IProviderWebhookManagementClient` abstraction + feature flag opt-in. `[CONCLUIDO 2026-06-30 — 59 unit + 3 integration = +62 net, 484 totais; arquitetura/docs-checks verdes; anti-regression `provider_accounts.webhook_events` `jsonb -> text` espelhada da Slice 3-IT; cliente HTTP real deferred em Slice 2-C.1]`
11. **Slice 7-IT**: Suite E2E do ciclo Outbox → ApplicationClient webhook (dispatcher real + Postgres real + API real). 7 testes em `tests/PaymentHub.IntegrationTests/EndToEnd/OutboxDispatcherE2ETests.cs` (P1: Sent, HMAC, 500 retry, 429 retry, UnprotectFailure; P2: AbacatePay full flow, no-redispatch de Sent). `[CONCLUIDO 2026-06-30 — 491 totais (484 + 7); arquitetura/docs-checks verdes; adicionou `InternalsVisibleTo("PaymentHub.IntegrationTests")` em `PaymentHub.Worker.csproj`; `ApplicationWebhookCaptureHandler` evoluido com `EnqueueResponse` + helper `InternalWebhookHmac`; detalhes em `docs/audits/slice-7-it-outbox-dispatcher-e2e-report-2026-06-30.md`]`

## Slices concluidos apos a geracao inicial (2026-06-26)

Com base na execucao do Bloco A ate 2026-06-26, os 5 gaps P1 da auditoria de 2026-06-17 estao resolvidos:

| # | Gap | Phase | Slice | Data |
|---|-----|-------|-------|------|
| P1-1 | Tenant/application inativos nao bloqueiam fluxos autenticados | 1, 6 | 6-A | 2026-06-17 |
| P1-2 | `RegisterProviderAccountHandler` usa tenant/application do body | 1, 6 | 6-B | 2026-06-18 |
| P1-3 | Endpoints de tenant/application sem politica de autenticacao | 1, 6 | 6-D | 2026-06-18 |
| P1-4 | Worker dedicado de outbox usa `NoopApplicationWebhookDispatcher` | 3, 7 | 7-A | 2026-06-26 |
| P1-5 | `ApplicationClient.WebhookSecret` persistido em texto claro | 6 | 6-C | 2026-06-25 |

Phase 6 e Phase 7 alcancaram 0 gaps P1 proprios em 2026-06-25 e 2026-06-26, respectivamente. Bloco A esta fechado.

Phase 2 (Phase 2 — Primeiro adapter de provider) atinge seu primeiro marco de implementacao com o Slice 2-A em 2026-06-27. Detalhes em `docs/audits/slice-2a-abacatepay-sandbox-report-2026-06-26.md`. Webhooks externos completos (HMAC, normalizacao de eventos) seguem em **Slice 2-B** (a abrir), dependente apenas de decisao de produto entre Phase 2 e Phase 3.

Slices de integracao end-to-end (middleware/checkout/workers com banco real) seguem dependentes de decisao entre Phase 2 e Phase 7. Slice 1-IT (base de testes de integracao com Postgres) foi concluido em 2026-06-26 e permanece verde apos Slice 2-A, Slice 3-IT, Slice 2-C e Slice 7-IT.

## Status de slices recentes (2026-06-30)

| Slice | Descricao | Data | Notas |
|-------|-----------|------|-------|
| **2-C** | PUT/GET para gerenciar inscricao de webhook AbacatePay + 4 colunas non-sensitive | 2026-06-30 | Cliente HTTP real deferred em 2-C.1 |
| **7-IT** | E2E do dispatcher de Outbox (7 testes) | 2026-06-30 | Multi-instancia continua deferred (7-M1) |

---

## Arquivos relacionados

- `docs/roadmap/000-payment-hub-roadmap.md`
- `docs/roadmap/002-phase-status-board.md`
- `docs/audits/spec-adherence-audit-2026-06-17.md`
- `docs/audits/roadmap-adherence-matrix-2026-06-17.md`
