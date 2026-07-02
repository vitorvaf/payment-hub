using PaymentHub.Application.Observability;

namespace PaymentHub.UnitTests.Support;

/// <summary>
/// Helpers for tests that exercise the CorrelationId contract. The slice
/// 9-O1 generator produces a GUID-N (32 hex chars, no dashes). The validator
/// accepts any value matching the canonical regex
/// <c>^[A-Za-z0-9\-]{8,128}$</c>.
/// </summary>
public static class CorrelationIdTestHelper
{
    /// <summary>
    /// A valid CorrelationId in the canonical 32-char GUID-N shape. Tests
    /// that need a stable seed reuse this value across cases.
    /// </summary>
    public const string ValidId = "abcd1234efgh5678ijkl9012mnop3456";

    /// <summary>
    /// A second valid CorrelationId with non-default characters. Useful for
    /// tests that assert round-tripping preserves the exact value.
    /// </summary>
    public const string ValidIdAlternate = "WXYZ-0000-aaaa-1111-bbbb-2222";

    /// <summary>
    /// Returns a freshly minted CorrelationId. Each call produces a unique
    /// value; tests that need determinism should pin
    /// <see cref="ValidId"/> instead.
    /// </summary>
    public static string New() => CorrelationIdGenerator.New();

    /// <summary>
    /// Asserts that <paramref name="candidate"/> satisfies the slice 9-O1
    /// validation window. Convenience for tests that pre-populate the
    /// middleware with a hand-rolled value.
    /// </summary>
    public static bool IsValid(string? candidate) => CorrelationIdGenerator.IsValid(candidate);

    /// <summary>
    /// Returns a string guaranteed to FAIL the validator. Use it to test the
    /// "invalid header substituted silently" path of the middleware.
    /// </summary>
    public static string InvalidId() => "!!@@##";
}
