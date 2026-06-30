using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PaymentHub.Domain.Enums;
using PaymentHub.Infrastructure.Postgres;
using PaymentHub.IntegrationTests.Infrastructure;
using PaymentHub.IntegrationTests.Support;

namespace PaymentHub.IntegrationTests.EndToEnd;

/// <summary>
/// End-to-end tests that exercise the entire Payment Hub API through
/// <see cref="Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory{TEntryPoint}"/>
/// against a real PostgreSQL (Testcontainers) + real DI graph + real
/// provider adapter, with only the outbound HTTP transport replaced by
/// deterministic fakes (no calls leave the test process).
/// </summary>
[Collection(PostgresCollection.Name)]
[Trait("Category", "Integration")]
public class AbacatePayCheckoutE2ETests
{
    private readonly PostgresFixture _postgres;

    /// <summary>
    /// Fake AbacatePay API key the test expects to see in the outbound
    /// <c>Authorization: Bearer ...</c> header. Must mirror what we seed
    /// in <see cref="E2ESeedHelpers"/>.
    /// </summary>
    private const string TestAbacatePayApiKey = "test-abacatepay-api-key";

    /// <summary>
    /// Fake webhook secret used both by the provider account seed and by
    /// the HMAC signatures in the webhook tests.
    /// </summary>
    private const string TestAbacatePayWebhookSecret = "test-abacatepay-webhook-secret";

    public AbacatePayCheckoutE2ETests(PostgresFixture postgres)
    {
        _postgres = postgres;
    }

    // -----------------------------------------------------------------
    // P1.1 — CreateCheckout E2E (AbacatePay fake)
    // -----------------------------------------------------------------

