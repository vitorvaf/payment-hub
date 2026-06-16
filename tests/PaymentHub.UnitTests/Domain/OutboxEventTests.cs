using FluentAssertions;
using PaymentHub.Domain.Entities;
using PaymentHub.Domain.Enums;
using PaymentHub.Domain.Services;

namespace PaymentHub.UnitTests.Domain;

public class OutboxEventTests
{
    [Fact]
    public void MarkSent_ShouldSetSentStatusAndTimestamp()
    {
        var outbox = new OutboxEvent(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            "payment.approved", "{}");

        outbox.MarkSent();

        outbox.Status.Should().Be(OutboxEventStatus.Sent);
        outbox.SentAt.Should().NotBeNull();
        outbox.NextRetryAt.Should().BeNull();
    }

    [Fact]
    public void MarkRetry_ShouldIncrementRetryAndScheduleNextAttempt()
    {
        var outbox = new OutboxEvent(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            "payment.approved", "{}");
        var now = new DateTime(2026, 6, 16, 12, 0, 0, DateTimeKind.Utc);

        outbox.MarkRetry("timeout", RetryPolicy.NextRetryAt(1, now)!.Value);

        outbox.RetryCount.Should().Be(1);
        outbox.Status.Should().Be(OutboxEventStatus.Pending);
        outbox.LastError.Should().Be("timeout");
        outbox.NextRetryAt.Should().NotBeNull();
    }

    [Fact]
    public void MarkFailed_ShouldSetFailedAndClearNextRetry()
    {
        var outbox = new OutboxEvent(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            "payment.approved", "{}");

        outbox.MarkFailed("permanent");

        outbox.Status.Should().Be(OutboxEventStatus.Failed);
        outbox.NextRetryAt.Should().BeNull();
        outbox.LastError.Should().Be("permanent");
    }
}
