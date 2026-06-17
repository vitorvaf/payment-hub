# Indice de Specs do Payment Hub

Data de referencia: 2026-06-17

Este arquivo indexa todas as specs formais do Payment Hub. Cada spec e mapeada para sua fase de roadmap, status e uma descricao de uma linha.

Status possivel: `NOT_STARTED` | `DRAFT` | `REVIEW` | `ACCEPTED` | `DEPRECATED`

---

## Specs existentes

| Arquivo | Fase | Status | Descricao |
|---------|------|--------|-----------|
| `000-mvp-scope.md` | Phase 0 | `ACCEPTED` | Escopo, regras obrigatorias, contratos e criterios de aceite do MVP. |
| `000-spec-index.md` | Todos | `ACCEPTED` | Este arquivo — indice de todas as specs. |
| `001-glossary-and-boundaries.md` | Phase 0 | `ACCEPTED` | Glossario de termos e limites de responsabilidade entre Payment Hub, provider e aplicacao cliente. |
| `001-product-vision-and-boundaries.md` | Phase 0 | `ACCEPTED` | Visao de produto, o que o Payment Hub e e nao e, personas tecnicas e fronteiras de escopo. |
| `002-multitenancy-and-authentication.md` | Phase 1 | `ACCEPTED` | Modelo de multitenancy, isolamento por tenant/application e autenticacao por API Key. |
| `003-domain-model.md` | Phase 1 | `ACCEPTED` | Entidades, invariantes, dados proibidos e modelo de dominio do Payment Hub. |
| `004-payment-lifecycle.md` | Phase 1 | `ACCEPTED` | Status canonico, transicoes permitidas e eventos gerados por mudanca de estado. |
| `005-checkout-creation.md` | Phase 1 | `ACCEPTED` | Contrato de `POST /api/v1/checkouts`, idempotencia, selecao de provider e resposta. |
| `006-provider-webhooks.md` | Phase 3 | `ACCEPTED` | Recebimento, deduplicacao e processamento assincrono de webhooks externos de provider. |
| `007-inbox-outbox-workers.md` | Phase 7 | `ACCEPTED` | Workers, retry policy, estados de Inbox/Outbox e contrato de webhook interno (HMAC). |
| `008-provider-adapters.md` | Phase 2 | `ACCEPTED` | Interface de adapter, expectativas para Fake e providers reais, mapeamento de status. |
| `009-api-contracts.md` | Phase 1 | `ACCEPTED` | Contratos HTTP detalhados de todos os endpoints da API publica. |
| `010-database-contract.md` | Phase 1 | `ACCEPTED` | Schema de banco, tabelas, indices, constraints e regras de persistencia. |
| `011-security-and-compliance.md` | Phase 6 | `ACCEPTED` | Regras de seguranca: API Key, credenciais, HMAC, HTTPS, logs e auditoria. |
| `012-observability-and-audit.md` | Phase 9 | `ACCEPTED` | Observabilidade operacional, metricas, tracing, audit log e health checks. |
| `013-testing-strategy.md` | Phase 1 | `ACCEPTED` | Estrategia de testes: unitarios, integracao, e2e e cobertura minima por slice. |
| `014-job-search-integration.md` | Phase 1 | `ACCEPTED` | Integracao esperada com Job Search / Quero Vagas Tech como primeiro consumidor. |

---

## Specs pendentes de criacao

| Arquivo sugerido | Fase | Status | Descricao |
|-----------------|------|--------|-----------|
| `015-financial-reconciliation.md` | Phase 8 | `NOT_STARTED` | Conciliacao financeira entre Payment Hub e extratos de providers. |
| `016-multi-provider-routing.md` | Phase 4 | `NOT_STARTED` | Roteamento multi-provider, selecao de provider default e failover. |
| `017-admin-panel.md` | Phase 5 | `NOT_STARTED` | Painel administrativo: gestao de tenants, applications, API keys e pagamentos. |
| `018-external-broker.md` | Phase 10 | `NOT_STARTED` | Migracao de Outbox para broker externo (RabbitMQ, Kafka, Azure Service Bus). |

---

## Mapeamento fase a spec

| Phase | Specs relacionadas |
|-------|--------------------|
| Phase 0 | `000-mvp-scope.md`, `001-glossary-and-boundaries.md`, `001-product-vision-and-boundaries.md` |
| Phase 1 | `002-multitenancy-and-authentication.md`, `003-domain-model.md`, `004-payment-lifecycle.md`, `005-checkout-creation.md`, `009-api-contracts.md`, `010-database-contract.md`, `013-testing-strategy.md`, `014-job-search-integration.md` |
| Phase 2 | `008-provider-adapters.md` |
| Phase 3 | `006-provider-webhooks.md` |
| Phase 4 | `016-multi-provider-routing.md` (pendente) |
| Phase 5 | `017-admin-panel.md` (pendente) |
| Phase 6 | `011-security-and-compliance.md` |
| Phase 7 | `007-inbox-outbox-workers.md` |
| Phase 8 | `015-financial-reconciliation.md` (pendente) |
| Phase 9 | `012-observability-and-audit.md` |
| Phase 10 | `018-external-broker.md` (pendente) |

---

## Arquivos relacionados

- `docs/roadmap/000-payment-hub-roadmap.md`
- `docs/adr/` — decisoes arquiteturais aceitas
- `docs/harness/project-context.md`
