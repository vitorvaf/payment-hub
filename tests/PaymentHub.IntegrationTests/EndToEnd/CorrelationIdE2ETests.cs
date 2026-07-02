using FluentAssertions;
using PaymentHub.Application.Observability;
using PaymentHub.IntegrationTests.Infrastructure;

namespace PaymentHub.IntegrationTests.EndToEnd;

/// <summary>
/// End-to-end tests for the Slice 9-O1 commitment: drive the real
/// <c>PaymentHub.Api</c> through <see cref="Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory{TEntryPoint}"/>
/// against a real PostgreSQL (Testcontainers) + real DI graph + real
/// <see cref="PaymentHub.Api.Auth.CorrelationIdMiddleware"/>. No outbound HTTP
/// leaves the test process; no hosted services run inside
/// <see cref="PaymentHubApiFactory"/>.
/// </summary>
/// <remarks>
/// <para>
/// The middleware runs BEFORE <c>ApiKeyAuthenticationMiddleware</c> in
/// <c>Program.cs</c> (lines 131-133) so the <c>X-Correlation-Id</c> header
/// is added to the response even for 401/403/404 paths. This test exercises
/// <c>GET /health</c>, an anonymous path with no controller mapping
/// (returns 404) — the response still carries the header, which is the
/// property we are validating. Status code is NOT asserted as 200; the
/// middleware contract is about the header, not the route.
/// </para>
/// <para>
/// These tests close the only pending item from Slice 9-O1 (audit report
/// linhas 201-205, validation-matrix linhas 235-237).
/// </para>
/// </remarks>
[Collection(PostgresCollection.Name)]
[Trait("Category", "Integration")]
public class CorrelationIdE2ETests
{
    /// <summary>
    /// Path that the auth middleware marks anonymous
    /// (<c>ApiKeyAuthenticationMiddleware.IsAnonymousPath</c> line 126) and
    /// that has no controller mapping. The middleware still runs and adds
    /// <c>X-Correlation-Id</c> to the response, so the test exercises the
    /// real pipeline end-to-end.
    /// </summary>
    private const string HealthEndpoint = "/health";

    /// <summary>
    /// Deterministic valid correlation id used by the preserve test. The
    /// string matches <see cref="CorrelationIdGenerator.IsValid"/>: 20 chars,
    /// ASCII letters + digits, no dashes (still within the regex charset).
    /// </summary>
    private const string ValidInboundCorrelationId = "test-correlation-123456";

    /// <summary>
    /// Invalid candidate the slice 9-O1 decision #2 covers: the middleware
    /// MUST substitute silently (no 400, no echo) with a freshly generated
    /// GUID-N. The charset includes punctuation that fails the regex.
    /// </summary>
    private const string InvalidInboundCorrelationId = "!!@@##bad";

    private readonly PostgresFixture _postgres;

    public CorrelationIdE2ETests(PostgresFixture postgres)
    {
        _postgres = postgres;
    }

    // -----------------------------------------------------------------
    // P1.1 — Response carries a generated X-Correlation-Id when the
    //        client does not provide one.
    // -----------------------------------------------------------------

    [Fact]
    public async Task Response_ShouldContainGeneratedCorrelationId_WhenRequestDoesNotProvideOne()
    {
        var factory = await CreateFreshFactoryAsync();
        try
        {
            using var client = factory.CreateClient();
            using var request = new HttpRequestMessage(HttpMethod.Get, HealthEndpoint);

            // Act — no X-Correlation-Id header.
            var response = await client.SendAsync(request);

            // Assert — response carries a syntactically valid generated id.
            response.Headers.TryGetValues(CorrelationIdGenerator.HeaderName, out var values)
                .Should().BeTrue("the middleware must add X-Correlation-Id to every response");

            var collected = values!.ToArray();
            collected.Should().HaveCount(1,
                because: "the middleware must emit exactly one X-Correlation-Id header");
            var generated = collected[0];

            generated.Should().NotBeNullOrWhiteSpace();
            CorrelationIdGenerator.IsValid(generated).Should().BeTrue(
                because: "generated ids follow Guid.NewGuid().ToString(\"N\") and pass the regex");

            // Assert — the generated id is a 32-char GUID-N form.
            generated.Length.Should().Be(32);
            generated.Should().MatchRegex("^[0-9a-f]{32}$");
        }
        finally
        {
            await factory.DisposeAsync();
        }
    }

    // -----------------------------------------------------------------
    // P1.2 — Response preserves the inbound X-Correlation-Id verbatim
    //        when the client provides a syntactically valid one.
    // -----------------------------------------------------------------