    [Fact]
    public async Task CreateCheckout_WithAbacatePayFake_PersistsPaymentAndOutbox()
    {
        var factory = await CreateFreshFactoryAsync();
        try
        {
            var credentials = await E2ESeedHelpers.SeedTenantAndApplicationAsync(
                factory,
                defaultProvider: ProviderCode.AbacatePay);

            var encryptedCredentials = factory.ProtectAbacatePayCredentials(
                apiKey: TestAbacatePayApiKey,
                webhookSecret: TestAbacatePayWebhookSecret);

            await E2ESeedHelpers.SeedProviderAccountAsync(
                factory,
                tenantId: credentials.TenantId,
                applicationId: credentials.ApplicationId,
                providerCode: ProviderCode.AbacatePay,
                environment: ProviderEnvironment.Sandbox,
                name: "Acme AbacatePay Sandbox",
                encryptedCredentials: encryptedCredentials,
                isDefault: true);

            using var client = factory.CreateClient();
            using var request = BuildCreateCheckoutRequest(credentials, idempotencyKey: "e2e-idem-001");

            // Act — POST /api/v1/checkouts
            var response = await client.SendAsync(request);

            // Assert — HTTP 201 with the canonical public DTO contract.
            response.StatusCode.Should().Be(HttpStatusCode.Created);

            var body = await response.Content.ReadFromJsonAsync<JsonElement>();
            var paymentId = body.GetProperty("paymentId").GetGuid();
            paymentId.Should().NotBeEmpty();
            body.GetProperty("status").GetString().Should().Be(PaymentStatus.Pending.ToString());
            body.GetProperty("provider").GetString().Should().Be(ProviderCode.AbacatePay.ToString());
            body.GetProperty("checkoutUrl").GetString().Should().StartWith("abacatepay://pix/");

            // The public response MUST NOT leak provider credentials or
            // PIX copy-paste codes (deferred slice — see audit report).
            var rawJson = body.GetRawText();
            rawJson.Should().NotContain(TestAbacatePayApiKey);
            rawJson.Should().NotContain(TestAbacatePayWebhookSecret);
            rawJson.Should().NotContain("brCode"); // Slice 3-IT does not yet expose brCode on the public DTO.

            // Assert — the fake transport saw exactly one outbound call with
            // the expected metadata + Bearer token.
            factory.AbacatePayHandler.CallCount.Should().Be(1);
            factory.AbacatePayHandler.LastRequestMethod.Should().Be("POST");
            factory.AbacatePayHandler.LastRequestPath.Should().Be("/v2/transparents/create");
            factory.AbacatePayHandler.LastAuthorizationHeader.Should().Be(TestAbacatePayApiKey);

            var capturedBody = factory.AbacatePayHandler.LastRequestBody;
            capturedBody.Should().NotBeNullOrEmpty();
            using var sent = JsonDocument.Parse(capturedBody!);
            var sentRoot = sent.RootElement;
            // (10*1000) + (3*500) = 10000 + 1500 = 11500 cents
            sentRoot.GetProperty("amount").GetInt64().Should().Be(11500L);
            var metadata = sentRoot.GetProperty("metadata");
            metadata.GetProperty("tenantId").GetString().Should().Be(credentials.TenantId.ToString());
            metadata.GetProperty("applicationId").GetString().Should().Be(credentials.ApplicationId.ToString());
            metadata.GetProperty("paymentId").GetGuid().Should().Be(paymentId);

            // Assert — the database reflects the canonical pipeline.
            await using var db = factory.CreateDbContext();
            var payments = await db.Payments.AsNoTracking()
                .Where(p => p.TenantId == credentials.TenantId)
                .ToListAsync();
            payments.Should().HaveCount(1);
            var payment = payments[0];
            payment.Status.Should().Be(PaymentStatus.Pending);
            payment.SelectedProvider.Should().Be(ProviderCode.AbacatePay);
            payment.ProviderPaymentId.Should().NotBeNullOrEmpty();
            payment.CheckoutUrl.Should().StartWith("abacatepay://pix/");
            payment.CustomerEmail.Should().Be("buyer@example.com");
            payment.CustomerName.Should().Be("Buyer Name");

            var attempts = await db.PaymentAttempts.AsNoTracking()
                .Where(a => a.PaymentId == payment.Id)
                .ToListAsync();
            attempts.Should().HaveCount(1);
            attempts[0].Status.Should().Be(PaymentAttemptStatus.Succeeded);
            attempts[0].ProviderCode.Should().Be(ProviderCode.AbacatePay);

            var idempotencyKeys = await db.IdempotencyKeys.AsNoTracking()
                .Where(k => k.PaymentId == payment.Id)
                .ToListAsync();
            idempotencyKeys.Should().HaveCount(1);
            idempotencyKeys[0].Key.Should().Be("e2e-idem-001");

            var outboxEvents = await db.OutboxEvents.AsNoTracking()
                .Where(o => o.TenantId == credentials.TenantId)
                .ToListAsync();
            outboxEvents.Should().HaveCount(1);
            outboxEvents[0].EventType.Should().Be("payment.checkout.created");
            outboxEvents[0].Status.Should().Be(OutboxEventStatus.Pending);
            outboxEvents[0].PayloadJson.Should().Contain(paymentId.ToString());
            outboxEvents[0].PayloadJson.Should().NotContain(TestAbacatePayApiKey);
            outboxEvents[0].PayloadJson.Should().NotContain(TestAbacatePayWebhookSecret);

            // The outbound webhook receiver MUST NOT have been called yet
            // — the create-checkout outbox event is enqueued, not dispatched.
            factory.WebhookHandler.Captured.Should().BeEmpty();
        }
        finally
        {
            await factory.DisposeAsync();
        }
    }

    // -----------------------------------------------------------------
    // P1.2 — Inbound AbacatePay webhook (valid signature)
    // -----------------------------------------------------------------

