namespace PaymentHub.Application.Abstractions.Context;

public interface ITenantContext
{
    Guid TenantId { get; }
    Guid ApplicationId { get; }
}

public interface IClock
{
    DateTime UtcNow { get; }
}
