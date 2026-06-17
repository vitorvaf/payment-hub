namespace PaymentHub.Application.Checkouts;

public sealed class IdempotencyConflictException : InvalidOperationException
{
    public IdempotencyConflictException()
        : base("Idempotency-Key was already used with a different checkout payload.")
    {
    }
}
