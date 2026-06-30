using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PaymentHub.Application.Abstractions.Context;
using PaymentHub.Application.Abstractions.Outbox;
using PaymentHub.Application.Abstractions.Persistence;
using PaymentHub.Application.Abstractions.Security;
using PaymentHub.Domain.Enums;
using PaymentHub.Infrastructure.Postgres.Options;
using PaymentHub.IntegrationTests.Infrastructure;
using PaymentHub.IntegrationTests.Support;
using PaymentHub.Infrastructure.Postgres;
using PaymentHub.Worker;

namespace PaymentHub.IntegrationTests.EndToEnd;

/// <summary>
/// End-to-end integration tests for the Slice 7-IT commitment: drive the real
/// <see cref="OutboxDispatcherWorker.DispatchOnceAsync"/> against the real
/// Postgres + the real <see cref="PaymentHub.Infrastructure.Postgres.Webhooks.HttpApplicationWebhookDispatcher"/>,
/// with only the outbound HTTP transport replaced by
/// <see cref="ApplicationWebhookCaptureHandler"/>. No outbound HTTP leaves the
/// test process; no hosted services run inside <see cref="PaymentHubApiFactory"/>;
/// no fake provider is involved.
/// </summary>
/// <remarks>
/// <para>
/// These tests assert the production contract documented in
/// <c>docs/specs/007-inbox-outbox-workers.md</c> + <c>docs/specs/011-security-and-compliance.md</c>:
/// </para>
/// <list type="bullet">
/// <item>The dispatcher signs every delivery with
/// <c>X-PaymentHub-Signature = hmac_sha256_hex_lowercase(secret, "{timestamp}.{rawBody}")</c>.</item>
/// <item>2xx responses transition <c>OutboxEvent</c> to <c>Sent</c> with
/// <c>LastError = null</c>.</item>
/// <item>Non-2xx HTTP responses transition <c>OutboxEvent</c> to <c>Pending</c>
/// with an incremented <c>RetryCount</c>, a future <c>NextRetryAt</c>, and a
/// <c>LastError</c> shaped <c>"HttpFailure: status={code}"</c>. The response
/// body, the URL and the secret never reach <c>LastError</c>.</item>
/// <item><c>IWebhookSecretProtector.Unprotect</c> failures raise
/// <c>WebhookDispatcherException</c> with category
/// <c>UnprotectFailure</c> BEFORE any HTTP request is sent.</item>
/// </list>
/// </remarks>
[Collection(PostgresCollection.Name)]
[Trait("Category", "Integration")]
public class OutboxDispatcherE2ETests
{
    /// <summary>Public DNS-formatted webhook target the production validator accepts.</summary>
    private const string TestWebhookUrl = "https://webhook.fake.test/hook";

    /// <summary>Plaintext webhook secret the test seeds via the protector.</summary>
    private const string TestWebhookSecret = "test-internal-webhook-secret-do-not-log";

    private readonly PostgresFixture _postgres;

    public OutboxDispatcherE2ETests(PostgresFixture postgres)
    {
        _postgres = postgres;
    }

    // -----------------------------------------------------------------
    // P1.1 — Happy path: dispatcher sends Pending event and marks Sent
    // -----------------------------------------------------------------

    [Fact]
    public async Task OutboxDispatcher_ShouldSendPendingEvent_ToApplicationClientWebhook_AndMarkSent()
    {
        var factory = await CreateFreshFactoryAsync();
        try
        {
            var credentials = await SeedApplicationWithWebhookAsync(factory);

            var outboxEventId = Guid.NewGuid();
            await EnqueueOutboxAsync(
                factory,
                credentials,
                outboxEventId,
                eventType: "payment.checkout.created",
                payload: new { paymentId = Guid.NewGuid(), status = "Pending" });

            // Act — drive one dispatcher iteration manually.
            await RunDispatcherOnceAsync(factory);

            // Assert — outbound webhook was captured exactly once with the expected shape.
            factory.WebhookHandler.CallCount.Should().Be(1);
            var captured = factory.WebhookHandler.Last;
            captured.Should().NotBeNull();
            captured!.Method.Should().Be("POST");
            captured.Url.Should().Be(TestWebhookUrl);
            captured.Body.Should().NotBeNullOrEmpty();

            using var sent = JsonDocument.Parse(captured.Body);
            sent.RootElement.GetProperty("paymentId").GetGuid().Should().NotBeEmpty();

            // Assert — the event transitioned to Sent with no error and a populated SentAt.
            await using var db = factory.CreateDbContext();
            var reloaded = await db.OutboxEvents.AsNoTracking()
                .SingleAsync(o => o.Id == outboxEventId);

            reloaded.Status.Should().Be(OutboxEventStatus.Sent);
            reloaded.SentAt.Should().NotBeNull();
            reloaded.LastError.Should().BeNull();
            reloaded.NextRetryAt.Should().BeNull();
            reloaded.RetryCount.Should().Be(0,
                because: "happy path: no retry should have incremented the counter");
        }
        finally
        {
            await factory.DisposeAsync();
        }
    }

