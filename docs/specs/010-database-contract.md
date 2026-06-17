# Contrato de Banco

## Objetivo

Definir tabelas, indices criticos e dados proibidos no PostgreSQL do MVP.

## Escopo

- Tabelas da migration inicial.
- Colunas essenciais, JSONB, constraints e indices.
- Regras futuras de retencao.

## Fora de escopo

- Split, wallet, recorrencia e particionamento imediato.

## Regras obrigatorias

- `payments` nao armazena dados de cartao.
- API Keys sao persistidas apenas como hash e prefixo.
- Credenciais de provider ficam criptografadas ou preparadas para criptografia.
- Payloads JSONB nao devem conter CVV, numero de cartao ou secrets.
- Migrations devem preservar dados e ser revisadas contra esta spec.

## Contratos

Tabelas principais:

- `tenants`: organizacoes raiz.
- `application_clients`: aplicacoes por tenant.
- `provider_accounts`: configuracao de provider por tenant/application.
- `api_keys`: hashes de API Key.
- `payments`: estado canonico de pagamento.
- `payment_attempts`: tentativas de provider/processamento.
- `webhook_events`: Inbox externo.
- `outbox_events`: Outbox interno.
- `audit_logs`: auditoria.
- `idempotency_keys`: deduplicacao de checkout.

Indices criticos:

| Tabela | Indice/colunas |
|--------|----------------|
| `idempotency_keys` | `tenant_id + application_id + key` |
| `webhook_events` | `provider_code + provider_event_id` |
| `api_keys` | `key_hash` |
| `tenants` | `slug` |
| `application_clients` | `tenant_id + name` |
| `payments` | `tenant_id + application_id + external_reference` |

## Criterios de aceite

- Schema documentado em `docs/database/schema.md` permanece coerente com migrations.
- Indices de idempotencia e deduplicacao existem antes de trafego real.
- Campos de valor financeiro usam centavos inteiros.

## Testes esperados

- Unique indexes.
- Mapeamento EF de value objects.
- Persistencia de JSONB.
- Ausencia de campos de cartao/CVV.

## Arquivos relacionados

- `docs/database/schema.md`
- `src/PaymentHub.Infrastructure.Postgres/Migrations/`
- `src/PaymentHub.Infrastructure.Postgres/Configurations/`
