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

    // ---------------------------------------------------------------------------------------------
    // Slice 7-A.7 — safe LastError transitions. These methods persist ONLY a category name (and
    // optionally an HTTP status code) to LastError. They must never accept or persist arbitrary
    // text — the dispatcher exception message lives in structured logs, not in the database.
    // ---------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(WebhookDispatcherCategory.NetworkError)]
    [InlineData(WebhookDispatcherCategory.Timeout)]
    [InlineData(WebhookDispatcherCategory.UnprotectFailure)]
    [InlineData(WebhookDispatcherCategory.MissingWebhookUrl)]
    [InlineData(WebhookDispatcherCategory.UnexpectedDispatcherError)]
    public void MarkRetryWithCategory_ShouldPersistCategoryNameAndScheduleNextAttempt(
        WebhookDispatcherCategory category)
    {
        var outbox = new OutboxEvent(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            "payment.approved", "{}");
        var now = new DateTime(2026, 6, 26, 12, 0, 0, DateTimeKind.Utc);

        outbox.MarkRetryWithCategory(category, RetryPolicy.NextRetryAt(1, now)!.Value);

        outbox.RetryCount.Should().Be(1);
        outbox.Status.Should().Be(OutboxEventStatus.Pending);
        outbox.LastError.Should().Be(category.ToString());
        outbox.NextRetryAt.Should().NotBeNull();
    }

    [Theory]
    [InlineData(WebhookDispatcherCategory.NetworkError)]
    [InlineData(WebhookDispatcherCategory.Timeout)]
    [InlineData(WebhookDispatcherCategory.UnprotectFailure)]
    [InlineData(WebhookDispatcherCategory.MissingWebhookUrl)]
    [InlineData(WebhookDispatcherCategory.UnexpectedDispatcherError)]
    public void MarkFailedWithCategory_ShouldPersistCategoryNameAndSetFailed(
        WebhookDispatcherCategory category)
    {
        var outbox = new OutboxEvent(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            "payment.approved", "{}");

        outbox.MarkFailedWithCategory(category);

        outbox.RetryCount.Should().Be(1);
        outbox.Status.Should().Be(OutboxEventStatus.Failed);
        outbox.LastError.Should().Be(category.ToString());
        outbox.NextRetryAt.Should().BeNull();
    }

    [Theory]
    [InlineData(500)]
    [InlineData(429)]
    [InlineData(404)]
    [InlineData(503)]
    public void MarkRetryWithStatus_ShouldPersistCategoryAndStatusCodeAndScheduleNextAttempt(
        int statusCode)
    {
        var outbox = new OutboxEvent(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            "payment.approved", "{}");
        var now = new DateTime(2026, 6, 26, 12, 0, 0, DateTimeKind.Utc);

        outbox.MarkRetryWithStatus(WebhookDispatcherCategory.HttpFailure, statusCode,
            RetryPolicy.NextRetryAt(1, now)!.Value);

        outbox.RetryCount.Should().Be(1);
        outbox.Status.Should().Be(OutboxEventStatus.Pending);
        outbox.LastError.Should().Be($"HttpFailure: status={statusCode}");
        outbox.NextRetryAt.Should().NotBeNull();
    }

    [Theory]
    [InlineData(500)]
    [InlineData(429)]
    [InlineData(404)]
    [InlineData(503)]
    public void MarkFailedWithStatus_ShouldPersistCategoryAndStatusCodeAndSetFailed(
        int statusCode)
    {
        var outbox = new OutboxEvent(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            "payment.approved", "{}");

        outbox.MarkFailedWithStatus(WebhookDispatcherCategory.HttpFailure, statusCode);

        outbox.RetryCount.Should().Be(1);
        outbox.Status.Should().Be(OutboxEventStatus.Failed);
        outbox.LastError.Should().Be($"HttpFailure: status={statusCode}");
        outbox.NextRetryAt.Should().BeNull();
    }
}