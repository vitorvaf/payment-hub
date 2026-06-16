using FluentAssertions;
using PaymentHub.Domain.Entities;
using PaymentHub.Domain.Enums;
using PaymentHub.Domain.Services;

namespace PaymentHub.UnitTests.Domain;

public class WebhookEventTests
{
    [Fact]
    public void Constructor_ShouldStartAsPending()
    {
        var webhook = BuildEvent();

        webhook.ProcessingStatus.Should().Be(WebhookProcessingStatus.Pending);
        webhook.RetryCount.Should().Be(0);
    }

    [Fact]
    public void MarkProcessed_ShouldSetProcessedStatusAndClearRetries()
    {
        var webhook = BuildEvent();
        webhook.MarkProcessing();
        webhook.MarkFailed("transient", DateTime.UtcNow.AddMinutes(1));
        webhook.MarkProcessing();
        webhook.MarkProcessed();

        webhook.ProcessingStatus.Should().Be(WebhookProcessingStatus.Processed);
        webhook.RetryCount.Should().Be(1);
        webhook.NextRetryAt.Should().BeNull();
        webhook.LastError.Should().BeNull();
    }

    [Fact]
    public void MarkFailed_ShouldIncrementRetryAndScheduleNextAttempt()
    {
        var webhook = BuildEvent();
        var now = new DateTime(2026, 6, 16, 12, 0, 0, DateTimeKind.Utc);

        webhook.MarkFailed("oops", RetryPolicy.NextRetryAt(1, now));

        webhook.ProcessingStatus.Should().Be(WebhookProcessingStatus.Pending);
        webhook.RetryCount.Should().Be(1);
        webhook.LastError.Should().Be("oops");
        webhook.NextRetryAt.Should().NotBeNull();
    }

    [Fact]
    public void MarkPermanentlyFailed_ShouldSetFailedStatus()
    {
        var webhook = BuildEvent();
        webhook.MarkPermanentlyFailed("nope");

        webhook.ProcessingStatus.Should().Be(WebhookProcessingStatus.Failed);
        webhook.RetryCount.Should().Be(1);
        webhook.NextRetryAt.Should().BeNull();
    }

    private static WebhookEvent BuildEvent() => new(
        Guid.NewGuid(),
        ProviderCode.Fake,
        "payment.updated",
        "{\"id\":\"fake_1\",\"status\":\"approved\"}",
        "evt-1",
        "sig-abc");
}
