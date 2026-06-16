using FluentAssertions;
using PaymentHub.Domain.Enums;
using PaymentHub.Domain.Services;

namespace PaymentHub.UnitTests.Domain;

public class PaymentStatusMapperTests
{
    [Theory]
    [InlineData("pending", PaymentStatus.Pending)]
    [InlineData("processing", PaymentStatus.Processing)]
    [InlineData("requires_action", PaymentStatus.RequiresAction)]
    [InlineData("approved", PaymentStatus.Approved)]
    [InlineData("rejected", PaymentStatus.Rejected)]
    [InlineData("cancelled", PaymentStatus.Cancelled)]
    [InlineData("expired", PaymentStatus.Expired)]
    [InlineData("refunded", PaymentStatus.Refunded)]
    [InlineData("failed", PaymentStatus.Failed)]
    public void Fake_MapsAllCanonicalStatuses(string providerStatus, PaymentStatus expected)
    {
        var mapped = PaymentStatusMapper.FromProviderStatus("Fake", providerStatus);
        mapped.Should().Be(expected);
    }

    [Theory]
    [InlineData("succeeded", PaymentStatus.Approved)]
    [InlineData("requires_action", PaymentStatus.RequiresAction)]
    [InlineData("processing", PaymentStatus.Processing)]
    [InlineData("canceled", PaymentStatus.Cancelled)]
    public void Stripe_MapsKnownStatuses(string providerStatus, PaymentStatus expected)
    {
        var mapped = PaymentStatusMapper.FromProviderStatus("Stripe", providerStatus);
        mapped.Should().Be(expected);
    }

    [Theory]
    [InlineData("approved", PaymentStatus.Approved)]
    [InlineData("rejected", PaymentStatus.Rejected)]
    [InlineData("refunded", PaymentStatus.Refunded)]
    [InlineData("charged_back", PaymentStatus.Chargeback)]
    [InlineData("in_process", PaymentStatus.Processing)]
    public void MercadoPago_MapsKnownStatuses(string providerStatus, PaymentStatus expected)
    {
        var mapped = PaymentStatusMapper.FromProviderStatus("MercadoPago", providerStatus);
        mapped.Should().Be(expected);
    }

    [Fact]
    public void UnknownStatus_ShouldDefaultToPending()
    {
        var mapped = PaymentStatusMapper.FromProviderStatus("Unknown", "zzz");
        mapped.Should().Be(PaymentStatus.Pending);
    }
}
