using System.Diagnostics;
using FluentAssertions;
using PaymentHub.Application.Observability;
using PaymentHub.UnitTests.Support;

namespace PaymentHub.UnitTests.Observability;

/// <summary>
/// Tests for <see cref="PaymentHubMetrics"/>. Slice 9-O1 introduces 13
/// counters and 3 histograms; tests assert the instruments are registered
/// under the canonical meter name and that the tag-whitelist guard rejects
/// non-allowlisted keys.
/// </summary>
public class PaymentHubMetricsTests
{
    [Fact]
    public void Meter_ShouldExposeCanonicalName()
    {
        PaymentHubMetrics.MeterName.Should().Be("PaymentHub");
        PaymentHubMetrics.Meter.Name.Should().Be("PaymentHub");
    }

    [Fact]
    public void Counter_ShouldRecordIncrements()
    {
        using var collector = new InMemoryMetricsCollector();

        PaymentHubMetrics.CheckoutsCreatedTotal.Add(1,
            new KeyValuePair<string, object?>(PaymentHubMetrics.TagKeys.Provider, "AbacatePay"));
        PaymentHubMetrics.CheckoutsCreatedTotal.Add(2,
            new KeyValuePair<string, object?>(PaymentHubMetrics.TagKeys.Provider, "AbacatePay"));

        var measurements = collector.For("paymenthub_checkouts_created_total");
        measurements.Should().HaveCount(2);
        measurements[0].Value.Should().Be(1L);
        measurements[1].Value.Should().Be(2L);
        measurements[0].Tags[PaymentHubMetrics.TagKeys.Provider].Should().Be("AbacatePay");
    }

    [Fact]
    public void Histogram_ShouldRecordSamples()
    {
        using var collector = new InMemoryMetricsCollector();

        PaymentHubMetrics.CheckoutDurationMs.Record(123.5,
            new KeyValuePair<string, object?>(PaymentHubMetrics.TagKeys.Status, "success"));

        var measurements = collector.For("paymenthub_checkout_duration_ms");
        measurements.Should().HaveCount(1);
        measurements[0].Value.Should().Be(123.5);
    }

    // Slice 9-O2 — coverage for the 4 new instruments added to wire
    // checkout failure, provider calls and provider-call latency.

    [Fact]
    public void CheckoutFailedTotal_ShouldBeRegistered()
    {
        using var collector = new InMemoryMetricsCollector();

        PaymentHubMetrics.CheckoutFailedTotal.Add(1);

        var measurements = collector.For("paymenthub_checkouts_failed_total");
        measurements.Should().HaveCount(1);
        measurements[0].Value.Should().Be(1L);
    }

    [Fact]
    public void ProviderCallTotal_ShouldBeRegistered()
    {
        using var collector = new InMemoryMetricsCollector();

        PaymentHubMetrics.ProviderCallTotal.Add(2,
            new KeyValuePair<string, object?>(PaymentHubMetrics.TagKeys.Provider, "abacatepay"),
            new KeyValuePair<string, object?>(PaymentHubMetrics.TagKeys.Operation, "create_transparent_pix"));

        var measurements = collector.For("paymenthub_provider_call_total");
        measurements.Should().HaveCount(1);
        measurements[0].Value.Should().Be(2L);
        measurements[0].Tags[PaymentHubMetrics.TagKeys.Provider].Should().Be("abacatepay");
        measurements[0].Tags[PaymentHubMetrics.TagKeys.Operation].Should().Be("create_transparent_pix");
    }

    [Fact]
    public void ProviderCallFailedTotal_ShouldRecordFailureCategory()
    {
        using var collector = new InMemoryMetricsCollector();

        PaymentHubMetrics.ProviderCallFailedTotal.Add(1,
            new KeyValuePair<string, object?>(PaymentHubMetrics.TagKeys.Provider, "abacatepay"),
            new KeyValuePair<string, object?>(PaymentHubMetrics.TagKeys.Operation, "check_transparent_pix"),
            new KeyValuePair<string, object?>(PaymentHubMetrics.TagKeys.ErrorCategory, "Timeout"));

        var measurements = collector.For("paymenthub_provider_call_failed_total");
        measurements.Should().HaveCount(1);
        measurements[0].Value.Should().Be(1L);
        measurements[0].Tags[PaymentHubMetrics.TagKeys.ErrorCategory].Should().Be("Timeout");
    }

    [Fact]
    public void ProviderCallDurationMs_ShouldRecordSamples()
    {
        using var collector = new InMemoryMetricsCollector();

        PaymentHubMetrics.ProviderCallDurationMs.Record(456.7,
            new KeyValuePair<string, object?>(PaymentHubMetrics.TagKeys.Provider, "abacatepay"));

        var measurements = collector.For("paymenthub_provider_call_duration_ms");
        measurements.Should().HaveCount(1);
        measurements[0].Value.Should().Be(456.7);
    }

    [Fact]
    public void Tag_ShouldBuildTagList_WithWhitelistedKey()
    {
        var tag = PaymentHubMetrics.Tag(PaymentHubMetrics.TagKeys.Provider, "AbacatePay");

        tag.Count.Should().Be(1);
    }

    [Fact]
    public void Tag_ShouldThrow_WhenKeyIsNotInWhitelist()
    {
        var act = () => PaymentHubMetrics.Tag("api_key", "leaked");

        act.Should().Throw<ArgumentException>()
            .WithMessage("*not in the whitelist*");
    }

    [Fact]
    public void Tag2_ShouldBuildTagList_WithTwoWhitelistedKeys()
    {
        var tags = PaymentHubMetrics.Tag(
            PaymentHubMetrics.TagKeys.Provider, "AbacatePay",
            PaymentHubMetrics.TagKeys.Status, "success");

        tags.Count.Should().Be(2);
    }

    [Fact]
    public void Tag2_ShouldThrow_WhenFirstKeyIsNotInWhitelist()
    {
        var act = () => PaymentHubMetrics.Tag(
            "raw_payload", "leaked",
            PaymentHubMetrics.TagKeys.Provider, "AbacatePay");

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Tag2_ShouldThrow_WhenSecondKeyIsNotInWhitelist()
    {
        var act = () => PaymentHubMetrics.Tag(
            PaymentHubMetrics.TagKeys.Provider, "AbacatePay",
            "signature", "leaked");

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void AllowedTagKeys_ShouldContainExpectedDimensions()
    {
        // The whitelist is the contract surface for cardinality safety.
        // Pin the keys so an accidental rename is caught at compile time.
        PaymentHubMetrics.AllowedTagKeys.Should().Contain(new[]
        {
            PaymentHubMetrics.TagKeys.Provider,
            PaymentHubMetrics.TagKeys.Operation,
            PaymentHubMetrics.TagKeys.Status,
            PaymentHubMetrics.TagKeys.ErrorCategory,
            PaymentHubMetrics.TagKeys.EventType,
            PaymentHubMetrics.TagKeys.Environment,
            PaymentHubMetrics.TagKeys.Worker,
        });
    }

    [Fact]
    public void AllowedTagKeys_ShouldNotContainForbiddenKeys()
    {
        // Anti-leak invariant: apiKey/webhookSecret/rawPayload/signature/
        // body/Authorization MUST NEVER be tag values. They are not
        // whitelisted and never will be.
        PaymentHubMetrics.AllowedTagKeys.Should().NotContain(new[]
        {
            "apiKey", "webhookSecret", "rawPayload", "signature", "body", "Authorization"
        }, because: "sensitive values must never appear as metric tag values");
    }
}
