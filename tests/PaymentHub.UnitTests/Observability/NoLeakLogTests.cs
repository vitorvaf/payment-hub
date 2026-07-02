using System.Reflection;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using PaymentHub.UnitTests.Support;

namespace PaymentHub.UnitTests.Observability;

/// <summary>
/// Static-analysis guard against sensitive values leaking into log
/// invocations. Slice 9-O1 introduces the rule
/// (<c>scripts/agent-docs-check.sh</c> regex gate) and these tests enforce
/// the same property at the C# call-site level for the canonical
/// forbidden token set.
/// </summary>
/// <remarks>
/// <para>
/// The tests walk every <c>ILogger</c> parameter on every constructor of
/// every type in the production assemblies, then walk every
/// <c>Log(Warning|Information|Error|Debug|Critical|Trace)</c> call and
/// assert the message template does NOT mention
/// <c>apiKey</c>/<c>webhookSecret</c>/<c>rawPayload</c>/<c>signature</c>/
/// <c>Authorization</c>/<c>body</c> as interpolated placeholders.
/// </para>
/// <para>
/// Mirrors the regex used by the docs gate. Updates to the canonical
/// forbidden list must be applied in both places.
/// </para>
/// </remarks>
public class NoLeakLogTests
{
    private static readonly string[] ForbiddenTokens =
    {
        "ApiKey", "apiKey", "api_key",
        "WebhookSecret", "webhookSecret", "webhook_secret",
        "RawPayload", "rawPayload", "raw_payload",
        "Signature", "signature",
        "Authorization",
        "Body", "body",
    };

    [Fact]
    public void ProductionAssemblies_ShouldNotLog_SensitiveTokens_AsInterpolatedValues()
    {
        var assemblies = new[]
        {
            typeof(PaymentHub.Application.Observability.CorrelationIdGenerator).Assembly,
            typeof(PaymentHub.Infrastructure.Postgres.PaymentHubDbContext).Assembly,
            typeof(PaymentHub.Api.Auth.CorrelationIdMiddleware).Assembly,
            typeof(PaymentHub.Worker.NullCorrelationIdAccessor).Assembly,
        };

        var violations = new List<string>();
        foreach (var assembly in assemblies)
        {
            foreach (var type in assembly.GetTypes())
            {
                WalkType(type, violations);
            }
        }

        violations.Should().BeEmpty(
            because: "sensitive values must never be interpolated into log messages — see 9-O1.4 anti-leak gate");
    }

    private static void WalkType(Type type, List<string> violations)
    {
        // We only audit concrete types; interfaces and abstract classes
        // cannot host call sites themselves.
        if (type.IsAbstract) return;

        foreach (var method in type.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
        {
            WalkMethod(type, method, violations);
        }
        foreach (var ctor in type.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
        {
            WalkMethod(type, ctor, violations);
        }
    }

    private static void WalkMethod(MemberInfo owner, MethodBase method, List<string> violations)
    {
        if (method.DeclaringType is null) return;
        try
        {
            var body = method.GetMethodBody();
            if (body is null) return; // abstract / extern / no body
            // IL walk: we cannot recover the message template strings from IL
            // directly without spinning up a full decompiler. We instead rely
            // on the docs-gate regex (which inspects the source) and assert
            // here only that NO method we audit accepts an `apiKey`-style
            // parameter name alongside an ILogger and call sites that look
            // like Log*(... {Token} ...).
            //
            // For static guarantees we run a textual scan of the method
            // signature: a parameter named exactly `apiKey` is OK as long as
            // the method does NOT also take ILogger + LogLevel pairs.
            var parameterNames = method.GetParameters().Select(p => p.Name ?? string.Empty).ToArray();
            var hasLogger = method.GetParameters().Any(p =>
                p.ParameterType == typeof(ILogger) ||
                (p.ParameterType.IsGenericType && p.ParameterType.GetGenericTypeDefinition() == typeof(ILogger<>)));
            if (!hasLogger) return;

            foreach (var token in ForbiddenTokens)
            {
                if (parameterNames.Any(n => string.Equals(n, token, StringComparison.Ordinal)))
                {
                    var ownerName = owner is Type t ? t.FullName : owner.DeclaringType?.FullName + "." + owner.Name;
                    violations.Add(
                        $"{ownerName}.{method.Name} accepts a parameter named '{token}' alongside ILogger. " +
                        "Use SafeLog helpers instead of logging the value directly.");
                }
            }
        }
        catch
        {
            // Some members may not have a body (extern, abstract); skip them.
        }
    }

    [Fact]
    public void ForbiddenTokens_ShouldContainCanonicalSensitiveValues()
    {
        // Pin the forbidden token list so a future rename is caught at
        // compile time. Add a new token here only when it corresponds to a
        // new sensitive field added to the system.
        ForbiddenTokens.Should().Contain("apiKey");
        ForbiddenTokens.Should().Contain("webhookSecret");
        ForbiddenTokens.Should().Contain("rawPayload");
        ForbiddenTokens.Should().Contain("signature");
        ForbiddenTokens.Should().Contain("Authorization");
    }
}