    [Fact]
    public async Task Response_ShouldPreserveCorrelationId_WhenRequestProvidesValidHeader()
    {
        var factory = await CreateFreshFactoryAsync();
        try
        {
            using var client = factory.CreateClient();
            using var request = new HttpRequestMessage(HttpMethod.Get, HealthEndpoint);
            request.Headers.Add(CorrelationIdGenerator.HeaderName, ValidInboundCorrelationId);

            // Act
            var response = await client.SendAsync(request);

            // Assert — the inbound id is echoed back verbatim.
            response.Headers.TryGetValues(CorrelationIdGenerator.HeaderName, out var values)
                .Should().BeTrue();
            var collected = values!.ToArray();
            collected.Should().HaveCount(1);
            collected[0].Should().Be(ValidInboundCorrelationId,
                because: "a valid inbound id must be preserved byte-exact on the response");
        }
        finally
        {
            await factory.DisposeAsync();
        }
    }

    // -----------------------------------------------------------------
    // P1.3 — Response substitutes an invalid inbound id silently
    //        (slice 9-O1 decision #2: never return 400 for a bad id,
    //        never log the rejected value).
    // -----------------------------------------------------------------

    [Fact]
    public async Task Response_ShouldReplaceInvalidCorrelationId_WhenRequestProvidesInvalidHeader()
    {
        var factory = await CreateFreshFactoryAsync();
        try
        {
            using var client = factory.CreateClient();
            using var request = new HttpRequestMessage(HttpMethod.Get, HealthEndpoint);
            request.Headers.Add(CorrelationIdGenerator.HeaderName, InvalidInboundCorrelationId);

            // Act — invalid value present in the inbound header.
            var response = await client.SendAsync(request);

            // Assert — middleware substituted a valid id, did not echo
            // the rejected value, and did not raise 4xx.
            response.Headers.TryGetValues(CorrelationIdGenerator.HeaderName, out var values)
                .Should().BeTrue();
            var collected = values!.ToArray();
            collected.Should().HaveCount(1);
            var replaced = collected[0];

            replaced.Should().NotBe(InvalidInboundCorrelationId,
                because: "invalid inbound ids are substituted with a fresh GUID-N");
            CorrelationIdGenerator.IsValid(replaced).Should().BeTrue();
            replaced.Length.Should().Be(32);
        }
        finally
        {
            await factory.DisposeAsync();
        }
    }

    // -----------------------------------------------------------------
    // P1.4 — Multiple inbound X-Correlation-Id headers collapse to
    //        exactly one response header (the middleware picks the
    //        first non-empty value and overwrites the response header).
    // -----------------------------------------------------------------

    [Fact]
    public async Task Response_ShouldCollapseDuplicateInboundCorrelationIdHeaders_ToSingleResponseHeader()
    {
        var factory = await CreateFreshFactoryAsync();
        try
        {
            using var client = factory.CreateClient();
            using var request = new HttpRequestMessage(HttpMethod.Get, HealthEndpoint);
            // Send TWO valid headers. Middleware should pick the first one
            // and set it on the response exactly once.
            request.Headers.TryAddWithoutValidation(
                CorrelationIdGenerator.HeaderName, ValidInboundCorrelationId);
            request.Headers.TryAddWithoutValidation(
                CorrelationIdGenerator.HeaderName, "second-correlation-987654");

            var response = await client.SendAsync(request);

            response.Headers.TryGetValues(CorrelationIdGenerator.HeaderName, out var values)
                .Should().BeTrue();
            var collected = values!.ToArray();
            collected.Should().HaveCount(1);
            collected[0].Should().Be(ValidInboundCorrelationId);
        }
        finally
        {
            await factory.DisposeAsync();
        }
    }

    /// <summary>
    /// Mirrors the pattern used by Slice 3-IT / 7-IT / 7-M1 E2E suites:
    /// build a fresh <see cref="PaymentHubApiFactory"/> per test so the
    /// captured HTTP state stays isolated. Reset the database only when
    /// the test needs to assert persistence (none of the 4 tests above do).
    /// </summary>
    private async Task<PaymentHubApiFactory> CreateFreshFactoryAsync()
    {
        // Touch the fixture so xUnit resolves the Postgres container first;
        // we do not call ResetDatabaseAsync because no test in this class
        // asserts on persisted rows.
        await Task.Yield();
        return new PaymentHubApiFactory(_postgres);
    }
}
