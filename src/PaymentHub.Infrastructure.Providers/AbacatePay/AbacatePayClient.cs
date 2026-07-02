using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PaymentHub.Application.Observability;
using PaymentHub.Infrastructure.Providers.AbacatePay.Models;

namespace PaymentHub.Infrastructure.Providers.AbacatePay;

/// <summary>
/// HTTP client for AbacatePay. Registered as Singleton: it depends only on
/// <see cref="IHttpClientFactory"/>, <see cref="IOptionsMonitor{T}"/> and
/// <see cref="ILogger{T}"/> — all singleton-safe. The adapter depends on
/// <see cref="IAbacatePayClient"/>, so the adapter can also remain Singleton
/// without captive-dependency risk.
/// </summary>
public sealed class AbacatePayClient : IAbacatePayClient
{
    public const string HttpClientName = "abacatepay";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptionsMonitor<AbacatePayOptions> _options;
    private readonly ILogger<AbacatePayClient> _logger;

    public AbacatePayClient(
        IHttpClientFactory httpClientFactory,
        IOptionsMonitor<AbacatePayOptions> options,
        ILogger<AbacatePayClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options;
        _logger = logger;
    }

    public Task<AbacatePayCreateTransparentPixResponse> CreateTransparentPixAsync(
        AbacatePayCreateTransparentPixRequest request,
        string apiKey,
        CancellationToken cancellationToken)
    {
        // NOTE: The AllowDevModeSimulation flag is checked only in
        // SimulateTransparentPixPaymentAsync — production-style create
        // calls are always allowed because that is the primary path the
        // sandbox adapter is here to cover.
        return SendAsync<AbacatePayCreateTransparentPixRequest, AbacatePayCreateTransparentPixResponse>(
            HttpMethod.Post,
            path: "transparents/create",
            apiKey: apiKey,
            body: request,
            cancellationToken: cancellationToken);
    }

    public Task<AbacatePayCheckTransparentPixResponse> CheckTransparentPixAsync(
        string providerPaymentId,
        string apiKey,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(providerPaymentId))
            throw new AbacatePayClientException(
                AbacatePayErrorCategory.BadRequest,
                "providerPaymentId is required.");