    // -----------------------------------------------------------------
    // P1.2 — Internal webhook carries valid X-PaymentHub-* signature
    // -----------------------------------------------------------------

    [Fact]
    public async Task OutboxDispatcher_ShouldSignInternalWebhook_WithApplicationClientSecret()
    {
        var factory = await CreateFreshFactoryAsync();
        try
        {
            var credentials = await SeedApplicationWithWebhookAsync(factory);

            var outboxEventId = Guid.NewGuid();
            await EnqueueOutboxAsync(
                factory,
                credentials,
                outboxEventId,
                eventType: "payment.approved",
                payload: new { paymentId = Guid.NewGuid(), status = "Approved" });

            await RunDispatcherOnceAsync(factory);

            // Assert — exactly one delivery with all four expected headers.
            var captured = factory.WebhookHandler.Last;
            captured.Should().NotBeNull();
            captured!.SignatureHeader.Should().NotBeNullOrEmpty();
            captured.TimestampHeader.Should().NotBeNullOrEmpty();
            captured.EventIdHeader.Should().Be(outboxEventId.ToString());
            captured.EventTypeHeader.Should().Be("payment.approved");

            // Assert — the HMAC matches sha256_hex_lowercase(secret, "{ts}.{body}").
            InternalWebhookHmac.Matches(
                captured.Body, TestWebhookSecret, captured.TimestampHeader!,
                captured.SignatureHeader).Should().BeTrue(
                because: "the dispatcher must sign {ts}.{body} with the application webhook secret");

            // Assert — tampering with either side of the contract invalidates the signature.
            InternalWebhookHmac.Matches(
                captured.Body + " ", TestWebhookSecret, captured.TimestampHeader!,
                captured.SignatureHeader).Should().BeFalse(
                because: "the body is part of the HMAC input and must be byte-exact");
            InternalWebhookHmac.Matches(
                captured.Body, TestWebhookSecret, "9999999999",
                captured.SignatureHeader).Should().BeFalse(
                because: "the timestamp is part of the HMAC input");

            // Assert — the signature is HMAC-SHA256 hex lowercase of the documented length (64 chars).
            captured.SignatureHeader!.Length.Should().Be(64);
            captured.SignatureHeader.Should().MatchRegex("^[0-9a-f]{64}$",
                because: "hex lowercase is the canonical encoding");
        }
        finally
        {
            await factory.DisposeAsync();
        }
    }

    // -----------------------------------------------------------------
    // P1.3 — 5xx from the consumer drives a safe retry
    // -----------------------------------------------------------------

