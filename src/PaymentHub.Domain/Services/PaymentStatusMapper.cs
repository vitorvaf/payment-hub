using PaymentHub.Domain.Enums;

namespace PaymentHub.Domain.Services;

public static class PaymentStatusMapper
{
    public static PaymentStatus FromProviderStatus(string providerCode, string providerStatus)
    {
        if (string.IsNullOrWhiteSpace(providerStatus))
            return PaymentStatus.Pending;

        var normalized = providerStatus.Trim().ToLowerInvariant();

        return providerCode.Trim().ToLowerInvariant() switch
        {
            "fake" => MapFake(normalized),
            "abacatepay" or "abacate_pay" => MapAbacatePay(normalized),
            "stripe" => MapStripe(normalized),
            "mercadopago" or "mercado_pago" => MapMercadoPago(normalized),
            _ => MapGeneric(normalized)
        };
    }

    private static PaymentStatus MapFake(string s) => s switch
    {
        "created" => PaymentStatus.Created,
        "pending" => PaymentStatus.Pending,
        "processing" => PaymentStatus.Processing,
        "requires_action" or "requiresaction" => PaymentStatus.RequiresAction,
        "approved" or "paid" or "succeeded" or "success" => PaymentStatus.Approved,
        "rejected" or "declined" => PaymentStatus.Rejected,
        "cancelled" or "canceled" or "voided" => PaymentStatus.Cancelled,
        "expired" => PaymentStatus.Expired,
        "refunded" => PaymentStatus.Refunded,
        "chargeback" => PaymentStatus.Chargeback,
        "failed" or "error" => PaymentStatus.Failed,
        _ => PaymentStatus.Pending
    };

    private static PaymentStatus MapAbacatePay(string s) => s switch
    {
        "pending" => PaymentStatus.Pending,
        "processing" => PaymentStatus.Processing,
        "paid" or "approved" => PaymentStatus.Approved,
        "expired" => PaymentStatus.Expired,
        "cancelled" or "canceled" => PaymentStatus.Cancelled,
        "refunded" => PaymentStatus.Refunded,
        "redeemed" => PaymentStatus.Approved,
        "under_dispute" => PaymentStatus.Pending,
        "failed" => PaymentStatus.Failed,
        _ => PaymentStatus.Pending
    };

    private static PaymentStatus MapStripe(string s) => s switch
    {
        "requires_payment_method" or "requires_confirmation" or "requires_action" => PaymentStatus.RequiresAction,
        "processing" => PaymentStatus.Processing,
        "succeeded" => PaymentStatus.Approved,
        "canceled" => PaymentStatus.Cancelled,
        "requires_capture" => PaymentStatus.Processing,
        _ => PaymentStatus.Pending
    };

    private static PaymentStatus MapMercadoPago(string s) => s switch
    {
        "pending" or "in_process" or "in_mediation" => PaymentStatus.Processing,
        "approved" or "accredited" => PaymentStatus.Approved,
        "rejected" => PaymentStatus.Rejected,
        "cancelled" or "canceled" => PaymentStatus.Cancelled,
        "refunded" => PaymentStatus.Refunded,
        "charged_back" => PaymentStatus.Chargeback,
        "expired" => PaymentStatus.Expired,
        _ => PaymentStatus.Pending
    };

    private static PaymentStatus MapGeneric(string s) => s switch
    {
        "approved" or "paid" or "succeeded" or "success" => PaymentStatus.Approved,
        "rejected" or "declined" or "failed" or "error" => PaymentStatus.Failed,
        "cancelled" or "canceled" or "voided" => PaymentStatus.Cancelled,
        "refunded" => PaymentStatus.Refunded,
        "expired" => PaymentStatus.Expired,
        "processing" => PaymentStatus.Processing,
        "requires_action" or "requiresaction" => PaymentStatus.RequiresAction,
        _ => PaymentStatus.Pending
    };
}
