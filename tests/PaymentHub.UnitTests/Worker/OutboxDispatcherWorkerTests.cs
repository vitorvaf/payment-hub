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
        repository.Setup(r => r.ClaimPendingForDispatchAsync(50, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
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
        repository.Verify(r => r.ClaimPendingForDispatchAsync(50, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Once);
        dispatcher.Verify(d => d.DispatchAsync(It.IsAny<OutboxEvent>(), It.IsAny<CancellationToken>()),
            Times.Exactly(pending.Count));
    }

    [Fact]
    public async Task DispatchOnceAsync_ShouldMarkSent_OnHttp2xx()
    {
        // Arrange: 1 event, dispatcher succeeds.
        var outbox = NewOutboxEvent("payment.approved");

        var repository = new Mock<IOutboxRepository>();
        repository.Setup(r => r.ClaimPendingForDispatchAsync(It.IsAny<int>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
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

        // Assert: event transitioned to Sent, with LastError cleared and exactly 1 save
        // (Slice 7-M1: MarkProcessing is now performed by ClaimPendingForDispatchAsync
        // inside the same transaction; the worker only persists MarkSent).
        outbox.Status.Should().Be(OutboxEventStatus.Sent);
        outbox.SentAt.Should().NotBeNull();
        outbox.LastError.Should().BeNull();

        eventStore.Verify(s => s.SaveAsync(It.IsAny<OutboxEvent>(), It.IsAny<CancellationToken>()),
            Times.Once);
        eventStore.Verify(s => s.SaveAsync(outbox, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DispatchOnceAsync_ShouldUseClock_ForRetrySchedule_WhenDispatcherThrows()
    {
        // Arrange: 1 event, dispatcher always throws (transient HTTP failure simulated as Exception).
        var outbox = NewOutboxEvent("payment.approved");

        var repository = new Mock<IOutboxRepository>();
        repository.Setup(r => r.ClaimPendingForDispatchAsync(It.IsAny<int>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
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
        repository.Setup(r => r.ClaimPendingForDispatchAsync(It.IsAny<int>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
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
        repository.Setup(r => r.ClaimPendingForDispatchAsync(It.IsAny<int>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
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
        repository.Setup(r => r.ClaimPendingForDispatchAsync(It.IsAny<int>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
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
        // Slice 7-M1: ClaimPendingForDispatchAsync returns the row already in `Processing`;
        // stamp it so the worker's safety check accepts the entity.
        outbox.MarkProcessing(FixedNow);

        var repository = new Mock<IOutboxRepository>();
        repository.Setup(r => r.ClaimPendingForDispatchAsync(It.IsAny<int>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
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
        // Slice 7-M1: ClaimPendingForDispatchAsync returns the row already in `Processing`.
        // Re-stamp after the retry loop so the worker's safety check accepts the entity.
        outbox.MarkProcessing(FixedNow);

        var repository = new Mock<IOutboxRepository>();
        repository.Setup(r => r.ClaimPendingForDispatchAsync(It.IsAny<int>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
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

    // =================================================================================================
    // Slice 7-A.8 — strong coverage: batch semantics, cancellation, reprocess stability and
    // LastError safety across the full worker surface.
    // =================================================================================================

    [Fact]
    public async Task DispatchOnceAsync_ShouldProcessMixedBatch_WithSuccessAndDifferentFailureCategories()
    {
        // Arrange: 3 events with 3 different outcomes. Each must end in the correct status,
        // each must keep its original Id (reprocess stability — C.1), and the dispatcher must be
        // called exactly once per event regardless of prior failures (batch isolation — B.6.4).
        var ok = NewOutboxEvent("payment.approved");
        var httpFail = NewOutboxEvent("payment.approved");
        var network = NewOutboxEvent("payment.approved");
        var originalIds = new[] { ok.Id, httpFail.Id, network.Id };

        var repository = new Mock<IOutboxRepository>();
        repository.Setup(r => r.ClaimPendingForDispatchAsync(It.IsAny<int>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { ok, httpFail, network });

        var eventStore = new Mock<IOutboxEventStore>();
        eventStore.Setup(s => s.SaveAsync(It.IsAny<OutboxEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var dispatcher = new Mock<IApplicationWebhookDispatcher>();
        var callIndex = 0;
        dispatcher.Setup(d => d.DispatchAsync(It.IsAny<OutboxEvent>(), It.IsAny<CancellationToken>()))
            .Returns<OutboxEvent, CancellationToken>(async (e, ct) =>
            {
                callIndex++;
                if (e.Id == httpFail.Id)
                    throw new WebhookDispatcherException(
                        WebhookDispatcherCategory.HttpFailure, 500,
                        "Application webhook responded 500 (consumer returned non-success).");
                if (e.Id == network.Id)
                    throw new WebhookDispatcherException(
                        WebhookDispatcherCategory.NetworkError,
                        "Network error while dispatching webhook.");
                await Task.CompletedTask; // success
            });

        var worker = BuildWorker(services => services
            .AddSingleton(eventStore.Object)
            .AddSingleton(dispatcher.Object)
            .AddSingleton(repository.Object), batchSize: 10);

        // Act
        await worker.DispatchOnceAsync(CancellationToken.None);

        // Assert: every event reached the dispatcher and ended up in its correct outcome.
        callIndex.Should().Be(3, "all 3 events must reach the dispatcher");

        ok.Status.Should().Be(OutboxEventStatus.Sent);
        ok.SentAt.Should().NotBeNull();
        ok.LastError.Should().BeNull();

        httpFail.Status.Should().Be(OutboxEventStatus.Pending);
        httpFail.RetryCount.Should().Be(1);
        httpFail.LastError.Should().Be("HttpFailure: status=500");
        httpFail.NextRetryAt.Should().NotBeNull();

        network.Status.Should().Be(OutboxEventStatus.Pending);
        network.RetryCount.Should().Be(1);
        network.LastError.Should().Be("NetworkError");
        network.NextRetryAt.Should().NotBeNull();

        // Repprocess stability (C.1): the OutboxEvent.Id never changes across retry.
        new[] { ok.Id, httpFail.Id, network.Id }.Should().Equal(originalIds);

        // eventStore.SaveAsync called once per event (Slice 7-M1: MarkProcessing is now
        // performed by ClaimPendingForDispatchAsync inside the same transaction; the
        // worker only persists the final mark).
        eventStore.Verify(s => s.SaveAsync(It.IsAny<OutboxEvent>(), It.IsAny<CancellationToken>()),
            Times.Exactly(3));
    }

    [Fact]
    public async Task DispatchOnceAsync_ShouldPropagateCancellation_WhenDispatcherThrowsOperationCanceledException()
    {
        // Slice 7-A.8 fix: without the catch (OperationCanceledException) re-throw in the worker,
        // an OCE from the dispatcher would fall through to the generic catch and be classified as
        // UnexpectedDispatcherError, silently swallowing the cancel signal AND polluting LastError.
        var outbox = NewOutboxEvent("payment.approved");

        var repository = new Mock<IOutboxRepository>();
        repository.Setup(r => r.ClaimPendingForDispatchAsync(It.IsAny<int>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { outbox });

        var eventStore = new Mock<IOutboxEventStore>();
        eventStore.Setup(s => s.SaveAsync(It.IsAny<OutboxEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var dispatcher = new Mock<IApplicationWebhookDispatcher>();
        dispatcher.Setup(d => d.DispatchAsync(It.IsAny<OutboxEvent>(), It.IsAny<CancellationToken>()))
            .Returns<OutboxEvent, CancellationToken>(async (_, ct) =>
            {
                await Task.Yield();
                ct.ThrowIfCancellationRequested();
            });

        var worker = BuildWorker(services => services
            .AddSingleton(eventStore.Object)
            .AddSingleton(dispatcher.Object)
            .AddSingleton(repository.Object), batchSize: 10);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act + Assert: the OCE propagates out instead of being swallowed.
        var act = async () => await worker.DispatchOnceAsync(cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();

        // The event must NOT have been mis-classified as UnexpectedDispatcherError.
        outbox.LastError.Should().BeNull("OCE must not be persisted as LastError");
        outbox.Status.Should().NotBe(OutboxEventStatus.Failed);
    }

    [Fact]
    public async Task DispatchOnceAsync_ShouldPreserveEventId_OnHttpFailureRetry()
    {
        // C.1 (qa-reviewer): eventId must be stable across retries so the worker can correlate
        // the same logical delivery across attempts (OutboxEvent.Id is the only stable handle).
        var outbox = NewOutboxEvent("payment.approved");
        var originalId = outbox.Id;

        var repository = new Mock<IOutboxRepository>();
        repository.Setup(r => r.ClaimPendingForDispatchAsync(It.IsAny<int>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            // Slice 7-M1: the test exercises two iterations; the entity transitions back to
            // `Pending` (via MarkRetryWithStatus) between iterations, so we re-stamp
            // `Processing` inside the mock to mirror the production claim contract.
            .ReturnsAsync(() => { outbox.MarkProcessing(FixedNow); return new[] { outbox }; });

        var eventStore = new Mock<IOutboxEventStore>();
        eventStore.Setup(s => s.SaveAsync(It.IsAny<OutboxEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var dispatcher = new Mock<IApplicationWebhookDispatcher>();
        dispatcher.Setup(d => d.DispatchAsync(It.IsAny<OutboxEvent>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new WebhookDispatcherException(
                WebhookDispatcherCategory.HttpFailure, 503,
                "Application webhook responded 503 (consumer returned non-success)."));

        var worker = BuildWorker(services => services
            .AddSingleton(eventStore.Object)
            .AddSingleton(dispatcher.Object)
            .AddSingleton(repository.Object), batchSize: 10);

        // Act: run two iterations (each call represents an attempt).
        await worker.DispatchOnceAsync(CancellationToken.None);
        var afterFirstRetry = outbox.Id;
        await worker.DispatchOnceAsync(CancellationToken.None);
        var afterSecondRetry = outbox.Id;

        // Assert
        outbox.Id.Should().Be(originalId, "eventId must never change across retries");
        afterFirstRetry.Should().Be(originalId);
        afterSecondRetry.Should().Be(originalId);
        outbox.RetryCount.Should().Be(2);
        outbox.LastError.Should().Be("HttpFailure: status=503");
    }

    [Fact]
    public async Task DispatchOnceAsync_ShouldNotIncludePayloadOrSecret_InLastError_OnHttpFailure()
    {
        // Belt-and-braces check on the worker side: even if the dispatcher's exception message
        // leaked (regression), the entity would refuse to persist it. Verify the worker's path
        // never touches ex.Message even when the payload + secret are in scope.
        const string secret = "leaky-webhook-secret";
        var outbox = new OutboxEvent(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            "payment.approved",
            "{\"secret\":\"" + secret + "\",\"eventId\":\"00000000-0000-0000-0000-000000000001\"}");
        // Slice 7-M1: ClaimPendingForDispatchAsync returns the row already in `Processing`;
        // stamp it so the worker's safety check accepts the entity.
        outbox.MarkProcessing(FixedNow);

        var repository = new Mock<IOutboxRepository>();
        repository.Setup(r => r.ClaimPendingForDispatchAsync(It.IsAny<int>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { outbox });

        var eventStore = new Mock<IOutboxEventStore>();
        eventStore.Setup(s => s.SaveAsync(It.IsAny<OutboxEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var dispatcher = new Mock<IApplicationWebhookDispatcher>();
        // Provide an exception message that simulates a hypothetical leak regression in the
        // dispatcher — the worker must STILL not copy it into LastError.
        dispatcher.Setup(d => d.DispatchAsync(It.IsAny<OutboxEvent>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new WebhookDispatcherException(
                WebhookDispatcherCategory.HttpFailure, 500,
                $"leaked body: {secret} and payload snippet: {outbox.PayloadJson[..40]}..."));

        var worker = BuildWorker(services => services
            .AddSingleton(eventStore.Object)
            .AddSingleton(dispatcher.Object)
            .AddSingleton(repository.Object), batchSize: 10);

        // Act
        await worker.DispatchOnceAsync(CancellationToken.None);

        // Assert
        outbox.LastError.Should().Be("HttpFailure: status=500");
        outbox.LastError.Should().NotContain(secret);
        outbox.LastError.Should().NotContain("leaked body");
        outbox.LastError.Should().NotContain(outbox.PayloadJson);
    }

    [Fact]
    public async Task DispatchOnceAsync_ShouldMarkFailedWithStatus_WhenRetriesExhausted_OnHttpFailure()
    {
        // Arrange: event already at MaxAttempts-1, then a final HTTP failure → terminal Failed
        // with safe LastError including the status code.
        var outbox = NewOutboxEvent("payment.approved");
        for (int i = 0; i < RetryPolicy.MaxAttempts - 1; i++)
        {
            outbox.MarkRetryWithStatus(WebhookDispatcherCategory.HttpFailure, 429,
                RetryPolicy.NextRetryAt(i + 1, FixedNow)!.Value);
        }
        outbox.RetryCount.Should().Be(RetryPolicy.MaxAttempts - 1);
        // Slice 7-M1: ClaimPendingForDispatchAsync returns the row already in `Processing`.
        // Re-stamp after the retry loop so the worker's safety check accepts the entity.
        outbox.MarkProcessing(FixedNow);

        var repository = new Mock<IOutboxRepository>();
        repository.Setup(r => r.ClaimPendingForDispatchAsync(It.IsAny<int>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { outbox });

        var eventStore = new Mock<IOutboxEventStore>();
        eventStore.Setup(s => s.SaveAsync(It.IsAny<OutboxEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var dispatcher = new Mock<IApplicationWebhookDispatcher>();
        dispatcher.Setup(d => d.DispatchAsync(It.IsAny<OutboxEvent>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new WebhookDispatcherException(
                WebhookDispatcherCategory.HttpFailure, 429,
                "Application webhook responded 429 (consumer returned non-success)."));

        var worker = BuildWorker(services => services
            .AddSingleton(eventStore.Object)
            .AddSingleton(dispatcher.Object)
            .AddSingleton(repository.Object), batchSize: 10);

        // Act
        await worker.DispatchOnceAsync(CancellationToken.None);

        // Assert: terminal Failed with safe LastError.
        outbox.Status.Should().Be(OutboxEventStatus.Failed);
        outbox.LastError.Should().Be("HttpFailure: status=429");
        outbox.NextRetryAt.Should().BeNull();
        outbox.RetryCount.Should().Be(RetryPolicy.MaxAttempts);
    }

    // --- helpers ---

    private static OutboxEvent NewOutboxEvent(string eventType)
    {
        // Slice 7-M1: tests now drive ClaimPendingForDispatchAsync which returns entities
        // already in `Processing` with a stamped `ProcessingStartedAt`. MarkProcessing here
        // mirrors that contract so the worker's safety check is satisfied.
        var ev = new OutboxEvent(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), eventType, "{}");
        ev.MarkProcessing(FixedNow);
        return ev;
    }

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