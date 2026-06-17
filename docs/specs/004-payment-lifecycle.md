# Ciclo de Vida do Pagamento

## Objetivo

Definir status canonico, transicoes e eventos esperados para pagamentos.

## Escopo

- Status canonico.
- Transicoes permitidas.
- Eventos gerados por mudanca de estado.
- Tratamento de eventos duplicados, atrasados e fora de ordem.
- Gap atual de `Created` versus `Pending` no checkout.

## Fora de escopo

- Regras de negocio da aplicacao cliente apos receber webhook interno.

## Regras obrigatorias

- Status canonico: `Created`, `Pending`, `Processing`, `RequiresAction`, `Approved`, `Rejected`, `Cancelled`, `Expired`, `Refunded`, `Chargeback`, `Failed`.
- `Created`: pagamento criado internamente antes da chamada ao provider.
- `Pending`: provider criou checkout/pagamento pendente e retornou `checkoutUrl`.
- Resposta de criacao de checkout retorna `Pending` quando URL foi criada com sucesso.
- Se a criacao no provider falhar, registrar attempt `Failed` e retornar erro sem gerar evento de sucesso.
- Eventos duplicados nao podem gerar efeitos duplicados.
- Eventos atrasados ou fora de ordem devem ser ignorados, tratados como no-op ou marcados para revisao conforme risco.
- Mesmo status aplicado novamente deve ser tratado como no-op idempotente.
- Transicao invalida nao deve sobrescrever o status atual nem gerar evento interno.

## Contratos

Transicoes base:

| De | Para |
|----|------|
| `Created` | `Pending`, `Failed` |
| `Pending` | `Processing`, `RequiresAction`, `Approved`, `Rejected`, `Cancelled`, `Expired`, `Failed` |
| `Processing` | `Approved`, `Rejected`, `Cancelled`, `Expired`, `Failed` |
| `RequiresAction` | `Processing`, `Approved`, `Rejected`, `Cancelled`, `Expired`, `Failed` |
| `Approved` | `Refunded`, `Chargeback` |
| Terminais | nao devem voltar para estados intermediarios sem regra explicita |

Eventos internos:

- `payment.checkout.created`
- `payment.pending`
- `payment.approved`
- `payment.rejected`
- `payment.expired`
- `payment.cancelled`
- `payment.refunded`
- `payment.chargeback`
- `payment.failed`

## Criterios de aceite

- Toda mudanca relevante de status gera `OutboxEvent` quando a aplicacao cliente precisa ser notificada.
- `PaymentAttempt` registra comunicacoes com provider ou processamento relevante.
- Status bruto do provider nunca vaza como status canonico de API.

## Testes esperados

- Mapeamento de status por provider.
- Transicoes validas e terminais.
- Eventos duplicados e fora de ordem.
- Provider failure no checkout registra attempt failed.

## Arquivos relacionados

- `src/PaymentHub.Domain/Enums/PaymentStatus.cs`
- `src/PaymentHub.Domain/Entities/Payment.cs`
- `src/PaymentHub.Domain/Services/PaymentStatusMapper.cs`
- `src/PaymentHub.Application/Checkouts/CreateCheckoutHandler.cs`

## Gap identificado

O dominio cria `Payment` com `Status = Created`. O handler atual persiste o `Payment`, chama o provider na mesma transacao logica e, em sucesso, usa `AttachProviderResult(..., PaymentStatus.Pending)` antes de salvar. Na pratica, o banco tende a observar o pagamento ja em `Pending` quando a criacao termina. Esse comportamento e aceitavel para o MVP, mas a spec consolida a semantica: `Created` e o estado interno antes do provider; `Pending` e o estado da resposta bem-sucedida.
