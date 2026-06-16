using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using PaymentHub.Application.Abstractions.Providers;
using PaymentHub.Infrastructure.Providers.Fake;

namespace PaymentHub.UnitTests.Infrastructure.Providers;

public class FakePaymentProviderAdapterTests
{
    private readonly FakePaymentProviderAdapter _adapter = new(NullLogger<FakePaymentProviderAdapter>.Instance);

    [Fact]
    public async Task CreateCheckoutAsync_ShouldReturnProviderIdAndCheckoutUrl()
    {
        var request = new CreateCheckoutProviderRequest(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            "order-1",
            2990,
            "BRL",
            "x@y.com",
            "Customer",
            "https://success",
            "https://cancel",
            null,
            new List<ProviderCheckoutItem>
            {
                new("a", "A", 1, 2990)
            });

        var result = await _adapter.CreateCheckoutAsync(request, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.ProviderPaymentId.Should().StartWith("fake_");
        result.CheckoutUrl.Should().StartWith("https://fake-checkout.local/payments/");
    }

    [Fact]
    public async Task ParseWebhookAsync_ShouldReturnInvalidWhenProviderPaymentIdMissing()
    {
        var result = await _adapter.ParseWebhookAsync(
            new ProviderWebhookRequest("{}", null, new Dictionary<string, string>()),
            CancellationToken.None);

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task ParseWebhookAsync_ShouldExtractProviderPaymentIdAndStatus()
    {
        var body = "{\"id\":\"fake_abc\",\"status\":\"approved\"}";

        var result = await _adapter.ParseWebhookAsync(
            new ProviderWebhookRequest(body, "sig", new Dictionary<string, string>()),
            CancellationToken.None);

        result.IsValid.Should().BeTrue();
        result.ProviderPaymentId.Should().Be("fake_abc");
        result.ProviderStatus.Should().Be("approved");
    }
}
