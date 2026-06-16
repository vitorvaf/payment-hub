using FluentAssertions;
using PaymentHub.Domain.Entities;
using PaymentHub.Domain.Enums;
using PaymentHub.Domain.ValueObjects;

namespace PaymentHub.UnitTests.Domain;

public class PaymentTests
{
    [Fact]
    public void Constructor_ShouldCreatePaymentWithCreatedStatus()
    {
        var payment = BuildPayment();

        payment.Status.Should().Be(PaymentStatus.Created);
        payment.Amount.Amount.Should().Be(2990);
        payment.Currency.Should().Be("BRL");
        payment.Attempts.Should().BeEmpty();
    }

    [Fact]
    public void AttachProviderResult_ShouldSetProviderIdAndUrlAndPendingStatus()
    {
        var payment = BuildPayment();

        payment.AttachProviderResult("fake_abc", "https://fake-checkout.local/p/abc", PaymentStatus.Pending);

        payment.ProviderPaymentId.Should().Be("fake_abc");
        payment.CheckoutUrl.Should().Be("https://fake-checkout.local/p/abc");
        payment.Status.Should().Be(PaymentStatus.Pending);
    }

    [Fact]
    public void ApplyProviderStatus_Approved_ShouldSetProcessedAt()
    {
        var payment = BuildPayment();

        payment.ApplyProviderStatus(PaymentStatus.Approved, "fake_abc");

        payment.Status.Should().Be(PaymentStatus.Approved);
        payment.ProcessedAt.Should().NotBeNull();
    }

    [Fact]
    public void RegisterAttempt_ShouldAddAttempt()
    {
        var payment = BuildPayment();

        var attempt = payment.RegisterAttempt(PaymentAttemptStatus.Succeeded, "fake_abc", null);

        payment.Attempts.Should().HaveCount(1);
        attempt.ProviderPaymentId.Should().Be("fake_abc");
        attempt.Status.Should().Be(PaymentAttemptStatus.Succeeded);
    }

    private static Payment BuildPayment()
    {
        return new Payment(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            "order-1",
            Money.Of(2990, "BRL"),
            ProviderCode.Fake,
            "customer@example.com",
            "Customer",
            "https://example.com/success",
            "https://example.com/cancel");
    }
}