        var path = $"transparents/check?id={Uri.EscapeDataString(providerPaymentId)}";
        return SendAsync<object, AbacatePayCheckTransparentPixResponse>(
            HttpMethod.Get,
            path: path,
            apiKey: apiKey,
            body: null,
            cancellationToken: cancellationToken);
    }

    public Task<AbacatePaySimulatePaymentResponse> SimulateTransparentPixPaymentAsync(
        string providerPaymentId,
        string apiKey,
        CancellationToken cancellationToken)
    {
        EnsureNotSimulationEndpointDisabled();

        if (string.IsNullOrWhiteSpace(providerPaymentId))
            throw new AbacatePayClientException(
                AbacatePayErrorCategory.BadRequest,
                "providerPaymentId is required.");

        var path = $"transparents/simulate-payment?id={Uri.EscapeDataString(providerPaymentId)}";
        return SendAsync<object, AbacatePaySimulatePaymentResponse>(
            HttpMethod.Post,
            path: path,
            apiKey: apiKey,
            body: null,
            cancellationToken: cancellationToken);
    }

    private void EnsureNotSimulationEndpointDisabled()
    {
        if (!_options.CurrentValue.AllowDevModeSimulation)
        {
            throw new AbacatePayClientException(
                AbacatePayErrorCategory.SimulationDisabled,
                "AbacatePay simulate-payment is disabled. Enable Providers:AbacatePay:AllowDevModeSimulation in development only.");
        }
    }

    private async Task<TResponse> SendAsync<TRequest, TResponse>(
        HttpMethod method,
        string path,
        string apiKey,
        TRequest? body,
        CancellationToken cancellationToken)
        where TResponse : class, new()
    {
        // Slice 9-O2: provider call duration metric. Recorded via finally
        // so success AND failure paths are captured. Operation name
        // derived from path (transparent_pix.create / check / simulate).
        var startedAt = Stopwatch.GetTimestamp();
        var operation = MapPathToOperation(path);
        // Slice 9-O2: increment call counter at the top of every attempt.
        PaymentHubMetrics.ProviderCallTotal.Record(1,
            PaymentHubMetrics.Tag(
                PaymentHubMetrics.TagKeys.Provider, "abacatepay",
                PaymentHubMetrics.TagKeys.Operation, operation));

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            PaymentHubMetrics.ProviderCallFailedTotal.Record(1,
                PaymentHubMetrics.Tag(
                    PaymentHubMetrics.TagKeys.Provider, "abacatepay",
                    PaymentHubMetrics.TagKeys.Operation, operation,
                    PaymentHubMetrics.TagKeys.ErrorCategory, "Unauthorized"));
            PaymentHubMetrics.ProviderCallDurationMs.Record(
                Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds,
                PaymentHubMetrics.Tag(
                    PaymentHubMetrics.TagKeys.Provider, "abacatepay",
                    PaymentHubMetrics.TagKeys.Operation, operation));
            throw new AbacatePayClientException(
                AbacatePayErrorCategory.Unauthorized,
                "AbacatePay API key is missing.");
        }

        var http = _httpClientFactory.CreateClient(HttpClientName);
        using var request = new HttpRequestMessage(method, path);

        if (body is not null)
        {
            request.Content = JsonContent.Create(body, options: JsonOptions);
        }

        // Bearer header. Never log this.
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        try
        {
            HttpResponseMessage response;
            try
            {
                response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            }
            catch (TaskCanceledException ex) when (cancellationToken.IsCancellationRequested)
            {
                // Caller asked for cancellation — not a transient provider timeout.
                throw new OperationCanceledException(ex.Message, ex, cancellationToken);
            }
            catch (TaskCanceledException ex)
            {
                // HttpClient timeout — surface as AbacatePayClientException(Timeout).
                _logger.LogWarning("AbacatePay request timed out for path {Path}.", path);
                PaymentHubMetrics.ProviderCallFailedTotal.Record(1,
                    PaymentHubMetrics.Tag(
                        PaymentHubMetrics.TagKeys.Provider, "abacatepay",
                        PaymentHubMetrics.TagKeys.Operation, operation,
                        PaymentHubMetrics.TagKeys.ErrorCategory, "Timeout"));
                throw new AbacatePayClientException(
                    AbacatePayErrorCategory.Timeout,
                    "AbacatePay request timed out.",
                    statusCode: null,
                    isTransient: true,
                    innerException: ex);
            }
            catch (HttpRequestException ex)
            {
                _logger.LogWarning(ex, "AbacatePay network failure for path {Path}.", path);
                PaymentHubMetrics.ProviderCallFailedTotal.Record(1,
                    PaymentHubMetrics.Tag(
                        PaymentHubMetrics.TagKeys.Provider, "abacatepay",
                        PaymentHubMetrics.TagKeys.Operation, operation,
                        PaymentHubMetrics.TagKeys.ErrorCategory, "Network"));
                throw new AbacatePayClientException(
                    AbacatePayErrorCategory.Network,
                    "AbacatePay network failure.",
                    statusCode: null,
                    isTransient: true,
                    innerException: ex);
            }

            var statusCode = (int)response.StatusCode;

            if (!response.IsSuccessStatusCode)
            {
                var category = CategorizeStatus(statusCode);
                _logger.LogWarning(
                    "AbacatePay returned HTTP {StatusCode} for path {Path} (category {Category}).",
                    statusCode, path, category);

                // Slice 9-O2: record failed-call metric before draining
                // the body. The category is whitelisted (ErrorCategory tag).
                PaymentHubMetrics.ProviderCallFailedTotal.Record(1,
                    PaymentHubMetrics.Tag(
                        PaymentHubMetrics.TagKeys.Provider, "abacatepay",
                        PaymentHubMetrics.TagKeys.Operation, operation,
                        PaymentHubMetrics.TagKeys.ErrorCategory, SafeLog.Category(category)));

                // Drain body to avoid socket exhaustion but never surface its contents.
                try { _ = await response.Content.ReadAsStringAsync().ConfigureAwait(false); }
                catch { /* best-effort drain */ }

                throw new AbacatePayClientException(
                    category,
                    $"AbacatePay HTTP {statusCode}.",
                    statusCode: statusCode,
                    isTransient: category is AbacatePayErrorCategory.RateLimited or AbacatePayErrorCategory.ServerError);
            }

            AbacatePayEnvelope<TResponse>? envelope;
            try
            {
                envelope = await response.Content
                    .ReadFromJsonAsync<AbacatePayEnvelope<TResponse>>(JsonOptions, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (JsonException ex)
            {
                PaymentHubMetrics.ProviderCallFailedTotal.Record(1,
                    PaymentHubMetrics.Tag(
                        PaymentHubMetrics.TagKeys.Provider, "abacatepay",
                        PaymentHubMetrics.TagKeys.Operation, operation,
                        PaymentHubMetrics.TagKeys.ErrorCategory, "EnvelopeFailure"));
                throw new AbacatePayClientException(
                    AbacatePayErrorCategory.EnvelopeFailure,
                    "AbacatePay returned a non-JSON or malformed envelope.",
                    statusCode: statusCode,
                    isTransient: false,
                    innerException: ex);
            }

            if (envelope is null || !envelope.Success || envelope.Data is null)
            {
                PaymentHubMetrics.ProviderCallFailedTotal.Record(1,
                    PaymentHubMetrics.Tag(
                        PaymentHubMetrics.TagKeys.Provider, "abacatepay",
                        PaymentHubMetrics.TagKeys.Operation, operation,
                        PaymentHubMetrics.TagKeys.ErrorCategory, "EnvelopeFailure"));
                throw new AbacatePayClientException(
                    AbacatePayErrorCategory.EnvelopeFailure,
                    $"AbacatePay envelope reported failure (HTTP {statusCode}).",
                    statusCode: statusCode);
            }

            return envelope.Data;
        }
        finally
        {
            // Slice 9-O2: record call duration regardless of outcome.
            // The duration tag is recorded on every call so dashboards can
            // compute p50/p95/p99 latency without splitting by status.
            PaymentHubMetrics.ProviderCallDurationMs.Record(
                Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds,
                PaymentHubMetrics.Tag(
                    PaymentHubMetrics.TagKeys.Provider, "abacatepay",
                    PaymentHubMetrics.TagKeys.Operation, operation));
        }
    }

    private static string MapPathToOperation(string path)
    {
        // Map known AbacatePay v2 paths to a stable operation name for
        // metrics. Unknown paths get the raw path so dashboards can still
        // group by provider + path cardinality.
        if (path.StartsWith("transparents/create", StringComparison.OrdinalIgnoreCase))
            return "create_transparent_pix";
        if (path.StartsWith("transparents/check", StringComparison.OrdinalIgnoreCase))
            return "check_transparent_pix";
        if (path.StartsWith("transparents/simulate-payment", StringComparison.OrdinalIgnoreCase))
            return "simulate_transparent_pix";
        return path;
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
}