    [Fact]
    public async Task OutboxDispatcher_ShouldMarkRetry_WhenApplicationWebhookReturnsServerError()
    {
        var factory = await CreateFreshFactoryAsync();
        try
        {
            var credentials = await SeedApplicationWithWebhookAsync(factory);

            // Program the captured handler to behave like a 5xx-failing webhook.
            factory.WebhookHandler.EnqueueResponse(HttpStatusCode.InternalServerError);

            var outboxEventId = Guid.NewGuid();
            await EnqueueOutboxAsync(
                factory,
                credentials,
                outboxEventId,
                eventType: "payment.failed",
                payload: new { paymentId = Guid.NewGuid(), status = "Failed" });

            await RunDispatcherOnceAsync(factory);

            // Assert — exactly one outbound POST was attempted.
            factory.WebhookHandler.CallCount.Should().Be(1);
            factory.WebhookHandler.Last!.Method.Should().Be("POST");

            // Assert — event stayed Pending, retry counter incremented, NextRetryAt is future.
            await using var db = factory.CreateDbContext();
            var reloaded = await db.OutboxEvents.AsNoTracking()
                .SingleAsync(o => o.Id == outboxEventId);

            reloaded.Status.Should().Be(OutboxEventStatus.Pending,
                because: "a retry keeps the event in Pending so the next iteration picks it up");
            reloaded.RetryCount.Should().Be(1);
            reloaded.NextRetryAt.Should().NotBeNull();
            reloaded.NextRetryAt!.Value.Should().BeAfter(DateTime.UtcNow.AddSeconds(-5),
                because: "NextRetryAt must point to the future so the next worker tick respects the backoff");

            // Assert — LastError contains the safe Category + statusCode pair only.
            reloaded.LastError.Should().NotBeNullOrEmpty();
            reloaded.LastError.Should().Contain(WebhookDispatcherCategory.HttpFailure.ToString());
            reloaded.LastError.Should().Contain("status=500");
            reloaded.LastError.Should().NotContain(TestWebhookUrl,
                because: "URLs must not appear in LastError");
            reloaded.LastError.Should().NotContain(TestWebhookSecret,
                because: "webhook secrets must never be persisted, logged or exposed");
            reloaded.LastError.Should().NotContain("Internal Server Error",
                because: "the response body / reason phrase must not leak into LastError");
            reloaded.LastError.Should().NotContain("{\"error\":",
                because: "any JSON body fragment must stay out of LastError");
        }
        finally
        {
            await factory.DisposeAsync();
        }
    }

    // -----------------------------------------------------------------
    // P1.4 — 429 from the consumer also drives a safe retry
    // -----------------------------------------------------------------

    [Fact]
    public async Task OutboxDispatcher_ShouldMarkRetry_WhenApplicationWebhookReturnsRateLimited()
    {
        var factory = await CreateFreshFactoryAsync();
        try
        {
            var credentials = await SeedApplicationWithWebhookAsync(factory);

            factory.WebhookHandler.EnqueueResponse(HttpStatusCode.TooManyRequests);

            var outboxEventId = Guid.NewGuid();
            await EnqueueOutboxAsync(
                factory,
                credentials,
                outboxEventId,
                eventType: "payment.checkout.created",
                payload: new { paymentId = Guid.NewGuid(), status = "Pending" });

            await RunDispatcherOnceAsync(factory);

            factory.WebhookHandler.CallCount.Should().Be(1);

            await using var db = factory.CreateDbContext();
            var reloaded = await db.OutboxEvents.AsNoTracking()
                .SingleAsync(o => o.Id == outboxEventId);

            reloaded.Status.Should().Be(OutboxEventStatus.Pending);
            reloaded.RetryCount.Should().Be(1);
            reloaded.NextRetryAt.Should().NotBeNull();
            reloaded.LastError.Should().Be(
                $"{WebhookDispatcherCategory.HttpFailure}: status=429");
        }
        finally
        {
            await factory.DisposeAsync();
        }
    }

    // -----------------------------------------------------------------
    // P1.5 — Unprotect failure never sends an unsigned HTTP request
    // -----------------------------------------------------------------

