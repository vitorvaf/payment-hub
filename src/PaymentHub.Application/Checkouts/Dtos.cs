namespace PaymentHub.Application.Checkouts;

public sealed record CreateCheckoutRequestDto(
    string ExternalReference,
    CustomerDto Customer,
    IReadOnlyList<CheckoutItemDto> Items,
    string Currency,
    string? SuccessUrl,
    string? CancelUrl,
    Dictionary<string, string>? Metadata);

public sealed record CustomerDto(string? Name, string? Email);

public sealed record CheckoutItemDto(
    string Id,
    string Name,
    int Quantity,
    long UnitAmount);
