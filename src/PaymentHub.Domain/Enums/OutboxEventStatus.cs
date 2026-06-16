namespace PaymentHub.Domain.Enums;

public enum OutboxEventStatus
{
    Pending = 1,
    Processing = 2,
    Sent = 3,
    Failed = 4
}
