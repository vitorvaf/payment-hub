# Indice de Specs do Payment Hub

Data de referencia: 2026-06-17

Este arquivo indexa todas as specs formais do Payment Hub. Cada spec e mapeada para sua fase de roadmap, status e uma descricao de uma linha.

Status possivel: `NOT_STARTED` | `DISCOVERY` | `SPEC_DRAFTED` | `SPEC_REVIEW_REQUIRED` | `READY_FOR_IMPLEMENTATION` | `IMPLEMENTING` | `IMPLEMENTED` | `VALIDATED` | `BLOCKED` | `DEFERRED`

Criterio de mapeamento para specs existentes: o status reflete o estado de implementacao da feature descrita, nao apenas a existencia do documento.

---

## Specs existentes

| Arquivo | Fase | Status | Descricao |
|---------|------|--------|-----------|
| `000-mvp-scope.md` | Phase 0 | `IMPLEMENTED` | Escopo, regras obrigatorias, contratos e criterios de aceite do MVP. |
| `000-spec-index.md` | Todos | `IMPLEMENTED` | Este arquivo — indice de todas as specs. |
| `001-glossary-and-boundaries.md` | Phase 0 | `IMPLEMENTED` | Glossario de termos e limites de responsabilidade entre Payment Hub, provider e aplicacao cliente. |
| `001-product-vision-and-boundaries.md` | Phase 0 | `IMPLEMENTED` | Visao de produto, o que o Payment Hub e e nao e, personas tecnicas e fronteiras de escopo. |
| `002-multitenancy-and-authentication.md` | Phase 1 | `IMPLEMENTING` | Modelo de multitenancy, isolamento por tenant/application e autenticacao por API Key. Gaps P1: enforcement de status ativo. |
| `003-domain-model.md` | Phase 1 | `IMPLEMENTED` | Entidades, invariantes, dados proibidos e modelo de dominio do Payment Hub. |
| `004-payment-lifecycle.md` | Phase 1 | `IMPLEMENTED` | Status canonico, transicoes permitidas e eventos gerados por mudanca de estado. |
| `005-checkout-creation.md` | Phase 1 | `IMPLEMENTED` | Contrato de `POST /api/v1/checkouts`, idempotencia, selecao de provider e resposta. |
| `006-provider-webhooks.md` | Phase 3 | `IMPLEMENTING` | Recebimento, deduplicacao e processamento assincrono de webhooks externos de provider. Gap P1: noop dispatcher. |
| `007-inbox-outbox-workers.md` | Phase 7 | `IMPLEMENTING` | Workers, retry policy, estados de Inbox/Outbox e contrato de webhook interno (HMAC). Gap P1: noop dispatcher. |
| `008-provider-adapters.md` | Phase 2 | `IMPLEMENTING` | Interface de adapter, expectativas para Fake e providers reais, mapeamento de status. Gap P2: adapters reais sao skeleton. |
| `009-api-contracts.md` | Phase 1 | `IMPLEMENTING` | Contratos HTTP detalhados de todos os endpoints da API publica. Gap P1: divergencia nos endpoints bootstrap/admin. |
| `010-database-contract.md` | Phase 1 | `IMPLEMENTING` | Schema de banco, tabelas, indices, constraints e regras de persistencia. Gap P2: FKs parciais. |
| `011-security-and-compliance.md` | Phase 6 | `IMPLEMENTING` | Regras de seguranca: API Key, credenciais, HMAC, HTTPS, logs e auditoria. 5 gaps P1 abertos. |
| `012-observability-and-audit.md` | Phase 9 | `SPEC_DRAFTED` | Observabilidade operacional, metricas, tracing, audit log e health checks. Spec existe; implementacao nao iniciada. |
| `013-testing-strategy.md` | Phase 1 | `IMPLEMENTING` | Estrategia de testes: unitarios, integracao, e2e e cobertura minima por slice. Gap P2: testes de integracao ausentes. |
| `014-job-search-integration.md` | Phase 1 | `SPEC_DRAFTED` | Integracao esperada com Job Search / Quero Vagas Tech como primeiro consumidor. Spec existe; integracao nao validada. |

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
