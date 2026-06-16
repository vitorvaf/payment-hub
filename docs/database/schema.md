# Schema do banco

Este documento descreve as tabelas criadas pela migration inicial do Payment Gateway MVP.

> Migration: `src/PaymentHub.Infrastructure.Postgres/Migrations/*_InitialSchema.cs`

## Tabelas

### `tenants`

Cadastro de tenants (organização raiz).

| Coluna | Tipo | Restrição |
|--------|------|-----------|
| id | uuid | PK |
| name | varchar(200) | NOT NULL |
| slug | varchar(80) | NOT NULL, UNIQUE |
| status | varchar(32) | NOT NULL (`Active`/`Suspended`/`Disabled`) |
| created_at | timestamptz | NOT NULL |
| updated_at | timestamptz | NOT NULL |

Índices:

- `ux_tenants_slug` UNIQUE
- `ix_tenants_status`

### `application_clients`

Aplicações clientes registradas dentro de um tenant.

| Coluna | Tipo | Restrição |
|--------|------|-----------|
| id | uuid | PK |
| tenant_id | uuid | NOT NULL |
| name | varchar(200) | NOT NULL |
| webhook_url | varchar(2000) | NULL |
| webhook_secret | varchar(500) | NULL |
| default_provider | varchar(32) | NULL |
| status | varchar(32) | NOT NULL |
| created_at | timestamptz | NOT NULL |
| updated_at | timestamptz | NOT NULL |

Índices:

- `ux_application_clients_tenant_id_name` UNIQUE
- `ix_application_clients_tenant_id_status`

### `provider_accounts`

Conta de provedor de pagamento (Abacate Pay, Stripe etc.) para um tenant + aplicação.

| Coluna | Tipo | Restrição |
|--------|------|-----------|
| id | uuid | PK |
| tenant_id | uuid | NOT NULL |
| application_id | uuid | NOT NULL |
| provider_code | varchar(32) | NOT NULL |
| environment | varchar(32) | NOT NULL (`Sandbox`/`Production`) |
| name | varchar(200) | NOT NULL |
| encrypted_credentials | text | NOT NULL (AES) |
| is_default | bool | NOT NULL |
| active | bool | NOT NULL |
| created_at | timestamptz | NOT NULL |
| updated_at | timestamptz | NOT NULL |

Índices:

- `ix_provider_accounts_tenant_id_application_id_provider_code_environment`

### `api_keys`

Chaves de API usadas para autenticação server-to-server.

| Coluna | Tipo | Restrição |
|--------|------|-----------|
| id | uuid | PK |
| tenant_id | uuid | NOT NULL |
| application_id | uuid | NOT NULL |
| name | varchar(200) | NOT NULL |
| key_hash | varchar(500) | NOT NULL, UNIQUE (HMAC-SHA256) |
| key_prefix | varchar(32) | NOT NULL |
| active | bool | NOT NULL |
| created_at | timestamptz | NOT NULL |
| revoked_at | timestamptz | NULL |
| last_used_at | timestamptz | NULL |

Índices:

- `ux_api_keys_key_hash` UNIQUE
- `ix_api_keys_tenant_id_application_id`

### `payments`

Pagamentos canônicos. Não armazena dados sensíveis de cartão.

| Coluna | Tipo | Restrição |
|--------|------|-----------|
| id | uuid | PK |
| tenant_id | uuid | NOT NULL |
| application_id | uuid | NOT NULL |
| external_reference | varchar(200) | NOT NULL |
| amount_in_cents | bigint | NOT NULL |
| currency | varchar(3) | NOT NULL (default `BRL`) |
| selected_provider | varchar(32) | NOT NULL |
| status | varchar(32) | NOT NULL (canônico) |
| provider_payment_id | varchar(200) | NULL |
| checkout_url | varchar(2000) | NULL |
| customer_email | varchar(200) | NULL |
| customer_name | varchar(200) | NULL |
| success_url | varchar(2000) | NULL |
| cancel_url | varchar(2000) | NULL |
| metadata | jsonb | NULL |
| created_at | timestamptz | NOT NULL |
| updated_at | timestamptz | NOT NULL |
| processed_at | timestamptz | NULL |

Índices:

- `ix_payments_tenant_id_application_id`
- `ix_payments_tenant_id_status`
- `ix_payments_tenant_id_application_id_external_reference`
- `ix_payments_selected_provider_provider_payment_id`
- `ix_payments_created_at`

