# Payment Hub Specs

`docs/specs/` e a fonte de verdade para contratos de implementacao do Payment Hub.
Antes de alterar fluxos de pagamento, webhooks, workers, seguranca, banco ou integracoes, leia a spec relacionada e atualize-a quando o contrato mudar.

## Indice

| Spec | Area | Quando ler |
|------|------|------------|
| `000-mvp-scope.md` | Escopo do MVP | Antes de propor nova capacidade ou mudar prioridades. |
| `001-glossary-and-boundaries.md` | Linguagem e limites | Antes de criar nomes, entidades ou responsabilidades novas. |
| `002-multitenancy-and-authentication.md` | Multitenancy e API Key | Antes de alterar middleware, tenants, applications ou autorizacao. |
| `003-domain-model.md` | Modelo de dominio | Antes de alterar entidades, value objects ou invariantes. |
| `004-payment-lifecycle.md` | Ciclo de vida | Antes de alterar status, transicoes, attempts ou eventos. |
| `005-checkout-creation.md` | Criacao de checkout | Antes de alterar API ou handler de checkout. |
| `006-provider-webhooks.md` | Webhooks externos | Antes de alterar controller, parser de provider ou worker de inbox. |
| `007-inbox-outbox-workers.md` | Processamento assincrono | Antes de alterar workers, retries, concorrencia ou dispatch. |
| `008-provider-adapters.md` | Adapters de provider | Antes de criar ou alterar provider real ou Fake. |
| `009-api-contracts.md` | Contratos HTTP | Antes de alterar endpoints, headers, responses ou erros. |
| `010-database-contract.md` | Banco de dados | Antes de alterar schema, migrations, indices ou retencao. |
| `011-security-and-compliance.md` | Seguranca | Antes de mexer em secrets, chaves, logs, webhooks ou dados sensiveis. |
| `012-observability-and-audit.md` | Logs e auditoria | Antes de alterar logs, metricas, correlation id ou AuditLog. |
| `013-testing-strategy.md` | Testes | Antes de definir cobertura de um slice. |
| `014-job-search-integration.md` | Integracao Job Search | Antes de alterar payloads ou eventos consumidos pelo Job Search. |

## Regra de uso

- Specs descrevem o contrato esperado.
- Documentos em `docs/architecture/`, `docs/api/` e `docs/database/` podem explicar detalhes, mas nao substituem a spec.
- Se o codigo atual divergir da spec, registre o gap e implemente correcao em slice separado.
- Mudancas arquiteturais novas devem atualizar ou criar ADR em `docs/adr/`.
