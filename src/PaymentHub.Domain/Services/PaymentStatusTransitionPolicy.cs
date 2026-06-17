using PaymentHub.Domain.Enums;

namespace PaymentHub.Domain.Services;

public static class PaymentStatusTransitionPolicy
{
    public static bool CanTransition(PaymentStatus current, PaymentStatus next)
    {
        if (current == next) return true;

        return current switch
        {
            PaymentStatus.Created => next is PaymentStatus.Pending or PaymentStatus.Failed,
            PaymentStatus.Pending => next is PaymentStatus.Processing
                or PaymentStatus.RequiresAction
                or PaymentStatus.Approved
                or PaymentStatus.Rejected
                or PaymentStatus.Cancelled
                or PaymentStatus.Expired
                or PaymentStatus.Failed,
            PaymentStatus.Processing => next is PaymentStatus.Approved
                or PaymentStatus.Rejected
                or PaymentStatus.Cancelled
                or PaymentStatus.Expired
                or PaymentStatus.Failed,
            PaymentStatus.RequiresAction => next is PaymentStatus.Processing
                or PaymentStatus.Approved
                or PaymentStatus.Rejected
                or PaymentStatus.Cancelled
                or PaymentStatus.Expired
                or PaymentStatus.Failed,
            PaymentStatus.Approved => next is PaymentStatus.Refunded or PaymentStatus.Chargeback,
            _ => false
        };
    }

    public static bool IsTerminal(PaymentStatus status)
        => status is PaymentStatus.Rejected
            or PaymentStatus.Cancelled
            or PaymentStatus.Expired
            or PaymentStatus.Refunded
            or PaymentStatus.Chargeback
            or PaymentStatus.Failed;
}
