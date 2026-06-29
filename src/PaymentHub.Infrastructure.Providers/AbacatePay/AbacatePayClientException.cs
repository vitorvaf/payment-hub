namespace PaymentHub.Infrastructure.Providers.AbacatePay;

/// <summary>
/// Thrown by <see cref="AbacatePayClient"/> when an AbacatePay API call fails.
/// The message is intentionally generic and MUST NOT contain:
/// <list type="bullet">
///   <item>The raw API key.</item>
///   <item>The Authorization header.</item>
///   <item>The full response body (especially <c>brCodeBase64</c>).</item>
///   <item>The full request body.</item>
/// </list>
/// Callers should rely on <see cref="Category"/>, <see cref="StatusCode"/>, and
/// <see cref="IsTransient"/> for routing and retry decisions.
/// </summary>
public sealed class AbacatePayClientException : Exception
{
    public AbacatePayErrorCategory Category { get; }
    public int? StatusCode { get; }
    public bool IsTransient { get; }

    public AbacatePayClientException(
        AbacatePayErrorCategory category,
        string message,
        int? statusCode = null,
        bool? isTransient = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        Category = category;
        StatusCode = statusCode;
        IsTransient = isTransient ?? DefaultTransient(category);
    }

    private static bool DefaultTransient(AbacatePayErrorCategory category) => category switch
    {
        AbacatePayErrorCategory.RateLimited => true,
        AbacatePayErrorCategory.ServerError => true,
        AbacatePayErrorCategory.Network => true,
        AbacatePayErrorCategory.Timeout => true,
        _ => false
    };
}