    [Fact]
    public async Task ProviderWebhook_ValidSignature_UpdatesPaymentAndEnqueuesOutbox()
    {
        var factory = await CreateFreshFactoryAsync();
        try
        {
            // Arrange — create a checkout so the webhook handler can find
            // a matching Payment via (tenantId, providerPaymentId).
            var (payment, credentials, client) = await CreateCheckoutAsync(factory);

            try
            {
                var providerPaymentId = payment.ProviderPaymentId!;
                var envelope = BuildAbacatePayCompletedEnvelope(
                    providerPaymentId: providerPaymentId,
                    providerStatus: "PAID",
                    tenantId: credentials.TenantId,
                    applicationId: credentials.ApplicationId,
                    paymentId: payment.Id,
                    eventId: $"evt-{Guid.NewGuid():N}");

                var signature = ComputeAbacatePayHmacSignature(envelope, TestAbacatePayWebhookSecret);
                var envelopeId = GetEnvelopeId(envelope);

                // Act — POST the webhook.
                using var webhookRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/webhooks/AbacatePay")
                {
                    Content = new StringContent(envelope, Encoding.UTF8, "application/json")
                };
                webhookRequest.Headers.Add("X-Webhook-Signature", signature);
                webhookRequest.Headers.Add("X-Provider-Event-Id", envelopeId);

                var response = await client.SendAsync(webhookRequest);
                response.StatusCode.Should().Be(HttpStatusCode.Accepted);

                var webhookResponseBody = await response.Content.ReadFromJsonAsync<JsonElement>();
                var webhookId = webhookResponseBody.GetProperty("webhookId").GetGuid();

// The Worker does not run inside WebApplicationFactory; we
                // explicitly invoke the same handler the Worker would call.
                // This is the documented testability decision for Slice 3-IT.
                using (var scope = factory.Services.CreateScope())
                {
                    var processor = scope.ServiceProvider
                        .GetRequiredService<PaymentHub.Application.Webhooks.IProcessWebhookEventHandler>();
                    await processor.ProcessAsync(webhookId, CancellationToken.None);
                }

                // Assert — Payment is now Approved.
                await using var db = factory.CreateDbContext();
                var refreshed = await db.Payments.AsNoTracking()
                    .SingleAsync(p => p.Id == payment.Id);
                refreshed.Status.Should().Be(PaymentStatus.Approved);
                refreshed.ProviderPaymentId.Should().Be(providerPaymentId);

                var attempts = await db.PaymentAttempts.AsNoTracking()
                    .Where(a => a.PaymentId == payment.Id)
                    .ToListAsync();
                attempts.Should().HaveCount(2);
                attempts.Should().Contain(a => a.Status == PaymentAttemptStatus.Succeeded);

                var outboxEvents = await db.OutboxEvents.AsNoTracking()
                    .Where(o => o.TenantId == credentials.TenantId)
                    .OrderBy(o => o.CreatedAt)
                    .ToListAsync();
                outboxEvents.Should().HaveCount(2);
                outboxEvents[0].EventType.Should().Be("payment.checkout.created");
                outboxEvents[1].EventType.Should().Be("payment.approved");
                outboxEvents[1].PayloadJson.Should().Contain(payment.Id.ToString());
                outboxEvents[1].PayloadJson.Should().NotContain(TestAbacatePayApiKey);
                outboxEvents[1].PayloadJson.Should().NotContain(TestAbacatePayWebhookSecret);

                var webhookEvent = await db.WebhookEvents.AsNoTracking()
                    .SingleAsync(w => w.Id == webhookId);
                webhookEvent.ProcessingStatus.Should().Be(WebhookProcessingStatus.Processed);
                webhookEvent.LastError.Should().BeNull();
                webhookEvent.ProviderEventId.Should().Be(envelopeId);
                webhookEvent.TenantId.Should().Be(credentials.TenantId);
                webhookEvent.ApplicationId.Should().Be(credentials.ApplicationId);

                // No outbound ApplicationClient webhook — ApplicationClient
                // has no WebhookUrl configured.
                factory.WebhookHandler.Captured.Should().BeEmpty();
            }
            finally
            {
                client.Dispose();
            }
        }
        finally
        {
            await factory.DisposeAsync();
        }
    }

    // -----------------------------------------------------------------
    // P1.3 — Inbound AbacatePay webhook is idempotent on duplicate event
    // -----------------------------------------------------------------