    [Fact]
    public async Task OutboxDispatcher_ShouldFailSafely_WhenWebhookSecretCannotBeUnprotected()
    {
        var factory = await CreateFreshFactoryAsync();
        try
        {
            // Seed an ApplicationClient whose WebhookSecret is a garbage blob that the
            // production protector will refuse to decrypt. The dispatcher must classify
            // that as UnprotectFailure without leaking the blob anywhere.
            var credentials = await SeedApplicationWithWebhookAsync(
                factory,
                forcedProtectedWebhookSecret: "not-a-valid-base64-aes-blob-xyzzy");

            var outboxEventId = Guid.NewGuid();
            await EnqueueOutboxAsync(
                factory,
                credentials,
                outboxEventId,
                eventType: "payment.status.changed",
                payload: new { paymentId = Guid.NewGuid(), status = "Approved" });

            await RunDispatcherOnceAsync(factory);

            // Assert — NO HTTP request reached the consumer transport.
            factory.WebhookHandler.CallCount.Should().Be(0,
                because: "UnprotectFailure is a security invariant: never send an unsigned request");

            // Assert — the event was marked for retry with the safe Category. The dispatcher
            // throws WebhookDispatcherException(UnprotectFailure) which the worker maps to
            // MarkRetryWithCategory; the retry schedule depends on the backoff policy.
            await using var db = factory.CreateDbContext();
            var reloaded = await db.OutboxEvents.AsNoTracking()
                .SingleAsync(o => o.Id == outboxEventId);

            reloaded.Status.Should().Be(OutboxEventStatus.Pending);
            reloaded.RetryCount.Should().Be(1);
            reloaded.NextRetryAt.Should().NotBeNull();
            reloaded.LastError.Should().Be(WebhookDispatcherCategory.UnprotectFailure.ToString());

            // Assert — LastError MUST NOT carry the corrupted blob, the webhook URL, the
            // would-be secret, or any HTTP framing.
            reloaded.LastError.Should().NotContain("not-a-valid-base64-aes-blob-xyzzy",
                because: "the protected secret blob must never appear in LastError");
            reloaded.LastError.Should().NotContain(TestWebhookUrl);
            reloaded.LastError.Should().NotContain(TestWebhookSecret);
            reloaded.LastError!.Length.Should().BeLessThanOrEqualTo(64,
                because: "UnprotectFailure category name is bounded; long values would mean leakage");
        }
        finally
        {
            await factory.DisposeAsync();
        }
    }

    // -----------------------------------------------------------------
    // P2.1 — AbacatePay webhook drives a real Outbox dispatch
    // -----------------------------------------------------------------

