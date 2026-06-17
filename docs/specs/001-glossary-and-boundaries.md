# Glossario e Limites

## Objetivo

Padronizar termos e fronteiras de responsabilidade para evitar ambiguidades em implementacoes futuras.

## Escopo

- Termos do dominio de pagamento.
- Limites entre Payment Hub, provider externo e aplicacao cliente.
- Responsabilidades que nao pertencem ao Payment Hub.

## Fora de escopo

- Definir contratos HTTP completos; ver `009-api-contracts.md`.
- Definir schema detalhado; ver `010-database-contract.md`.

## Regras obrigatorias

- Payment Hub e dono do estado canonico do pagamento.
- Provider externo executa checkout hospedado e emite eventos.
- Aplicacao cliente e dona da regra de negocio pos-pagamento.
- Payment Hub nao libera plano premium diretamente; ele notifica a aplicacao cliente.

## Contratos

| Termo | Definicao |
|-------|-----------|
| Tenant | Organizacao raiz que isola configuracao, credenciais e dados. |
| ApplicationClient | Sistema consumidor dentro de um tenant. |
| Provider | Gateway externo, como Fake, AbacatePay, Stripe ou MercadoPago. |
| ProviderAccount | Configuracao de provider para tenant + application. |
| Payment | Estado canonico de um pagamento no Payment Hub. |
| PaymentAttempt | Tentativa de comunicacao ou processamento relacionada ao pagamento. |
| Checkout | Sessao de pagamento hospedada pelo provider. |
| Webhook externo | Evento recebido de provider externo. |
| Webhook interno | Evento enviado pelo Payment Hub para aplicacao cliente. |
| Inbox | Persistencia de webhooks externos antes do processamento. |
| Outbox | Persistencia de eventos internos antes do dispatch. |
| Idempotency-Key | Chave fornecida pelo cliente para evitar criacao duplicada. |
| ExternalReference | Referencia do pagamento no sistema cliente. |
| ProviderPaymentId | Identificador do pagamento no provider. |
| PaymentStatus canonico | Status interno independente de provider. |
| Hosted Checkout | Checkout hospedado fora do Payment Hub. |
| AuditLog | Registro auditavel de acao administrativa ou sensivel. |

## Criterios de aceite

- Novas entidades e endpoints usam estes nomes ou justificam divergencia.
- Regras de negocio pos-pagamento ficam no consumidor, nao no Payment Hub.

## Testes esperados

- Nao aplicavel neste momento.

## Arquivos relacionados

- `docs/architecture/overview.md`
- `src/PaymentHub.Domain/Entities/`
- `src/PaymentHub.Domain/Enums/`