    [Fact]
    public async Task ProviderWebhook_DuplicateAbacatePayEvent_IsIdempotent()
    {
        var factory = await CreateFreshFactoryAsync();
        try
        {
            var (payment, credentials, client) = await CreateCheckoutAsync(factory);

            try
            {
                var envelope = BuildAbacatePayCompletedEnvelope(
                    providerPaymentId: payment.ProviderPaymentId!,
                    providerStatus: "PAID",
                    tenantId: credentials.TenantId,
                    applicationId: credentials.ApplicationId,
                    paymentId: payment.Id,
                    eventId: $"evt-dup-{Guid.NewGuid():N}");

                var signature = ComputeAbacatePayHmacSignature(envelope, TestAbacatePayWebhookSecret);
                var envelopeId = GetEnvelopeId(envelope);

                // First POST — establishes the WebhookEvent row.
                var firstResponse = await SendAbacatePayWebhookAsync(client, envelope, signature, envelopeId);
                firstResponse.StatusCode.Should().Be(HttpStatusCode.Accepted);
                var firstBody = await firstResponse.Content.ReadFromJsonAsync<JsonElement>();
                var firstWebhookId = firstBody.GetProperty("webhookId").GetGuid();

                // Second POST with the SAME event id — must return the SAME
                // webhookId and MUST NOT create a new WebhookEvent row.
                var secondResponse = await SendAbacatePayWebhookAsync(client, envelope, signature, envelopeId);
                secondResponse.StatusCode.Should().Be(HttpStatusCode.Accepted);
                var secondBody = await secondResponse.Content.ReadFromJsonAsync<JsonElement>();
                var secondWebhookId = secondBody.GetProperty("webhookId").GetGuid();
                secondWebhookId.Should().Be(firstWebhookId);

                // Process the single WebhookEvent (idempotency-wise this is
                // called once by the worker; the second POST would never
                // trigger a new dispatch).
                using (var scope = factory.Services.CreateScope())
                {
                    var processor = scope.ServiceProvider
                        .GetRequiredService<PaymentHub.Application.Webhooks.IProcessWebhookEventHandler>();
                    await processor.ProcessAsync(firstWebhookId, CancellationToken.None);
                }

                await using var db = factory.CreateDbContext();
                var webhookCount = await db.WebhookEvents.AsNoTracking().CountAsync();
                webhookCount.Should().Be(1); // duplicate event id must collapse into the same row.

                var attempts = await db.PaymentAttempts.AsNoTracking()
                    .Where(a => a.PaymentId == payment.Id)
                    .ToListAsync();
                attempts.Should().HaveCount(2); // checkout attempt + one webhook-driven approved attempt.

                var outboxEvents = await db.OutboxEvents.AsNoTracking()
                    .Where(o => o.TenantId == credentials.TenantId)
                    .ToListAsync();
                outboxEvents.Should().HaveCount(2);
            }
            finally
            {
                client.Dispose();
            }
        }
        finally
        {
            await factory.DisposeAsync();
        }
    }

    // -----------------------------------------------------------------
    // P1.4 — Inbound AbacatePay webhook without signature returns 401
    // -----------------------------------------------------------------

