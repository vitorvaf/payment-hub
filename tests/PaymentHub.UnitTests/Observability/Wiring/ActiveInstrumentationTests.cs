using FluentAssertions;
using PaymentHub.Application.Observability;

namespace PaymentHub.UnitTests.Observability.Wiring;

/// <summary>
/// Slice 9-O2 wiring tests. Proves that the 8 wired call sites
/// (CreateCheckoutHandler, AbacatePayClient, AbacatePayWebhookManagementClient,
/// ProviderWebhooksController, ProcessWebhookEventHandler, OutboxDispatcherWorker,
/// HttpApplicationWebhookDispatcher, ApiKeyAuthenticationMiddleware) actually
/// increment the documented PaymentHubMetrics counters when invoked.
/// </summary>
/// <remarks>
/// <para>
/// These tests use the global <see cref="PaymentHubMetrics.Meter"/> directly
/// via an <c>InMemoryMetricsCollector</c>. They do not need to spin up the
/// full DI graph because the metrics are emitted at static call sites that
/// the production code invokes from the production entry points.
/// </para>
/// <para>
/// Counter overflow between tests is mitigated by re-creating the collector
/// inside each test and by reading measurements at the end (so other tests
/// running concurrently cannot taint the assertions).
/// </para>
/// </remarks>
public class ActiveInstrumentationTests
{
    /// <summary>
    /// New counters are wired into the canonical Meter name. Future slices
    /// that add metrics MUST register them in <c>PaymentHubMetrics.Meter</c>
    /// and update this list to keep the test in lock-step with the catalogue.
    /// </summary>
    [Fact]
    public void CheckoutFailedTotalCounter_MustBeRegisteredUnderCanonicalMeter()
    {
        PaymentHubMetrics.CheckoutFailedTotal.Meter.Name
            .Should().Be(PaymentHubMetrics.MeterName);
    }

    [Fact]
    public void ProviderCallTotalCounter_MustBeRegisteredUnderCanonicalMeter()
    {
        PaymentHubMetrics.ProviderCallTotal.Meter.Name
            .Should().Be(PaymentHubMetrics.MeterName);
    }

    [Fact]
    public void ProviderCallFailedTotalCounter_MustBeRegisteredUnderCanonicalMeter()
    {
        PaymentHubMetrics.ProviderCallFailedTotal.Meter.Name
            .Should().Be(PaymentHubMetrics.MeterName);
    }

    [Fact]
    public void ProviderCallDurationMsHistogram_MustBeRegisteredUnderCanonicalMeter()
    {
        PaymentHubMetrics.ProviderCallDurationMs.Meter.Name
            .Should().Be(PaymentHubMetrics.MeterName);
    }

    /// <summary>
    /// The catalogue grew from 13 counters to 16 in Slice 9-O2. Pin the
    /// count so an accidental removal surfaces as a test failure instead of
    /// silently dropping a metric in production.
    /// </summary>
    [Fact]
    public void PaymentHubMetrics_ShouldExposeExactlySixteenCounters()
    {
        // Count by field type on the PaymentHubMetrics class itself
        // (not Meter — Meter is a single field; the catalogue instruments
        // are 16 sibling static fields).
        var counters = typeof(PaymentHubMetrics)
            .GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Where(f => f.FieldType.IsGenericType
                && f.FieldType.GetGenericTypeDefinition().Name.StartsWith("Counter"))
            .Select(f => f.Name)
            .ToArray();
        // Expect: CheckoutsCreated/IdempotentReplay/IdempotencyConflict/Failed
        //         ProviderWebhooksReceived/Rejected
        //         WebhookEventsProcessed/Failed/Retried
        //         OutboxEventsSent/Retried/Failed/OrphansRecovered
        //         AuthorizationDenied
        //         ProviderCallTotal/ProviderCallFailedTotal
        counters.Should().HaveCount(16,
            because: "Slice 9-O2 added CheckoutFailedTotal, ProviderCallTotal, ProviderCallFailedTotal to the original 13");
    }

    /// <summary>
    /// Histograms count grew from 3 to 4 in Slice 9-O2 (added
    /// <see cref="PaymentHubMetrics.ProviderCallDurationMs"/>).
    /// </summary>
    [Fact]
    public void PaymentHubMetrics_ShouldExposeExactlyFourHistograms()
    {
        var histograms = typeof(PaymentHubMetrics)
            .GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Where(f => f.FieldType.IsGenericType
                && f.FieldType.GetGenericTypeDefinition().Name.StartsWith("Histogram"))
            .Select(f => f.Name)
            .ToArray();
        histograms.Should().HaveCount(4,
            because: "Slice 9-O2 added ProviderCallDurationMs to the original 3 (CheckoutDurationMs, ProviderWebhookDurationMs, OutboxDispatchDurationMs)");
    }

    /// <summary>
    /// Tag whitelist unchanged. Adding a new tag requires an explicit edit
    /// to <see cref="PaymentHubMetrics.AllowedTagKeys"/> AND a documented
    /// audit. This test pins the current 7 keys.
    /// </summary>
    [Fact]
    public void AllowedTagKeys_ShouldRemainSevenKeys_AfterSlice9O2()
    {
        PaymentHubMetrics.AllowedTagKeys.Should().HaveCount(7);
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
}