    [Fact]
    public async Task AbacatePayWebhookFlow_ShouldCreateOutbox_AndDispatchInternalWebhook()
    {
        var factory = await CreateFreshFactoryAsync();
        try
        {
            // Arrange — drive the create-checkout pipeline so we have a payment + provider account.
            var (payment, credentials, client) = await CreateCheckoutAsync(factory);

            // Now seed a webhook URL + secret on the same application so the dispatcher has
            // somewhere to send the internal payment.approved outbox event after the inbound
            // provider webhook flips the payment. We use a dedicated helper that re-seeds
            // the application with both fields populated.
            await SeedWebhookUrlAndSecretForApplicationAsync(
                factory,
                credentials,
                webhookUrl: TestWebhookUrl,
                protectedWebhookSecret: factory.ProtectWebhookSecret(TestWebhookSecret));

            try
            {
                // Act — POST a valid transparent.completed webhook from AbacatePay.
                var providerPaymentId = payment.ProviderPaymentId!;
                var envelope = BuildAbacatePayCompletedEnvelope(
                    providerPaymentId: providerPaymentId,
                    tenantId: credentials.TenantId,
                    applicationId: credentials.ApplicationId,
                    paymentId: payment.Id,
                    eventId: $"evt-7it-{Guid.NewGuid():N}",
                    webhookSecret: "test-abacatepay-webhook-secret");
                var envelopeId = EnvelopeId(envelope);
                var signature = ComputeAbacatePayHmac(envelope, "test-abacatepay-webhook-secret");

                using var inbound = new HttpRequestMessage(HttpMethod.Post, "/api/v1/webhooks/AbacatePay")
                {
                    Content = new StringContent(envelope, Encoding.UTF8, "application/json")
                };
                inbound.Headers.Add("X-Webhook-Signature", signature);
                inbound.Headers.Add("X-Provider-Event-Id", envelopeId);

                var inboundResponse = await client.SendAsync(inbound);
                inboundResponse.StatusCode.Should().Be(HttpStatusCode.Accepted);
                var inboundBody = await inboundResponse.Content.ReadFromJsonAsync<JsonElement>();
                var webhookId = inboundBody.GetProperty("webhookId").GetGuid();

                // Slice 7-IT follows Slice 3-IT: the Worker is not hosted by
                // WebApplicationFactory so we explicitly invoke the same processor the
                // worker loop would invoke. This mirrors production behaviour for the
                // Inbox half of the pipeline.
                using (var scope = factory.Services.CreateScope())
                {
                    var processor = scope.ServiceProvider
                        .GetRequiredService<PaymentHub.Application.Webhooks.IProcessWebhookEventHandler>();
                    await processor.ProcessAsync(webhookId, CancellationToken.None);
                }

                // Assert — the Outbox contains the payment.approved event pointing at our
                // application and that event has not been dispatched yet.
                await using (var intermediateDb = factory.CreateDbContext())
                {
                    var approved = await intermediateDb.OutboxEvents.AsNoTracking()
                        .Where(o => o.TenantId == credentials.TenantId
                                    && o.EventType == "payment.approved")
                        .SingleAsync();
                    approved.Status.Should().Be(OutboxEventStatus.Pending);
                    approved.PayloadJson.Should().Contain(payment.Id.ToString());
                }

                // Act — drive the dispatcher. After this the consumer should have received
                // exactly one POST signed with the application webhook secret.
                await RunDispatcherOnceAsync(factory);
            }
            finally
            {
                client.Dispose();
            }

            // Assert — both internal webhooks were delivered, both signed, both pointed at
            // TestWebhookUrl. The dispatcher runs one iteration which fans out to every
            // pending event for this tenant, so the create-checkout outbox and the
            // payment.approved outbox both reach the consumer inside the same tick.
            factory.WebhookHandler.CallCount.Should().Be(2,
                because: "the create-checkout outbox AND the payment.approved outbox are both pending");
            var capturedApprovals = factory.WebhookHandler.Captured
                .Where(c => c.EventTypeHeader == "payment.approved")
                .ToList();
            capturedApprovals.Should().HaveCount(1,
                because: "the webhook processor created exactly one payment.approved outbox");
            var captured = capturedApprovals.Single();
            captured.Url.Should().Be(TestWebhookUrl);
            captured.SignatureHeader.Should().NotBeNullOrEmpty();
            InternalWebhookHmac.Matches(
                captured.Body, TestWebhookSecret, captured.TimestampHeader!,
                captured.SignatureHeader).Should().BeTrue(
                because: "the AbacatePay flow MUST end with a valid HMAC against the app webhook secret");
            captured.Body.Should().NotContain("test-abacatepay-webhook-secret",
                because: "the outbound payload must contain only safe metadata — never the provider secret");

            // Assert — both outbox events transitioned cleanly.
            await using var db = factory.CreateDbContext();
            var outboxEvents = await db.OutboxEvents.AsNoTracking()
                .Where(o => o.TenantId == credentials.TenantId)
                .OrderBy(o => o.CreatedAt)
                .ToListAsync();
            outboxEvents.Should().HaveCount(2,
                because: "the checkout created payment.checkout.created plus the approved webhook payment.approved");
            outboxEvents.Should().OnlyContain(o => o.Status == OutboxEventStatus.Sent);
            outboxEvents.Should().OnlyContain(o => o.LastError == null);
        }
        finally
        {
            await factory.DisposeAsync();
        }
    }

    // -----------------------------------------------------------------
    // P2.2 — An already-Sent event is not redispatched on the next iteration
    // -----------------------------------------------------------------

    [Fact]
    public async Task OutboxDispatcher_ShouldNotDispatchAlreadySentEvent()
    {
        var factory = await CreateFreshFactoryAsync();
        try
        {
            var credentials = await SeedApplicationWithWebhookAsync(factory);

            var outboxEventId = Guid.NewGuid();
            await EnqueueOutboxAsync(
                factory,
                credentials,
                outboxEventId,
                eventType: "payment.status.changed",
                payload: new { paymentId = Guid.NewGuid(), status = "Approved" });

            // First iteration dispatches.
            await RunDispatcherOnceAsync(factory);
            factory.WebhookHandler.CallCount.Should().Be(1);

            // Second iteration MUST be a no-op for the Sent event.
            await RunDispatcherOnceAsync(factory);
            factory.WebhookHandler.CallCount.Should().Be(1,
                because: "the worker uses GetPendingForDispatchAsync which filters out Sent");

            // Sanity check the row in Postgres.
            await using var db = factory.CreateDbContext();
            var reloaded = await db.OutboxEvents.AsNoTracking()
                .SingleAsync(o => o.Id == outboxEventId);
            reloaded.Status.Should().Be(OutboxEventStatus.Sent);
            reloaded.RetryCount.Should().Be(0);
            reloaded.LastError.Should().BeNull();
        }
        finally
        {
            await factory.DisposeAsync();
        }
    }

