using FluentAssertions;
using PaymentHub.Domain.Services;

namespace PaymentHub.UnitTests.Domain;

public class RetryPolicyTests
{
    [Fact]
    public void NextRetryAt_FirstAttempt_ShouldBeImmediate()
    {
        var now = new DateTime(2026, 6, 16, 12, 0, 0, DateTimeKind.Utc);
        var next = RetryPolicy.NextRetryAt(0, now);
        next.Should().Be(now);
    }

    [Fact]
    public void NextRetryAt_SecondAttempt_ShouldAddOneMinute()
    {
        var now = new DateTime(2026, 6, 16, 12, 0, 0, DateTimeKind.Utc);
        var next = RetryPolicy.NextRetryAt(1, now);
        next.Should().Be(now.AddMinutes(1));
    }

    [Fact]
    public void NextRetryAt_FifthAttempt_ShouldAddOneHour()
    {
        var now = new DateTime(2026, 6, 16, 12, 0, 0, DateTimeKind.Utc);
        var next = RetryPolicy.NextRetryAt(4, now);
        next.Should().Be(now.AddHours(1));
    }

    [Fact]
    public void NextRetryAt_BeyondPolicy_ShouldReturnNull()
    {
        var now = new DateTime(2026, 6, 16, 12, 0, 0, DateTimeKind.Utc);
        var next = RetryPolicy.NextRetryAt(5, now);
        next.Should().BeNull();
    }

    [Fact]
    public void IsExhausted_AfterFiveAttempts_ShouldBeTrue()
    {
        RetryPolicy.IsExhausted(5).Should().BeTrue();
        RetryPolicy.IsExhausted(4).Should().BeFalse();
    }
}