### `payment_attempts`

Cada tentativa de integração com um provedor.

| Coluna | Tipo | Restrição |
|--------|------|-----------|
| id | uuid | PK |
| payment_id | uuid | NOT NULL (FK payments) |
| tenant_id | uuid | NOT NULL |
| application_id | uuid | NOT NULL |
| provider_code | varchar(32) | NOT NULL |
| status | varchar(32) | NOT NULL |
| provider_payment_id | varchar(200) | NULL |
| error_message | varchar(2000) | NULL |
| created_at | timestamptz | NOT NULL |

Índices:

- `ix_payment_attempts_tenant_id_payment_id`

### `webhook_events` (Inbox)

Tabela de entrada para webhooks externos.

| Coluna | Tipo | Restrição |
|--------|------|-----------|
| id | uuid | PK |
| tenant_id | uuid | NULL (preenchido após associação) |
| application_id | uuid | NULL |
| provider_code | varchar(32) | NOT NULL |
| provider_event_id | varchar(200) | NULL |
| event_type | varchar(80) | NOT NULL |
| raw_payload | jsonb | NOT NULL |
| signature | varchar(500) | NULL |
| processing_status | varchar(32) | NOT NULL |
| retry_count | int | NOT NULL |
| last_error | varchar(2000) | NULL |
| processed_at | timestamptz | NULL |
| next_retry_at | timestamptz | NULL |
| received_at | timestamptz | NOT NULL |
| updated_at | timestamptz | NOT NULL |

Índices:

- `ux_webhook_events_provider_code_provider_event_id` UNIQUE parcial (quando `provider_event_id IS NOT NULL`)
- `ix_webhook_events_processing_status_next_retry_at`
- `ix_webhook_events_received_at`

### `outbox_events` (Outbox)

Tabela de saída para webhooks internos.

| Coluna | Tipo | Restrição |
|--------|------|-----------|
| id | uuid | PK |
| tenant_id | uuid | NOT NULL |
| application_id | uuid | NOT NULL |
| event_type | varchar(80) | NOT NULL |
| payload | jsonb | NOT NULL |
| status | varchar(32) | NOT NULL |
| retry_count | int | NOT NULL |
| last_error | varchar(2000) | NULL |
| sent_at | timestamptz | NULL |
| next_retry_at | timestamptz | NULL |
| created_at | timestamptz | NOT NULL |
| updated_at | timestamptz | NOT NULL |

Índices:

- `ix_outbox_events_status_next_retry_at`
- `ix_outbox_events_created_at`

### `audit_logs`

Auditoria de ações administrativas.

| Coluna | Tipo | Restrição |
|--------|------|-----------|
| id | uuid | PK |
| tenant_id | uuid | NULL |
| application_id | uuid | NULL |
| actor | varchar(200) | NOT NULL |
| action | varchar(80) | NOT NULL |
| entity | varchar(120) | NULL |
| entity_id | varchar(120) | NULL |
| metadata | jsonb | NULL |
| created_at | timestamptz | NOT NULL |

Índices:

- `ix_audit_logs_tenant_id_created_at`

### `idempotency_keys`

Tabela de idempotência para criação de checkouts.

| Coluna | Tipo | Restrição |
|--------|------|-----------|
| id | uuid | PK |
| tenant_id | uuid | NOT NULL |
| application_id | uuid | NOT NULL |
| key | varchar(200) | NOT NULL |
| request_hash | varchar(128) | NOT NULL |
| payment_id | uuid | NOT NULL |
| created_at | timestamptz | NOT NULL |

Índices:

- `ux_idempotency_keys_tenant_id_application_id_key` UNIQUE

## Índices únicos críticos

| Tabela | Colunas | Propósito |
|--------|---------|-----------|
| `idempotency_keys` | `(tenant_id, application_id, key)` | Garantir 1 pagamento por chave de idempotência |
| `webhook_events` | `(provider_code, provider_event_id)` | Evitar processamento duplicado de webhooks externos |
| `api_keys` | `key_hash` | Hash único de API Key |
| `tenants` | `slug` | Identificador humano único |
| `application_clients` | `(tenant_id, name)` | Nome único dentro do tenant |

## Extensões futuras

- Particionamento de `webhook_events` e `outbox_events` por mês.
- Adicionar `payment_splits` quando o split financeiro entrar no escopo.
- Adicionar `wallet_balances` quando a wallet entrar no escopo.