    // -----------------------------------------------------------------
    // Shared helpers
    // -----------------------------------------------------------------

    /// <summary>
    /// Boots a fresh <see cref="PaymentHubApiFactory"/>, eager-creates a
    /// client (which triggers the host build + migration of the API
    /// pipeline), then truncates every application table so the test
    /// starts from an empty database.
    /// </summary>
    private async Task<PaymentHubApiFactory> CreateFreshFactoryAsync()
    {
        var factory = new PaymentHubApiFactory(_postgres);
        _ = factory.CreateClient();
        await factory.ResetDatabaseAsync();
        return factory;
    }

    /// <summary>
    /// Seeds a tenant + active application with a working webhook URL +
    /// webhook secret (protected via the same <see cref="IWebhookSecretProtector"/>
    /// the production code uses). Returns the credentials the dispatcher
    /// needs to address the seed.
    /// </summary>
    private static async Task<E2ECredentials> SeedApplicationWithWebhookAsync(
        PaymentHubApiFactory factory,
        string? forcedProtectedWebhookSecret = null)
    {
        var protectedSecret = forcedProtectedWebhookSecret
            ?? factory.ProtectWebhookSecret(TestWebhookSecret);
        return await E2ESeedHelpers.SeedTenantAndApplicationAsync(
            factory,
            webhookUrl: TestWebhookUrl,
            protectedWebhookSecret: protectedSecret);
    }

    /// <summary>
    /// Loads the existing <see cref="Domain.Entities.ApplicationClient"/> and updates
    /// its webhook URL + protected secret without disturbing the rest of the application
    /// (API key, status, name). Mirrors what the Slice 2-C handler does in production.
    /// </summary>
    private static async Task SeedWebhookUrlAndSecretForApplicationAsync(
        PaymentHubApiFactory factory,
        E2ECredentials credentials,
        string webhookUrl,
        string protectedWebhookSecret)
    {
        using var scope = factory.Services.CreateScope();
        var apps = scope.ServiceProvider.GetRequiredService<IApplicationClientRepository>();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var app = await apps.GetByTenantAndIdAsync(
            credentials.TenantId, credentials.ApplicationId, CancellationToken.None);
        app.Should().NotBeNull("the tenant + application must already be seeded");
        app!.UpdateWebhook(webhookUrl, protectedWebhookSecret);
        await uow.SaveChangesAsync(CancellationToken.None);
    }

    /// <summary>
    /// Drives the create-checkout happy path so the P2.1 test can reuse a
    /// fully-formed <see cref="Domain.Entities.Payment"/>. Mirrors
    /// <see cref="AbacatePayCheckoutE2ETests.CreateCheckoutAsync"/>.
    /// </summary>
    private static async Task<(Domain.Entities.Payment Payment,
                                E2ECredentials Credentials,
                                HttpClient Client)>
        CreateCheckoutAsync(PaymentHubApiFactory factory)
    {
        const string testAbacatePayApiKey = "test-abacatepay-api-key";
        const string testAbacatePayWebhookSecret = "test-abacatepay-webhook-secret";

        var credentials = await E2ESeedHelpers.SeedTenantAndApplicationAsync(
            factory,
            defaultProvider: ProviderCode.AbacatePay);

        var encrypted = factory.ProtectAbacatePayCredentials(
            testAbacatePayApiKey, testAbacatePayWebhookSecret);
        await E2ESeedHelpers.SeedProviderAccountAsync(
            factory,
            tenantId: credentials.TenantId,
            applicationId: credentials.ApplicationId,
            providerCode: ProviderCode.AbacatePay,
            environment: ProviderEnvironment.Sandbox,
            name: "Acme AbacatePay Sandbox",
            encryptedCredentials: encrypted,
            isDefault: true);

        var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/checkouts")
        {
            Content = JsonContent.Create(BuildCheckoutPayload(credentials))
        };
        request.Headers.Add("Authorization", credentials.AuthorizationHeader);
        request.Headers.Add("X-Tenant-Id", credentials.TenantIdHeader);
        request.Headers.Add("X-Application-Id", credentials.ApplicationIdHeader);
        request.Headers.Add("Idempotency-Key", "e2e-7it-idem");

        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var paymentId = body.GetProperty("paymentId").GetGuid();

        await using var db = factory.CreateDbContext();
        var payment = await db.Payments.AsNoTracking().SingleAsync(p => p.Id == paymentId);
        return (payment, credentials, client);
    }