    [Fact]
    public async Task ProviderWebhook_MissingSignature_Rejected401WithoutPersist()
    {
        var factory = await CreateFreshFactoryAsync();
        try
        {
            // Arrange — seed tenant + application so the controller path is
            // fully exercised even though the auth middleware never runs
            // for the webhook endpoint.
            var credentials = await E2ESeedHelpers.SeedTenantAndApplicationAsync(
                factory,
                defaultProvider: ProviderCode.AbacatePay);

            using var client = factory.CreateClient();

            // Act — POST without X-Webhook-Signature.
            var envelope = BuildAbacatePayCompletedEnvelope(
                providerPaymentId: $"pix-{Guid.NewGuid():N}",
                providerStatus: "PAID",
                tenantId: credentials.TenantId,
                applicationId: credentials.ApplicationId,
                paymentId: Guid.NewGuid(),
                eventId: $"evt-no-sig-{Guid.NewGuid():N}");

            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/webhooks/AbacatePay")
            {
                Content = new StringContent(envelope, Encoding.UTF8, "application/json")
            };
            // Deliberately NO X-Webhook-Signature header.

            var response = await client.SendAsync(request);

            // Assert — fail-fast 401 from the controller.
            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
            var body = await response.Content.ReadFromJsonAsync<JsonElement>();
            body.GetProperty("error").GetString().Should().Be("missing_signature");

            // Assert — NO WebhookEvent row was persisted.
            await using var db = factory.CreateDbContext();
            var webhookCount = await db.WebhookEvents.AsNoTracking().CountAsync();
            webhookCount.Should().Be(0); // fail-fast 401 must not touch the database.
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
    /// Drives the create-checkout happy path so the webhook tests can
    /// reuse a fully-formed <see cref="PaymentHub.Domain.Entities.Payment"/>.
    /// </summary>
    private async Task<(PaymentHub.Domain.Entities.Payment Payment,
                        E2ECredentials Credentials,
                        HttpClient Client)>
        CreateCheckoutAsync(PaymentHubApiFactory factory)
    {
        var credentials = await E2ESeedHelpers.SeedTenantAndApplicationAsync(
            factory,
            defaultProvider: ProviderCode.AbacatePay);

        var encrypted = factory.ProtectAbacatePayCredentials(
            TestAbacatePayApiKey, TestAbacatePayWebhookSecret);
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
        using var request = BuildCreateCheckoutRequest(credentials, idempotencyKey: "e2e-webhook-idem");

        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var paymentId = body.GetProperty("paymentId").GetGuid();

        await using var db = factory.CreateDbContext();
        var payment = await db.Payments.AsNoTracking()
            .SingleAsync(p => p.Id == paymentId);
        return (payment, credentials, client);
    }

    private static HttpRequestMessage BuildCreateCheckoutRequest(
        E2ECredentials credentials,
        string idempotencyKey)
    {
        var payload = new
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

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/checkouts")
        {
            Content = JsonContent.Create(payload)
        };
        request.Headers.Add("Authorization", credentials.AuthorizationHeader);
        request.Headers.Add("X-Tenant-Id", credentials.TenantIdHeader);
        request.Headers.Add("X-Application-Id", credentials.ApplicationIdHeader);
        request.Headers.Add("Idempotency-Key", idempotencyKey);
        return request;
    }

    /// <summary>
    /// Builds an AbacatePay v2 transparent-completed envelope with the
    /// metadata block the handler needs to resolve tenant/application.
    /// </summary>
    private static string BuildAbacatePayCompletedEnvelope(
        string providerPaymentId,
        string providerStatus,
        Guid tenantId,
        Guid applicationId,
        Guid paymentId,
        string eventId)
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
                status = providerStatus,
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
        return JsonSerializer.Serialize(envelope, new JsonSerializerOptions(JsonSerializerDefaults.Web));
    }

    private static async Task<HttpResponseMessage> SendAbacatePayWebhookAsync(
        HttpClient client,
        string envelope,
        string base64Signature,
        string providerEventId)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/webhooks/AbacatePay")
        {
            Content = new StringContent(envelope, Encoding.UTF8, "application/json")
        };
        request.Headers.Add("X-Webhook-Signature", base64Signature);
        request.Headers.Add("X-Provider-Event-Id", providerEventId);
        return await client.SendAsync(request);
    }

    /// <summary>
    /// Computes the Base64-encoded HMAC-SHA256 signature that
    /// <see cref="PaymentHub.Infrastructure.Providers.AbacatePay.Webhooks.HmacAbacatePayWebhookSignatureVerifier"/>
    /// expects on the <c>X-Webhook-Signature</c> header.
    /// </summary>
    private static string ComputeAbacatePayHmacSignature(string rawBody, string webhookSecret)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(webhookSecret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(rawBody));
        return Convert.ToBase64String(hash);
    }

    private static string GetEnvelopeId(string envelopeJson)
    {
        using var doc = JsonDocument.Parse(envelopeJson);
        return doc.RootElement.GetProperty("id").GetString()
            ?? throw new InvalidOperationException("Envelope id is missing.");
    }
}