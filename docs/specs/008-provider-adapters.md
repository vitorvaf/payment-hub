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
| AbacatePay | Skeleton ate tarefa de integracao real |
| Stripe | Skeleton ate tarefa de integracao real |
| MercadoPago | Skeleton ate tarefa de integracao real |

## Criterios de aceite

- Provider code e estavel.
- Falha de provider retorna erro controlado.
- Parser de webhook retorna provider payment id, event type e status bruto mapeavel.

## Testes esperados

- Fake cria checkout e parseia webhook.
- Mappers de status por provider.
- Provider desconhecido.
- Falha de provider nao persiste dado sensivel em logs.

## Arquivos relacionados

- `src/PaymentHub.Application/Abstractions/Providers/IPaymentProviderAdapter.cs`
- `src/PaymentHub.Application/Abstractions/Providers/ProviderModels.cs`
- `src/PaymentHub.Infrastructure.Providers/`