    private static object BuildCheckoutPayload(E2ECredentials credentials) => new
    {
        externalReference = $"ext-{Guid.NewGuid():N}",
        customer = new { name = "Buyer Name", email = "buyer@example.com" },
        items = new[]
        {
            new { id = "sku-1", name = "Pro plan", quantity = 1, unitAmount = 10000L },
            new { id = "sku-2", name = "Add-on", quantity = 3, unitAmount = 500L }
        },
        currency = "BRL",
        successUrl = "https://example.test/success",
        cancelUrl = "https://example.test/cancel",
        metadata = new Dictionary<string, string>
        {
            ["tenantId"] = credentials.TenantId.ToString(),
            ["applicationId"] = credentials.ApplicationId.ToString()
        }
    };

    /// <summary>
    /// Persists an <c>OutboxEvent</c> with the supplied payload through the
    /// production <see cref="IOutboxPublisher"/>. Going through the real
    /// publisher (rather than inserting rows directly) keeps the JSON shape +
    /// EF tracking identical to what the production code produces.
    /// </summary>
    private static async Task EnqueueOutboxAsync(
        PaymentHubApiFactory factory,
        E2ECredentials credentials,
        Guid outboxEventId,
        string eventType,
        object payload)
    {
        using var scope = factory.Services.CreateScope();
        var publisher = scope.ServiceProvider.GetRequiredService<IOutboxPublisher>();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        await publisher.EnqueueAsync(
            outboxEventId,
            credentials.TenantId,
            credentials.ApplicationId,
            eventType,
            payload,
            CancellationToken.None);
        await uow.SaveChangesAsync(CancellationToken.None);
    }

    /// <summary>
    /// Constructs an <see cref="OutboxDispatcherWorker"/> from the factory's
    /// service provider (the worker is hosted by the Worker process, NOT by
    /// the API host) and drives a single iteration. Mirrors the production
    /// behaviour because the worker resolves the same scoped services.
    /// </summary>
    private static async Task RunDispatcherOnceAsync(PaymentHubApiFactory factory)
    {
        var scopeFactory = factory.Services.GetRequiredService<IServiceScopeFactory>();
        var clock = factory.Services.GetRequiredService<IClock>();
        var logger = factory.Services
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger<OutboxDispatcherWorker>();
        var options = factory.Services.GetRequiredService<IOptions<PaymentHubOptions>>();

        var worker = new OutboxDispatcherWorker(scopeFactory, clock, logger, options);
        await worker.DispatchOnceAsync(CancellationToken.None);
    }

    /// <summary>
    /// Builds an AbacatePay v2 transparent-completed envelope. Same shape as
    /// <see cref="AbacatePayCheckoutE2ETests"/>; duplicated here so the two
    /// test classes remain independent.
    /// </summary>
    private static string BuildAbacatePayCompletedEnvelope(
        string providerPaymentId,
        Guid tenantId,
        Guid applicationId,
        Guid paymentId,
        string eventId,
        string webhookSecret)
    {
        var envelope = new
        {
            id = eventId,
            @event = "transparent.completed",
            apiVersion = 2,
            devMode = true,
            data = new
            {
                id = providerPaymentId,
                status = "PAID",
                amount = 11500L,
                devMode = true,
                metadata = new Dictionary<string, string>
                {
                    ["tenantId"] = tenantId.ToString(),
                    ["applicationId"] = applicationId.ToString(),
                    ["paymentId"] = paymentId.ToString()
                }
            }
        };
        _ = webhookSecret; // parameter kept for symmetry with future variants; not embedded.
        return JsonSerializer.Serialize(envelope, new JsonSerializerOptions(JsonSerializerDefaults.Web));
    }

    private static string ComputeAbacatePayHmac(string rawBody, string secret)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(rawBody));
        return Convert.ToBase64String(hash);
    }

    private static string EnvelopeId(string envelopeJson)
    {
        using var doc = JsonDocument.Parse(envelopeJson);
        return doc.RootElement.GetProperty("id").GetString()
            ?? throw new InvalidOperationException("Envelope id is missing.");
    }
}
