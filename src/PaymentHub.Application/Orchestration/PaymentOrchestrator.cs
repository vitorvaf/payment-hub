using PaymentHub.Application.Abstractions.Providers;
using PaymentHub.Application.Checkouts;
using PaymentHub.Application.Webhooks;

namespace PaymentHub.Application.Orchestration;

public sealed class PaymentOrchestrator : Abstractions.Providers.IPaymentOrchestrator
{
    private readonly ICreateCheckoutHandler _createCheckout;
    private readonly IProcessWebhookEventHandler _processWebhook;

    public PaymentOrchestrator(
        ICreateCheckoutHandler createCheckout,
        IProcessWebhookEventHandler processWebhook)
    {
        _createCheckout = createCheckout;
        _processWebhook = processWebhook;
    }

    public async Task<CreateCheckoutResponse> CreateCheckoutAsync(
        CreateCheckoutCommand command,
        CancellationToken cancellationToken)
    {
        var request = new CreateCheckoutRequestDto(
            command.ExternalReference,
            new CustomerDto(command.CustomerName, command.CustomerEmail),
            command.Items.Select(i => new CheckoutItemDto(i.Id, i.Name, i.Quantity, i.UnitAmount)).ToList(),
            command.Currency,
            command.SuccessUrl,
            command.CancelUrl,
            string.IsNullOrWhiteSpace(command.MetadataJson)
                ? null
                : System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(command.MetadataJson));

        return await _createCheckout.HandleAsync(
            command.TenantId,
            command.ApplicationId,
            command.IdempotencyKey,
            request,
            command.RequestedProviderCode,
            cancellationToken);
    }

    public Task ProcessWebhookAsync(ProcessWebhookEventCommand command, CancellationToken cancellationToken)
        => _processWebhook.ProcessAsync(command.WebhookEventId, cancellationToken);
}
