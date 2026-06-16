namespace PaymentHub.Domain.Enums;

public enum PaymentStatus
{
    Created = 1,
    Pending = 2,
    Processing = 3,
    RequiresAction = 4,
    Approved = 5,
    Rejected = 6,
    Cancelled = 7,
    Expired = 8,
    Refunded = 9,
    Chargeback = 10,
    Failed = 11
}
