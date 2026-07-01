using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PaymentHub.Application.Abstractions.Providers;
using PaymentHub.Application.Abstractions.Security;
using PaymentHub.Domain.Enums;
using PaymentHub.Infrastructure.Providers.AbacatePay.Models;

namespace PaymentHub.Infrastructure.Providers.AbacatePay;

/// <summary>
/// Slice 2-C.1. Real <see cref="IProviderWebhookManagementClient"/>
/// against the AbacatePay <c>POST /webhooks/create</c> endpoint.
/// Replaces the no-op default from Slice 2-C; the configure-handler
/// already drives registration through this interface so no
/// handler-level change is required.
///
/// <para>
/// <b>Lifecycle:</b> registered as <c>Singleton</c> via
/// <c>AddPaymentHubProviders</c>. All its dependencies
/// (<see cref="IHttpClientFactory"/>, <see cref="IOptionsMonitor{T}"/>,
/// <see cref="ICredentialProtector"/>, <see cref="ILogger{T}"/>) are
/// singleton-safe — no captive-dependency risk.
/// </para>
/// <para>
/// <b>Security baseline (re-asserting audit Slice 2-C anti-patterns):</b>
/// <list type="bullet">
///   <item>The <c>apiKey</c> is extracted from
///   <paramref name="protectedCredentials"/> via
///   <see cref="ProviderAccountCredentialsInspector"/>; the raw key
///   lives only in the local <c>apiKey</c> variable and dies when the
///   request returns.</item>
///   <item>The <c>webhookSecret</c> is forwarded to the upstream as
///   the <c>secret</c> field of the JSON body but never logged,
///   never persisted, never echoed.</item>
///   <item>Failure messages always use the <c>AbacatePayErrorCategory</c>
///   enum name and a status code — never the raw body or any
///   credential.</item>
/// </list>
/// </para>
/// </summary>
public sealed class AbacatePayWebhookManagementClient
    : IProviderWebhookManagementClient
{
    /// <summary>
    /// Named <see cref="HttpClient"/> used for outbound webhook
    /// registration calls. Kept distinct from the <see cref="AbacatePayClient"/>
    /// named client (<c>abacatepay</c>) so that the webhook-management
    /// lifecycle can be tuned independently of the create-transport
    /// lifecycle (different timeout, retry policy, or rate-limit
    /// reservation in the future).
    /// </summary>
    public const string HttpClientName = "abacatepay-webhooks";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptionsMonitor<AbacatePayOptions> _options;
    private readonly IProviderAccountCredentialsReader _credentialsReader;
    private readonly IProviderWebhookRegistrationFeaturePolicy _featurePolicy;
    private readonly ILogger<AbacatePayWebhookManagementClient> _logger;

    public AbacatePayWebhookManagementClient(
        IHttpClientFactory httpClientFactory,
        IOptionsMonitor<AbacatePayOptions> options,
        IProviderAccountCredentialsReader credentialsReader,
        IProviderWebhookRegistrationFeaturePolicy featurePolicy,
        ILogger<AbacatePayWebhookManagementClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options;
        _credentialsReader = credentialsReader;
        _featurePolicy = featurePolicy;
        _logger = logger;
    }

    public async Task<ProviderWebhookRegistrationOutcome> RegisterWebhookAsync(
        ProviderCode providerCode,
        string protectedCredentials,
        string webhookSecret,
        string callbackUrl,
        IReadOnlyList<string> events,
        CancellationToken cancellationToken)
    {
        // ---- 1. Provider gate ----
        // Only AbacatePay is supported today. Anything else returns
        // RegistrationFailed without touching the network — keeps the
        // configuration surface narrow until a second provider lands.
        if (providerCode != ProviderCode.AbacatePay)
        {
            _logger.LogWarning(
                "AbacatePayWebhookManagementClient called with non-AbacatePay provider {ProviderCode}; refusing.",
                providerCode);
            return ProviderWebhookRegistrationOutcome.RegistrationFailed;
        }

        // ---- 2. Feature flag gate ----
        // The handler already short-circuits when the flag is off, but
        // we double-check here so this client cannot be misused by a
        // future caller that skips the handler-level guard.
        if (!_featurePolicy.IsRemoteRegistrationEnabled(providerCode))
        {
            _logger.LogWarning(
                "AbacatePayWebhookManagementClient refused: registration feature flag is off (provider={ProviderCode}).",
                providerCode);
            return ProviderWebhookRegistrationOutcome.RegistrationFailed;
        }

        // ---- 3. Pre-flight ----
        // Defensive: a malformed callbackUrl, an empty event list, or a
        // missing apiKey all imply the caller already passed through the
        // validator (which gates these). Reject defensively but never
        // surface which check failed (anti-enumeration on input).
        if (string.IsNullOrWhiteSpace(callbackUrl)
            || events is null || events.Count == 0
            || string.IsNullOrWhiteSpace(webhookSecret))
        {
            _logger.LogWarning(
                "AbacatePayWebhookManagementClient refused: incomplete request (provider={ProviderCode}).",
                providerCode);
            return ProviderWebhookRegistrationOutcome.RegistrationFailed;
        }

        var apiKey = _credentialsReader.ReadApiKey(protectedCredentials);
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            _logger.LogWarning(
                "AbacatePayWebhookManagementClient could not extract apiKey from protected credentials.");
            return ProviderWebhookRegistrationOutcome.RegistrationFailed;
        }

        // ---- 4. Compute path & build payload (NEVER logged in full) ----
        // We follow the Slice 2-A gotcha: BaseAddress is "https://...abacatepay.com/v2/"
        // (configured in ProvidersServiceCollectionExtensions), so paths are
        // RELATIVE — using "webhooks/create" (no leading slash) preserves the
        // /v2/ segment from the BaseAddress.
        const string path = "webhooks/create";

        var payload = new AbacatePayCreateWebhookRequest
        {
            Name = $"Payment Hub - {providerCode}",
            Endpoint = callbackUrl,
            Secret = webhookSecret,
            Events = events
        };

        // ---- 5. Send ----
        try
        {
            using var http = _httpClientFactory.CreateClient(HttpClientName);
            using var request = new HttpRequestMessage(HttpMethod.Post, path)
            {
                Content = JsonContent.Create(payload, options: JsonOptions)
            };
            // Bearer header. The apiKey itself is never logged.
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            using var response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);

            if (response.IsSuccessStatusCode)
            {
                var envelope = await ReadEnvelopeAsync<AbacatePayCreateWebhookResponse>(response, cancellationToken)
                    .ConfigureAwait(false);
                if (envelope is null || !envelope.Success || envelope.Data is null
                    || string.IsNullOrWhiteSpace(envelope.Data.Id))
                {
                    _logger.LogWarning(
                        "AbacatePay webhook registration succeeded at HTTP level but envelope returned success=false (provider={ProviderCode}, status={StatusCode}).",
                        providerCode, (int)response.StatusCode);
                    return ProviderWebhookRegistrationOutcome.RegistrationFailed;
                }

                _logger.LogInformation(
                    "AbacatePay webhook registered successfully (provider={ProviderCode}, endpoint length={EndpointLength}, events={EventCount}).",
                    providerCode, callbackUrl.Length, events.Count);
                return ProviderWebhookRegistrationOutcome.Registered;
            }

            // Categorise HTTP failure with safe message.
            var statusCode = (int)response.StatusCode;
            var category = CategorizeStatus(statusCode);
            LogSafeWarning(providerCode, path, statusCode, category);
            DrainBodySafely(response);
            return ProviderWebhookRegistrationOutcome.RegistrationFailed;
        }
        catch (TaskCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException(
                "AbacatePay webhook registration was cancelled by the caller.",
                cancellationToken);
        }
        catch (TaskCanceledException ex)
        {
            _logger.LogWarning(
                "AbacatePay webhook registration timed out (provider={ProviderCode}).",
                ProviderCode.AbacatePay);
            throw new AbacatePayClientException(
                AbacatePayErrorCategory.Timeout,
                "AbacatePay webhook registration timed out.",
                statusCode: null,
                isTransient: true,
                innerException: ex);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex,
                "AbacatePay webhook registration network failure (provider={ProviderCode}).",
                ProviderCode.AbacatePay);
            throw new AbacatePayClientException(
                AbacatePayErrorCategory.Network,
                "AbacatePay webhook registration network failure.",
                statusCode: null,
                isTransient: true,
                innerException: ex);
        }
    }

    private static AbacatePayErrorCategory CategorizeStatus(int statusCode) => statusCode switch
    {
        400 => AbacatePayErrorCategory.BadRequest,
        401 or 403 => AbacatePayErrorCategory.Unauthorized,
        404 => AbacatePayErrorCategory.NotFound,
        429 => AbacatePayErrorCategory.RateLimited,
        >= 500 and < 600 => AbacatePayErrorCategory.ServerError,
        _ => AbacatePayErrorCategory.Unexpected
    };

    private void LogSafeWarning(ProviderCode providerCode, string path, int statusCode, AbacatePayErrorCategory category)
    {
        // Re-asserting Slice 2-C anti-regression: never log the payload, the
        // bearer header, the apiKey, or the webhookSecret.
        _logger.LogWarning(
            "AbacatePay returned HTTP {StatusCode} for {Path} (category {Category}, provider={ProviderCode}).",
            statusCode, path, category, providerCode);
    }

    private void DrainBodySafely(HttpResponseMessage response)
    {
        // Discard the body but read it to avoid socket exhaustion. We
        // deliberately do NOT inspect contents.
        try
        {
            _ = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        }
        catch
        {
            // best-effort drain
        }
    }

    private static async Task<AbacatePayEnvelope<T>?> ReadEnvelopeAsync<T>(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
        where T : class, new()
    {
        try
        {
            return await response.Content
                .ReadFromJsonAsync<AbacatePayEnvelope<T>>(JsonOptions, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (JsonException ex)
        {
            throw new AbacatePayClientException(
                AbacatePayErrorCategory.EnvelopeFailure,
                "AbacatePay returned a non-JSON or malformed envelope.",
                statusCode: (int)response.StatusCode,
                isTransient: false,
                innerException: ex);
        }
    }
}
