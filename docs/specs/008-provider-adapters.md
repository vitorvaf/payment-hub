# Adapters de Provider

## Objetivo

Definir o contrato dos adapters de pagamento e a expectativa para Fake e provedores reais.

## Escopo

- Interface de adapter.
- Providers Fake, AbacatePay, Stripe e MercadoPago.
- Mapeamento de status bruto para status canonico.
- Regras de logs e dados sensiveis.

## Fora de escopo

- Implementacao real de AbacatePay, Stripe ou MercadoPago sem tarefa propria.

## Regras obrigatorias

- Adapter deve traduzir vocabulario externo para contratos internos.
- Adapter nao deve vazar payload sensivel, API keys ou secrets em logs.
- Fake pode ser funcional no MVP.
- AbacatePay, Stripe e MercadoPago podem existir como skeleton.
- Integracao real deve vir em slice especifico com testes e docs do provider.

## Contratos

```csharp
public interface IPaymentProviderAdapter
{
    string ProviderCode { get; }

    Task<CreateCheckoutProviderResult> CreateCheckoutAsync(
        CreateCheckoutProviderRequest request,
        CancellationToken cancellationToken);

    Task<ProviderWebhookParseResult> ParseWebhookAsync(
        ProviderWebhookRequest request,
        CancellationToken cancellationToken);
}
```

| Provider | MVP |
|----------|-----|
| Fake | Funcional para dev/testes |
| AbacatePay | **Funcional em sandbox/devMode (Slice 2-A, 2026-06-27)**; cartao e boleto seguem skeleton |
| Stripe | Skeleton ate tarefa de integracao real |
| MercadoPago | Skeleton ate tarefa de integracao real |

### AbacatePay (Slice 2-A — 2026-06-27)

- Modo: Checkout Transparente PIX em sandbox/devMode (sem checkout hospedado nem cartao).
- Endpoints: `POST /transparents/create`, `GET /transparents/check`, `POST /transparents/simulate-payment`.
- Auth: `Authorization: Bearer <api-key>` onde a chave vem de `ProviderAccount.EncryptedCredentials` (AES via `ICredentialProtector`) e nunca logada.
- Configuracao: secao `Providers:AbacatePay` em `appsettings*.json` com `BaseUrl`, `TimeoutSeconds` e `AllowDevModeSimulation`. Default `false` em producao e so `true` em `appsettings.Development.json`.
- Erros classificados em `AbacatePayErrorCategory` (`BadRequest`, `Unauthorized`, `NotFound`, `RateLimited`, `ServerError`, `Network`, `Timeout`, `EnvelopeFailure`, `Unexpected`, `SimulationDisabled`) com flag `IsTransient`. Mensagens do `AbacatePayClientException` NAO incluem API key, body, `brCodeBase64` ou response body.
- Status mapping canonico via `PaymentStatusMapper.MapAbacatePay`. Status adicionais alem dos basicos: `redeemed -> Approved`, `under_dispute -> Pending` (decisao documentada em teste).
- `IPaymentProviderAdapter` NAO foi alterada neste slice: `CheckTransparentPixAsync` e `SimulateTransparentPixPaymentAsync` ficam apenas em `IAbacatePayClient`. A interface so sera estendida quando houver pelo menos um segundo caller concreto.
- Webhooks externos completos (HMAC, normalizacao de eventos) ficam em **Slice 2-B (a abrir)**.

## Criterios de aceite

- Provider code e estavel.
- Falha de provider retorna erro controlado.
- Parser de webhook retorna provider payment id, event type e status bruto mapeavel.
- AbacatePay: API key so chega ao client apos `ICredentialProtector.Unprotect`; nenhuma chave e logada; `RawResponseJson` nao expoe credencial; erros categorizados com `IsTransient`.
- Adapter e tests cobrem Bearer header, unmarshal de envelope, mapeamento de status e ausencia de leak de segredos.

## Testes esperados

- Fake cria checkout e parseia webhook.
- Mappers de status por provider (incluindo AbacatePay: `PENDING`, `PAID`, `APPROVED`, `EXPIRED`, `CANCELLED`, `CANCELED`, `FAILED`, `REFUNDED`, `REDEEMED`, `UNDER_DISPUTE`).
- Provider desconhecido.
- Falha de provider nao persiste dado sensivel em logs.
- AbacatePayClient: 400/401/403/404/429/5xx, timeout/network, `success=false`, parseamento de `brCode`/`brCodeBase64`, simulacao gated por `AllowDevModeSimulation`.
- AbacatePayProviderAdapter: unprotect + extract apiKey, payload com amount/customer/metadata, ProviderPaymentId retornado, status mapeado, sintese de CheckoutUrl `abacatepay://pix/<id>`.

## Arquivos relacionados

- `src/PaymentHub.Application/Abstractions/Providers/IPaymentProviderAdapter.cs`
- `src/PaymentHub.Application/Abstractions/Providers/ProviderModels.cs`
- `src/PaymentHub.Infrastructure.Providers/AbacatePay/AbacatePayProviderAdapter.cs`
- `src/PaymentHub.Infrastructure.Providers/AbacatePay/AbacatePayClient.cs`
- `src/PaymentHub.Infrastructure.Providers/AbacatePay/AbacatePayOptions.cs`
- `src/PaymentHub.Infrastructure.Providers/AbacatePay/AbacatePayErrorCategory.cs`
- `src/PaymentHub.Infrastructure.Providers/AbacatePay/AbacatePayClientException.cs`
- `src/PaymentHub.Infrastructure.Providers/AbacatePay/IAbacatePayClient.cs`
- `src/PaymentHub.Infrastructure.Providers/AbacatePay/Models/*`
- `src/PaymentHub.Infrastructure.Providers/ProvidersServiceCollectionExtensions.cs`
- `tests/PaymentHub.UnitTests/Infrastructure/Providers/AbacatePay/AbacatePayClientTests.cs`
- `tests/PaymentHub.UnitTests/Infrastructure/Providers/AbacatePay/AbacatePayProviderAdapterTests.cs`
- `tests/PaymentHub.UnitTests/Domain/PaymentStatusMapperTests.cs`
- `tests/PaymentHub.UnitTests/Support/FakeCredentialProtector.cs`
