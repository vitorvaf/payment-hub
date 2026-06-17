# Modelo de Dominio

## Objetivo

Formalizar entidades, invariantes e dados proibidos no dominio do Payment Hub.

## Escopo

- Entidades principais: `Tenant`, `ApplicationClient`, `ProviderAccount`, `ApiKey`, `Payment`, `PaymentAttempt`, `WebhookEvent`, `OutboxEvent`, `AuditLog`, `IdempotencyKey`.
- Campos essenciais, relacionamentos e comportamentos.

## Fora de escopo

- Migrations detalhadas; ver `010-database-contract.md`.
- Novas entidades para split, wallet ou recorrencia.

## Regras obrigatorias

- `Payment` nao pode conter numero de cartao, validade, CVV ou PAN mascarado como dado obrigatorio.
- `Payment.Amount` e persistido como inteiro em centavos (`amount_in_cents`).
- Moeda inicial suportada: `BRL`.
- `ExternalReference` vincula pagamento ao sistema cliente.
- `ProviderPaymentId` vincula pagamento ao provider.
- Entidades com tenant/application devem carregar esses ids explicitamente.

## Contratos

| Entidade | Responsabilidade | Invariantes principais | Nao deve conter |
|----------|------------------|------------------------|-----------------|
| Tenant | Organizacao raiz | slug unico, status controlado | secrets de provider |
| ApplicationClient | Sistema consumidor | pertence a tenant, status controlado | API Key em claro |
| ProviderAccount | Credencial/config provider | tenant + application + provider | credencial em claro |
| ApiKey | Autenticacao S2S | hash unico, prefixo auditavel | chave em claro persistida |
| Payment | Estado canonico | valor positivo, moeda, status | dados de cartao/CVV |
| PaymentAttempt | Tentativa de provider/processamento | pertence a payment | payload sensivel bruto |
| WebhookEvent | Inbox externo | provider, payload bruto, status de processamento | regra pesada no controller |
| OutboxEvent | Evento interno pendente | tenant/application, event type, payload | secret de webhook |
| AuditLog | Auditoria sensivel | actor, action, entity, metadata | secrets |
| IdempotencyKey | Deduplicacao de checkout | tenant + application + key unico | payload completo se hash basta |

## Criterios de aceite

- Alteracoes de dominio preservam invariantes acima.
- Novos value objects recebem configuracao de persistencia quando necessario.
- Status externos sao mapeados para status canonico antes de atualizar `Payment`.

## Testes esperados

- Construtores rejeitam ids vazios, valores invalidos e referencias ausentes.
- `Payment` inicia em `Created`.
- Transicoes atualizam timestamps e processed_at quando terminal.
- Hash/idempotencia nao persiste dados desnecessarios.

## Arquivos relacionados

- `src/PaymentHub.Domain/Entities/`
- `src/PaymentHub.Domain/ValueObjects/`
- `src/PaymentHub.Infrastructure.Postgres/Configurations/`
