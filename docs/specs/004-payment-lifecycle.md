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
- `PaymentAttemptStatus.Succeeded` nao significa apenas "webhook processado"; para webhooks, ele deve representar status financeiro positivo (`Approved`, `Refunded`, `Chargeback` no MVP).
- `Rejected`, `Failed`, `Expired` e `Cancelled` devem registrar attempt como `Failed`.
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

O dominio cria `Payment` com `Status = Created`.
O handler atual persiste o `Payment`, chama o provider na mesma transacao logica e,
em sucesso, usa `AttachProviderResult(..., PaymentStatus.Pending)` antes de salvar.
Na pratica, o banco tende a observar o pagamento ja em `Pending` quando a criacao termina.

Esse comportamento e aceitavel para o MVP, mas a spec consolida a semantica:
`Created` e o estado interno antes do provider;
`Pending` e o estado da resposta bem-sucedida.

## Provider AbacatePay (Slice 2-A — 2026-06-27)

Adapter funcional para **Checkout Transparente PIX** em sandbox/devMode.
Endpoints Reais usados pelo `AbacatePayClient`:

- `POST /transparents/create` — cria o PIX e retorna `brCode`, `brCodeBase64`,
  `expiresAt` e `providerPaymentId`. O adapter sintetiza
  `abacatepay://pix/<providerPaymentId>` como `CheckoutUrl` para manter
  simetria com a API publica (consumidores que precisarem renderizar o QR
  Code de forma imediata devem consumir `RawResponseJson` ate que a
  response publica exponha `brCode`/`brCodeBase64` em micro-slice proprio).
- `GET /transparents/check?id=<providerPaymentId>` — consulta de status.
  Exposto apenas em `IAbacatePayClient` (sincronizacao interna).
- `POST /transparents/simulate-payment?id=<providerPaymentId>` — opt-in via
  `Providers:AbacatePay:AllowDevModeSimulation`; **default `false` em
  producao**. Cobertura de teste sobe no `AbacatePayClientTests`.

Mapeamento canonico extendido (status adicionais alem dos basicos ja
documentados em `008-provider-adapters.md`):

| Status bruto AbacatePay | Status canonico |
|-------------------------|-----------------|
| `PENDING` | `Pending` |
| `PAID` / `APPROVED` | `Approved` |
| `EXPIRED` | `Expired` |
| `CANCELLED` / `CANCELED` | `Cancelled` |
| `REFUNDED` | `Refunded` |
| `REDEEMED` | `Approved` (decisao documentada em teste — equivalente funcional de um PIX ja resgatado) |
| `UNDER_DISPUTE` | `Pending` (decisao explicita: ate existir anti-fraude/MVP de chargeback, mantemos em estado intermedio ate conciliacao) |
| `FAILED` | `Failed` |
| (status desconhecido) | `Pending` (default seguro) |

Caminho canonico de criacao via adapter:

1. `CreateCheckoutHandler` resolve `ProviderAccount`, propaga
   `ProviderAccountId`, `Environment`, `EncryptedCredentials` via
   `CreateCheckoutProviderRequest` (campos opcionais, backward-compatible).
2. `AbacatePayProviderAdapter.CreateCheckoutAsync` chama
   `ICredentialProtector.Unprotect` para extrair a API key da conta
   registrada. Se a conta nao tiver credencial valida o adapter retorna
   `Success=false` com mensagem segura (sem vazar chave).
3. Client HTTP envia `POST /transparents/create` com Bearer; resposta
   `200 + success=true` e mapeada para `PaymentStatus.Pending` via
   `PaymentStatusMapper`. O `Payment` e persistido em `Pending` com
   `PaymentAttemptStatus.Succeeded`, mesmo padrao de outros providers.
4. Falha categorizada (400/401/403/404/429/5xx, network, timeout,
   `success=false`) NAO e logada com API key/body/brCodeBase64; apenas
   `AbacatePayErrorCategory` + `StatusCode?` + `IsTransient` chegam a
   logs e ao `PaymentAttempt.LastError`.
