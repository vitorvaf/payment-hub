using System.Net;
using System.Text;
using System.Text.Json;

namespace PaymentHub.IntegrationTests.Support;

/// <summary>
/// In-memory HTTP handler used to replace the named <c>abacatepay</c>
/// <see cref="HttpClient"/> during end-to-end tests. Validates that the
/// outbound request carries the expected <c>Bearer</c> token and metadata
/// (tenantId/applicationId/paymentId), then responds with a deterministic
/// AbacatePay envelope mirroring the production sandbox contract.
/// </summary>
/// <remarks>
/// <para>
/// Captures the last request body + headers so the test can assert what the
/// adapter actually sent to the provider. The response payload is built
/// per request from a deterministic id so callers can correlate the
/// returned <c>id</c> with the captured request metadata.
/// </para>
/// <para>
/// This handler MUST only be registered for the <c>abacatepay</c> named
/// client (the <see cref="PaymentHubApiFactory"/> does exactly that). It is
/// not designed to be a general-purpose HTTP recorder.
/// </para>
/// </remarks>
public sealed class AbacatePayFakeHttpHandler : HttpMessageHandler
{
    /// <summary>
    /// API key the test expects the outbound request to carry in
    /// <c>Authorization: Bearer ...</c>. Compared verbatim (case-sensitive)
    /// because the real AbacatePay API key is opaque.
    /// </summary>
    public string ExpectedApiKey { get; set; } = "test-abacatepay-api-key";

    /// <summary>
    /// Whether the fake should accept the request. Tests can flip this to
    /// simulate provider failures (returns 500 envelope) without rebuilding
    /// the handler.
    /// </summary>
    public bool SimulateEnvelopeFailure { get; set; }

    /// <summary>
    /// Last request body captured (UTF-8 decoded).
    /// </summary>
    public string? LastRequestBody { get; private set; }

    /// <summary>
    /// Last request authorization header value (without the <c>Bearer </c>
    /// prefix). Null when the header was missing or malformed.
    /// </summary>
    public string? LastAuthorizationHeader { get; private set; }

    /// <summary>
    /// Last request HTTP method (POST expected for create).
    /// </summary>
    public string? LastRequestMethod { get; private set; }

    /// <summary>
    /// Last request URI relative to the named client <c>BaseAddress</c>.
    /// </summary>
    public string? LastRequestPath { get; private set; }

    /// <summary>
    /// Total number of calls handled since the handler was created.
    /// </summary>
    public int CallCount { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        CallCount++;

        LastRequestMethod = request.Method.Method;
        LastRequestPath = request.RequestUri?.AbsolutePath;

        if (request.Headers.Authorization is { Scheme: "Bearer" } auth)
        {
            LastAuthorizationHeader = auth.Parameter;
        }
        else
        {
            LastAuthorizationHeader = null;
        }

        if (request.Content is not null)
        {
            LastRequestBody = request.Content.ReadAsStringAsync(cancellationToken).GetAwaiter().GetResult();
        }
        else
        {
            LastRequestBody = null;
        }

        if (SimulateEnvelopeFailure)
        {
            return Task.FromResult(BuildJson(
                HttpStatusCode.InternalServerError,
                new
                {
                    data = (object?)null,
                    success = false,
                    error = "simulated_failure"
                }));
        }

        // Build a deterministic id so the test can correlate the response
        // with the captured request metadata.
        var providerPaymentId = ExtractMetadataPaymentId(LastRequestBody)
            ?? Guid.NewGuid().ToString("N");

        var responseBody = new
        {
            data = new
            {
                id = providerPaymentId,
                status = "PENDING",
                amount = ExtractAmount(LastRequestBody),
                @expiresAt = DateTimeOffset.UtcNow.AddHours(1),
                brCode = $"00020126580014BR.GOV.BCB.PIX0136test-{providerPaymentId}",
                brCodeBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes($"brCode:{providerPaymentId}")),
                devMode = true
            },
            success = true,
            error = (string?)null
        };

        return Task.FromResult(BuildJson(HttpStatusCode.OK, responseBody));
    }

    private static long? ExtractAmount(string? body)
    {
        if (string.IsNullOrWhiteSpace(body)) return null;
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("amount", out var amountElement))
            {
                return amountElement.ValueKind == JsonValueKind.Number
                    ? amountElement.GetInt64()
                    : null;
            }
        }
        catch (JsonException)
        {
            // body was malformed — leave null.
        }
        return null;
    }

    private static string? ExtractMetadataPaymentId(string? body)
    {
        if (string.IsNullOrWhiteSpace(body)) return null;
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("metadata", out var meta)
                && meta.ValueKind == JsonValueKind.Object
                && meta.TryGetProperty("paymentId", out var paymentId)
                && paymentId.ValueKind == JsonValueKind.String)
            {
                return paymentId.GetString();
            }
        }
        catch (JsonException)
        {
            // body was malformed — fall back to a random id.
        }
        return null;
    }

    private static HttpResponseMessage BuildJson(HttpStatusCode status, object payload)
    {
        var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        return new HttpResponseMessage(status)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }
}