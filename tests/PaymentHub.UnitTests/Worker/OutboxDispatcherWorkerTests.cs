using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using PaymentHub.Application.Abstractions.Context;
using PaymentHub.Application.Abstractions.Outbox;
using PaymentHub.Domain.Entities;
using PaymentHub.Domain.Enums;
using PaymentHub.Domain.Services;
using PaymentHub.Infrastructure.Postgres.Options;
using PaymentHub.Worker;

namespace PaymentHub.UnitTests.Worker;

/// <summary>
/// Minimal coverage for the Slice 7-A.4 refactor. These tests prove that
/// <see cref="OutboxDispatcherWorker"/> no longer touches <c>PaymentHubDbContext</c> directly
/// and instead routes reads through <see cref="IOutboxRepository"/>, writes through
/// <see cref="IOutboxEventStore"/>, and time through <see cref="IClock"/>.
///
/// Full coverage of dispatch outcomes, batch semantics and cancellation is intentionally left
/// for sub-slice 7-A.8.
/// </summary>
public class OutboxDispatcherWorkerTests
{
    private static readonly DateTime FixedNow = new(2026, 6, 26, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task DispatchOnceAsync_ShouldPickAllPendingEvents_FromRepository()
    {
        // Arrange: 2 pending events, repository returns both.
        var pending = new List<OutboxEvent>
        {
            NewOutboxEvent("payment.approved"),
            NewOutboxEvent("payment.rejected")
        };

        var repository = new Mock<IOutboxRepository>();
        repository.Setup(r => r.GetPendingForDispatchAsync(50, It.IsAny<CancellationToken>()))
            .ReturnsAsync(pending);

        var eventStore = new Mock<IOutboxEventStore>();
        var dispatcher = new Mock<IApplicationWebhookDispatcher>();

        var worker = BuildWorker(services => services
            .AddSingleton(eventStore.Object)
            .AddSingleton(dispatcher.Object)
            .AddSingleton(repository.Object), batchSize: 50);

        // Act
        await worker.DispatchOnceAsync(CancellationToken.None);

        // Assert: repository was queried once with the configured batch size and every
        // returned event reached the dispatcher.
        repository.Verify(r => r.GetPendingForDispatchAsync(50, It.IsAny<CancellationToken>()), Times.Once);
        dispatcher.Verify(d => d.DispatchAsync(It.IsAny<OutboxEvent>(), It.IsAny<CancellationToken>()),
            Times.Exactly(pending.Count));
    }

    [Fact]
    public async Task DispatchOnceAsync_ShouldMarkSent_OnHttp2xx()
    {
        // Arrange: 1 event, dispatcher succeeds.
        var outbox = NewOutboxEvent("payment.approved");

        var repository = new Mock<IOutboxRepository>();
        repository.Setup(r => r.GetPendingForDispatchAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { outbox });

        var eventStore = new Mock<IOutboxEventStore>();
        eventStore.Setup(s => s.SaveAsync(It.IsAny<OutboxEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var dispatcher = new Mock<IApplicationWebhookDispatcher>();

        var worker = BuildWorker(services => services
            .AddSingleton(eventStore.Object)
            .AddSingleton(dispatcher.Object)
            .AddSingleton(repository.Object), batchSize: 10);

        // Act
        await worker.DispatchOnceAsync(CancellationToken.None);

        // Assert: event transitioned to Sent, with LastError cleared and at least 2 saves
        // (one for MarkProcessing + one for MarkSent).
        outbox.Status.Should().Be(OutboxEventStatus.Sent);
        outbox.SentAt.Should().NotBeNull();
        outbox.LastError.Should().BeNull();

        eventStore.Verify(s => s.SaveAsync(It.IsAny<OutboxEvent>(), It.IsAny<CancellationToken>()),
            Times.Exactly(2));
        eventStore.Verify(s => s.SaveAsync(outbox, It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task DispatchOnceAsync_ShouldUseClock_ForRetrySchedule_WhenDispatcherThrows()
    {
        // Arrange: 1 event, dispatcher always throws (transient HTTP failure simulated as Exception).
        var outbox = NewOutboxEvent("payment.approved");

        var repository = new Mock<IOutboxRepository>();
        repository.Setup(r => r.GetPendingForDispatchAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { outbox });

        var eventStore = new Mock<IOutboxEventStore>();
        eventStore.Setup(s => s.SaveAsync(It.IsAny<OutboxEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var dispatcher = new Mock<IApplicationWebhookDispatcher>();
        dispatcher.Setup(d => d.DispatchAsync(It.IsAny<OutboxEvent>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));

        var worker = BuildWorker(services => services
            .AddSingleton(eventStore.Object)
            .AddSingleton(dispatcher.Object)
            .AddSingleton(repository.Object), batchSize: 10);

        // Act
        await worker.DispatchOnceAsync(CancellationToken.None);

        // Assert: retry schedule came from RetryPolicy.NextRetryAt(1, _clock.UtcNow), not from
        // DateTime.UtcNow. The expected value is anchored to FixedNow, proving the worker reads
        // the time from IClock.
        var expectedNextRetry = RetryPolicy.NextRetryAt(1, FixedNow);
        expectedNextRetry.Should().NotBeNull("sanity check: RetryPolicy must produce a backoff for retry #1");
        outbox.RetryCount.Should().Be(1);
        outbox.Status.Should().Be(OutboxEventStatus.Pending);
        outbox.NextRetryAt.Should().Be(expectedNextRetry!.Value);
    }

    [Fact]
    public void OutboxDispatcherWorker_Type_ShouldNotReferencePaymentHubDbContext()
    {
        // Structural sanity check: confirms the Slice 7-A.4 refactor removed the direct
        // PaymentHubDbContext dependency from the worker. Compile-time guarantees are limited
        // because the symbol may appear in any nested type; checking the file source keeps the
        // assertion local and stable across refactors.
        var sourcePath = Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            "src", "PaymentHub.Worker", "OutboxDispatcherWorker.cs");
        var fullPath = Path.GetFullPath(sourcePath);
        File.Exists(fullPath).Should().BeTrue($"worker source must exist at {fullPath}");

        var source = File.ReadAllText(fullPath);
        source.Should().NotContain("PaymentHubDbContext",
            "Slice 7-A.4 forbids the worker from touching PaymentHubDbContext directly");
        source.Should().NotContain("PaymentHub.Infrastructure.Postgres;",
            "Slice 7-A.4 forbids the worker from importing the Postgres namespace");
    }

    // =================================================================================================
    // Slice 7-A.7 — safe LastError transitions in the worker.
    // =================================================================================================

    [Theory]
    [InlineData(500)]
    [InlineData(429)]
    [InlineData(404)]
    [InlineData(503)]
    public async Task DispatchOnceAsync_ShouldPersistSafeLastError_WhenDispatcherThrowsHttpFailure(int statusCode)
    {
        // Arrange: dispatcher throws WebhookDispatcherException(HttpFailure, statusCode).
        var outbox = NewOutboxEvent("payment.approved");
        // Use a payload that looks like it could leak if a regression reintroduces ex.Message:
        // the assertion below ensures the payload never reaches LastError.
        outbox.GetType(); // touch to ensure entity is in scope for the read access below

        var repository = new Mock<IOutboxRepository>();
        repository.Setup(r => r.GetPendingForDispatchAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { outbox });

        var eventStore = new Mock<IOutboxEventStore>();
        eventStore.Setup(s => s.SaveAsync(It.IsAny<OutboxEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var dispatcher = new Mock<IApplicationWebhookDispatcher>();
        dispatcher.Setup(d => d.DispatchAsync(It.IsAny<OutboxEvent>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new WebhookDispatcherException(
                WebhookDispatcherCategory.HttpFailure, statusCode,
                $"Application webhook responded {statusCode} (consumer returned non-success)."));

        var worker = BuildWorker(services => services
            .AddSingleton(eventStore.Object)
            .AddSingleton(dispatcher.Object)
            .AddSingleton(repository.Object), batchSize: 10);

        // Act
        await worker.DispatchOnceAsync(CancellationToken.None);

        // Assert: LastError contains only the category + status code (NO ex.Message content).
        outbox.Status.Should().Be(OutboxEventStatus.Pending);
        outbox.LastError.Should().Be($"HttpFailure: status={statusCode}");
        outbox.LastError.Should().NotContain("consumer returned",
            "ex.Message must never reach LastError");
        outbox.LastError.Should().NotContain(outbox.PayloadJson,
            "payload must never leak into LastError");
    }

    [Fact]
    public async Task DispatchOnceAsync_ShouldPersistNetworkErrorCategory_WhenHttpRequestExceptionThrown()
    {
        // Arrange: dispatcher throws WebhookDispatcherException(NetworkError) with a noisy
        // message simulating a HttpRequestException; the worker must NOT propagate that
        // message into LastError.
        var outbox = NewOutboxEvent("payment.approved");

        var repository = new Mock<IOutboxRepository>();
        repository.Setup(r => r.GetPendingForDispatchAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { outbox });

        var eventStore = new Mock<IOutboxEventStore>();
        eventStore.Setup(s => s.SaveAsync(It.IsAny<OutboxEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var dispatcher = new Mock<IApplicationWebhookDispatcher>();
        dispatcher.Setup(d => d.DispatchAsync(It.IsAny<OutboxEvent>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new WebhookDispatcherException(
                WebhookDispatcherCategory.NetworkError,
                "Connection refused (https://internal-router.acme.io:443/api/v1/hook?token=REDACTED)"));

        var worker = BuildWorker(services => services
            .AddSingleton(eventStore.Object)
            .AddSingleton(dispatcher.Object)
            .AddSingleton(repository.Object), batchSize: 10);

        // Act
        await worker.DispatchOnceAsync(CancellationToken.None);

        // Assert: LastError contains only the category name; no URL, no token, no ex.Message content.
        outbox.LastError.Should().Be("NetworkError");
        outbox.LastError.Should().NotContain("internal-router.acme.io",
            "URLs must never leak into LastError");
        outbox.LastError.Should().NotContain("REDACTED",
            "token-like fragments must never leak into LastError");
        outbox.LastError.Should().NotContain("Connection refused",
            "ex.Message must never leak into LastError");
    }

    [Fact]
    public async Task DispatchOnceAsync_ShouldPersistUnprotectFailureCategory_WhenSecretCorrupted()
    {
        // Arrange: unprotect failure should be categorised and the exception's message
        // (which may carry the raw blob or hex of the cipher) must NOT reach LastError.
        var outbox = NewOutboxEvent("payment.approved");

        var repository = new Mock<IOutboxRepository>();
        repository.Setup(r => r.GetPendingForDispatchAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { outbox });

        var eventStore = new Mock<IOutboxEventStore>();
        eventStore.Setup(s => s.SaveAsync(It.IsAny<OutboxEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var dispatcher = new Mock<IApplicationWebhookDispatcher>();
        dispatcher.Setup(d => d.DispatchAsync(It.IsAny<OutboxEvent>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new WebhookDispatcherException(
                WebhookDispatcherCategory.UnprotectFailure,
                "CryptographicException: padding invalid (raw blob: 0xDEADBEEFCAFEBABE...)"));

        var worker = BuildWorker(services => services
            .AddSingleton(eventStore.Object)
            .AddSingleton(dispatcher.Object)
            .AddSingleton(repository.Object), batchSize: 10);

        // Act
        await worker.DispatchOnceAsync(CancellationToken.None);

        // Assert
        outbox.LastError.Should().Be("UnprotectFailure");
        outbox.LastError.Should().NotContain("DEADBEEF",
            "blob hex must never leak into LastError");
        outbox.LastError.Should().NotContain("padding invalid",
            "ex.Message must never leak into LastError");
    }

    [Fact]
    public async Task DispatchOnceAsync_ShouldNotLeakWebhookSecretOrPayload_WhenGenericExceptionThrown()
    {
        // Arrange: a non-WebhookDispatcherException surfaces to the worker's catch-all.
        // The category must be UnexpectedDispatcherError and LastError must be sanitised.
        var secret = "supersecret-webhook-key-should-never-leak";
        var outbox = new OutboxEvent(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            "payment.approved",
            "{\"secret\":\"" + secret + "\",\"eventId\":\"00000000-0000-0000-0000-000000000001\"}");

        var repository = new Mock<IOutboxRepository>();
        repository.Setup(r => r.GetPendingForDispatchAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { outbox });

        var eventStore = new Mock<IOutboxEventStore>();
        eventStore.Setup(s => s.SaveAsync(It.IsAny<OutboxEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var dispatcher = new Mock<IApplicationWebhookDispatcher>();
        dispatcher.Setup(d => d.DispatchAsync(It.IsAny<OutboxEvent>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom: " + secret));

        var worker = BuildWorker(services => services
            .AddSingleton(eventStore.Object)
            .AddSingleton(dispatcher.Object)
            .AddSingleton(repository.Object), batchSize: 10);

        // Act
        await worker.DispatchOnceAsync(CancellationToken.None);

        // Assert
        outbox.LastError.Should().Be("UnexpectedDispatcherError");
        outbox.LastError.Should().NotContain(secret,
            "webhook secret-like strings must never leak into LastError");
        outbox.LastError.Should().NotContain(outbox.PayloadJson,
            "payload must never leak into LastError");
        outbox.LastError.Should().NotContain("boom",
            "ex.Message must never leak into LastError");
    }

    [Fact]
    public async Task DispatchOnceAsync_ShouldMarkFailedWithCategory_WhenRetriesExhausted()
    {
        // Arrange: dispatch always throws WebhookDispatcherException(NetworkError) and the
        // event has RetryCount already at MaxAttempts-1 so the next failure becomes permanent.
        var outbox = NewOutboxEvent("payment.approved");
        for (int i = 0; i < RetryPolicy.MaxAttempts - 1; i++)
        {
            outbox.MarkRetryWithCategory(WebhookDispatcherCategory.NetworkError,
                RetryPolicy.NextRetryAt(i + 1, FixedNow)!.Value);
        }
        outbox.RetryCount.Should().Be(RetryPolicy.MaxAttempts - 1);

        var repository = new Mock<IOutboxRepository>();
        repository.Setup(r => r.GetPendingForDispatchAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { outbox });

        var eventStore = new Mock<IOutboxEventStore>();
        eventStore.Setup(s => s.SaveAsync(It.IsAny<OutboxEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var dispatcher = new Mock<IApplicationWebhookDispatcher>();
        dispatcher.Setup(d => d.DispatchAsync(It.IsAny<OutboxEvent>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new WebhookDispatcherException(
                WebhookDispatcherCategory.NetworkError, "Connection refused"));

        var worker = BuildWorker(services => services
            .AddSingleton(eventStore.Object)
            .AddSingleton(dispatcher.Object)
            .AddSingleton(repository.Object), batchSize: 10);

        // Act
        await worker.DispatchOnceAsync(CancellationToken.None);

        // Assert: permanent failure with safe LastError.
        outbox.Status.Should().Be(OutboxEventStatus.Failed);
        outbox.LastError.Should().Be("NetworkError");
        outbox.NextRetryAt.Should().BeNull();
        outbox.RetryCount.Should().Be(RetryPolicy.MaxAttempts);
    }

    // --- helpers ---

    private static OutboxEvent NewOutboxEvent(string eventType)
        => new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), eventType, "{}");

    /// <summary>
    /// Builds the worker with a real <see cref="IServiceScopeFactory"/> resolved from an
    /// in-memory service collection. This avoids hand-mocking the scope/provider chain and
    /// keeps the tests close to the production DI shape.
    /// </summary>
    private static OutboxDispatcherWorker BuildWorker(
        Action<IServiceCollection> registerScoped,
        int batchSize)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IClock>(new FixedClock(FixedNow));
        services.AddSingleton<ILogger<OutboxDispatcherWorker>>(NullLogger<OutboxDispatcherWorker>.Instance);
        services.AddSingleton<IOptions<PaymentHubOptions>>(Options.Create(new PaymentHubOptions
        {
            OutboxWorkerBatchSize = batchSize,
            OutboxWorkerIntervalSeconds = 60
        }));

        // Repository, EventStore and Dispatcher are Scoped in production (they depend on
        // PaymentHubDbContext via the EF repositories). We register them as scoped here so the
        // worker resolves them through a real scope.
        services.AddScoped<IOutboxRepository>(_ => null!);
        services.AddScoped<IOutboxEventStore>(_ => null!);
        services.AddScoped<IApplicationWebhookDispatcher>(_ => null!);

        registerScoped(services);

        var provider = services.BuildServiceProvider();
        var scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();

        return new OutboxDispatcherWorker(
            scopeFactory,
            provider.GetRequiredService<IClock>(),
            provider.GetRequiredService<ILogger<OutboxDispatcherWorker>>(),
            provider.GetRequiredService<IOptions<PaymentHubOptions>>());
    }

    private sealed class FixedClock : IClock
    {
        public FixedClock(DateTime utcNow) => UtcNow = utcNow;
        public DateTime UtcNow { get; }
    }
}