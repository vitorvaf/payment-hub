using System.Text.Json;
using FluentAssertions;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using PaymentHub.Api.Controllers;
using PaymentHub.Application.Abstractions.Context;
using PaymentHub.Application.Tenants;
using PaymentHub.Application.Tenants.Dtos;
using PaymentHub.Domain.Enums;

namespace PaymentHub.UnitTests.Api;

/// <summary>
/// Controller tests for the Slice 2-C webhook management endpoints. The
/// focus is on tenant guard + scope-boundary mapping:
/// 401 (tenant context missing), 404 (provider account not in scope),
/// 409 (account inactive or non-AbacatePay) and 200 (success).
///
/// The controller is intentionally thin — domain logic lives in the
/// handlers; these tests only assert boundary mapping.
/// </summary>
public class ProviderAccountsWebhookControllerTests
{
    private readonly Mock<IValidator<ConfigureAbacatePayWebhookRequestDto>> _webhookValidator = new();
    private readonly Mock<IConfigureProviderAccountWebhookHandler> _configureHandler = new(MockBehavior.Strict);
    private readonly Mock<IGetProviderAccountWebhookHandler> _getHandler = new(MockBehavior.Strict);
    private readonly Mock<ITenantContext> _tenantContext = new();

    private const string ValidCallbackUrl = "https://merchant.example.com/webhook";

    [Fact]
    public async Task ConfigureWebhook_ShouldUseTenantAndApplicationFromAuthenticatedContext()
    {
        var tenantId = Guid.NewGuid();
        var applicationId = Guid.NewGuid();
        var providerAccountId = Guid.NewGuid();
        _tenantContext.SetupGet(c => c.TenantId).Returns(tenantId);
        _tenantContext.SetupGet(c => c.ApplicationId).Returns(applicationId);

        _webhookValidator.Setup(v => v.ValidateAsync(It.IsAny<ConfigureAbacatePayWebhookRequestDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult());

        var expectedResponse = NewWebhookResponse(providerAccountId);
        _configureHandler.Setup(h => h.HandleAsync(
                tenantId, applicationId, providerAccountId, It.IsAny<ConfigureAbacatePayWebhookRequestDto>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ConfigureWebhookOutcome.Success(expectedResponse));

        var controller = NewController();

        var result = await controller.ConfigureWebhook(providerAccountId, ValidRequest(), CancellationToken.None);

        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        ok.StatusCode.Should().Be(200);
        _configureHandler.Verify(h => h.HandleAsync(
            tenantId, applicationId, providerAccountId,
            It.IsAny<ConfigureAbacatePayWebhookRequestDto>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ConfigureWebhook_ShouldReturnBadRequest_WhenValidationFails()
    {
        var failures = new List<ValidationFailure>
        {
            new("CallbackUrl", "CallbackUrl must be a public HTTPS URL.")
        };
        _webhookValidator.Setup(v => v.ValidateAsync(It.IsAny<ConfigureAbacatePayWebhookRequestDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult(failures));

        var controller = NewController();

        var result = await controller.ConfigureWebhook(Guid.NewGuid(), ValidRequest(), CancellationToken.None);

        result.Result.Should().BeOfType<BadRequestObjectResult>();
        _configureHandler.Verify(
            h => h.HandleAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(),
                It.IsAny<ConfigureAbacatePayWebhookRequestDto>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _tenantContext.VerifyGet(c => c.TenantId, Times.Never);
    }

    [Fact]
    public async Task ConfigureWebhook_ShouldReturnUnauthorized_WhenTenantContextMissing()
    {
        _webhookValidator.Setup(v => v.ValidateAsync(It.IsAny<ConfigureAbacatePayWebhookRequestDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult());
        _tenantContext.SetupGet(c => c.TenantId).Throws(new InvalidOperationException("tenant missing"));

        var controller = NewController();

        var result = await controller.ConfigureWebhook(Guid.NewGuid(), ValidRequest(), CancellationToken.None);

        result.Result.Should().BeOfType<UnauthorizedObjectResult>();
        _configureHandler.Verify(
            h => h.HandleAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(),
                It.IsAny<ConfigureAbacatePayWebhookRequestDto>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ConfigureWebhook_ShouldReturnNotFound_WhenHandlerReturnsNotFound()
    {
        var tenantId = Guid.NewGuid();
        var applicationId = Guid.NewGuid();
        var providerAccountId = Guid.NewGuid();
        _tenantContext.SetupGet(c => c.TenantId).Returns(tenantId);
        _tenantContext.SetupGet(c => c.ApplicationId).Returns(applicationId);
        _webhookValidator.Setup(v => v.ValidateAsync(It.IsAny<ConfigureAbacatePayWebhookRequestDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult());
        _configureHandler.Setup(h => h.HandleAsync(
                tenantId, applicationId, providerAccountId, It.IsAny<ConfigureAbacatePayWebhookRequestDto>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ConfigureWebhookOutcome.NotFound());

        var controller = NewController();
        var result = await controller.ConfigureWebhook(providerAccountId, ValidRequest(), CancellationToken.None);

        var notFound = result.Result.Should().BeOfType<NotFoundObjectResult>().Subject;
        notFound.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task ConfigureWebhook_ShouldReturnConflict_WhenHandlerReturnsInactive()
    {
        var tenantId = Guid.NewGuid();
        var applicationId = Guid.NewGuid();
        var providerAccountId = Guid.NewGuid();
        _tenantContext.SetupGet(c => c.TenantId).Returns(tenantId);
        _tenantContext.SetupGet(c => c.ApplicationId).Returns(applicationId);
        _webhookValidator.Setup(v => v.ValidateAsync(It.IsAny<ConfigureAbacatePayWebhookRequestDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult());
        _configureHandler.Setup(h => h.HandleAsync(
                tenantId, applicationId, providerAccountId, It.IsAny<ConfigureAbacatePayWebhookRequestDto>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ConfigureWebhookOutcome.Inactive());

        var controller = NewController();
        var result = await controller.ConfigureWebhook(providerAccountId, ValidRequest(), CancellationToken.None);

        var conflict = result.Result.Should().BeOfType<ConflictObjectResult>().Subject;
        conflict.StatusCode.Should().Be(409);
    }

    [Fact]
    public async Task ConfigureWebhook_ShouldReturnConflict_WhenHandlerReturnsUnsupportedProvider()
    {
        var tenantId = Guid.NewGuid();
        var applicationId = Guid.NewGuid();
        var providerAccountId = Guid.NewGuid();
        _tenantContext.SetupGet(c => c.TenantId).Returns(tenantId);
        _tenantContext.SetupGet(c => c.ApplicationId).Returns(applicationId);
        _webhookValidator.Setup(v => v.ValidateAsync(It.IsAny<ConfigureAbacatePayWebhookRequestDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult());
        _configureHandler.Setup(h => h.HandleAsync(
                tenantId, applicationId, providerAccountId, It.IsAny<ConfigureAbacatePayWebhookRequestDto>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ConfigureWebhookOutcome.UnsupportedProvider());

        var controller = NewController();
        var result = await controller.ConfigureWebhook(providerAccountId, ValidRequest(), CancellationToken.None);

        var conflict = result.Result.Should().BeOfType<ConflictObjectResult>().Subject;
        conflict.StatusCode.Should().Be(409);
    }

    [Fact]
    public async Task GetWebhook_ShouldReturnOk_WhenHandlerReturnsSuccess()
    {
        var tenantId = Guid.NewGuid();
        var applicationId = Guid.NewGuid();
        var providerAccountId = Guid.NewGuid();
        _tenantContext.SetupGet(c => c.TenantId).Returns(tenantId);
        _tenantContext.SetupGet(c => c.ApplicationId).Returns(applicationId);
        var expected = NewWebhookResponse(providerAccountId);
        _getHandler.Setup(h => h.HandleAsync(
                tenantId, applicationId, providerAccountId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetWebhookOutcome.Success(expected));

        var controller = NewController();
        var result = await controller.GetWebhook(providerAccountId, CancellationToken.None);

        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        ok.StatusCode.Should().Be(200);
    }

    [Fact]
    public async Task GetWebhook_ShouldReturnNotFound_WhenHandlerReturnsNotFound()
    {
        var tenantId = Guid.NewGuid();
        var applicationId = Guid.NewGuid();
        var providerAccountId = Guid.NewGuid();
        _tenantContext.SetupGet(c => c.TenantId).Returns(tenantId);
        _tenantContext.SetupGet(c => c.ApplicationId).Returns(applicationId);
        _getHandler.Setup(h => h.HandleAsync(
                tenantId, applicationId, providerAccountId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetWebhookOutcome.NotFound());

        var controller = NewController();
        var result = await controller.GetWebhook(providerAccountId, CancellationToken.None);

        result.Result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task GetWebhook_ShouldReturnConflict_WhenHandlerReturnsInactive()
    {
        var tenantId = Guid.NewGuid();
        var applicationId = Guid.NewGuid();
        var providerAccountId = Guid.NewGuid();
        _tenantContext.SetupGet(c => c.TenantId).Returns(tenantId);
        _tenantContext.SetupGet(c => c.ApplicationId).Returns(applicationId);
        _getHandler.Setup(h => h.HandleAsync(
                tenantId, applicationId, providerAccountId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetWebhookOutcome.Inactive());

        var controller = NewController();
        var result = await controller.GetWebhook(providerAccountId, CancellationToken.None);

        result.Result.Should().BeOfType<ConflictObjectResult>();
    }

    [Fact]
    public async Task GetWebhook_ShouldReturnConflict_WhenHandlerReturnsUnsupportedProvider()
    {
        var tenantId = Guid.NewGuid();
        var applicationId = Guid.NewGuid();
        var providerAccountId = Guid.NewGuid();
        _tenantContext.SetupGet(c => c.TenantId).Returns(tenantId);
        _tenantContext.SetupGet(c => c.ApplicationId).Returns(applicationId);
        _getHandler.Setup(h => h.HandleAsync(
                tenantId, applicationId, providerAccountId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetWebhookOutcome.UnsupportedProvider());

        var controller = NewController();
        var result = await controller.GetWebhook(providerAccountId, CancellationToken.None);

        result.Result.Should().BeOfType<ConflictObjectResult>();
    }

    [Fact]
    public async Task GetWebhook_ShouldReturnUnauthorized_WhenTenantContextMissing()
    {
        _tenantContext.SetupGet(c => c.TenantId).Throws(new InvalidOperationException("tenant missing"));

        var controller = NewController();
        var result = await controller.GetWebhook(Guid.NewGuid(), CancellationToken.None);

        result.Result.Should().BeOfType<UnauthorizedObjectResult>();
        _getHandler.Verify(
            h => h.HandleAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ConfigureWebhook_ShouldIgnoreExtraTenantIdFieldsInBody()
    {
        var tenantId = Guid.NewGuid();
        var applicationId = Guid.NewGuid();
        var providerAccountId = Guid.NewGuid();
        _tenantContext.SetupGet(c => c.TenantId).Returns(tenantId);
        _tenantContext.SetupGet(c => c.ApplicationId).Returns(applicationId);
        _webhookValidator.Setup(v => v.ValidateAsync(It.IsAny<ConfigureAbacatePayWebhookRequestDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult());
        var expectedResponse = NewWebhookResponse(providerAccountId);
        _configureHandler.Setup(h => h.HandleAsync(
                tenantId, applicationId, providerAccountId, It.IsAny<ConfigureAbacatePayWebhookRequestDto>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ConfigureWebhookOutcome.Success(expectedResponse));

        // The DTO does not declare tenantId/applicationId. Send a JSON
        // payload that nonetheless has those fields so we confirm they
        // are ignored by the controller (the same Slice 6-B invariant).
        var raw = System.Text.Json.JsonSerializer.SerializeToElement(new
        {
            callbackUrl = "https://merchant.example.com/webhook",
            events = new[] { "transparent.completed" },
            webhookSecret = (string?)null,
            registerRemotely = false,
            tenantId = Guid.NewGuid(),
            applicationId = Guid.NewGuid()
        });
        var deserialized = raw.Deserialize<ConfigureAbacatePayWebhookRequestDto>(new System.Text.Json.JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        })!;

        var controller = NewController();
        await controller.ConfigureWebhook(providerAccountId, deserialized, CancellationToken.None);

        _configureHandler.Verify(
            h => h.HandleAsync(tenantId, applicationId, providerAccountId,
                It.IsAny<ConfigureAbacatePayWebhookRequestDto>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // -----------------------------------------------------------------
    // Slice 2-C.1: when the feature flag is off, the handler returns
    // Success with RemoteRegistrationDeferred. The controller must still
    // map that to 200 OK and surface the deferred status in the body so
    // the operator can see what happened. This is the Slice 2-C.1 contract
    // change versus the no-op default of Slice 2-C.
    // -----------------------------------------------------------------
    [Fact]
    public async Task ConfigureWebhook_ShouldReturnOkWithRemoteRegistrationDeferred_WhenFeatureFlagIsOffAndRegisterRemotelyTrue()
    {
        var tenantId = Guid.NewGuid();
        var applicationId = Guid.NewGuid();
        var providerAccountId = Guid.NewGuid();
        _tenantContext.SetupGet(c => c.TenantId).Returns(tenantId);
        _tenantContext.SetupGet(c => c.ApplicationId).Returns(applicationId);
        _webhookValidator.Setup(v => v.ValidateAsync(It.IsAny<ConfigureAbacatePayWebhookRequestDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult());

        var deferredResponse = NewWebhookResponse(
            providerAccountId,
            hasWebhookSecret: true,
            remoteRegistrationStatus: "RemoteRegistrationDeferred");
        _configureHandler.Setup(h => h.HandleAsync(
                tenantId, applicationId, providerAccountId,
                It.Is<ConfigureAbacatePayWebhookRequestDto>(r => r.RegisterRemotely),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ConfigureWebhookOutcome.Success(deferredResponse));

        var controller = NewController();
        var request = ValidRequest() with { WebhookSecret = "valid-webhook-secret-32-chars-long!", RegisterRemotely = true };

        var result = await controller.ConfigureWebhook(providerAccountId, request, CancellationToken.None);

        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        ok.StatusCode.Should().Be(200);
        var responseBody = ok.Value.Should().BeOfType<ProviderAccountWebhookResponseDto>().Subject;
        responseBody.RemoteRegistrationStatus.Should().Be("RemoteRegistrationDeferred");
        responseBody.HasWebhookSecret.Should().BeTrue(
            because: "the local credentials blob carries the new webhookSecret even when remote registration is deferred");
    }

    // -----------------------------------------------------------------
    // Slice 2-C.1: when the feature flag is on and the upstream
    // returns Registered, the handler returns Success with
    // RemoteRegistrationStatus="Registered". The controller must surface
    // that string verbatim in the response body so the operator can
    // distinguish from the deferred / failed outcomes.
    // -----------------------------------------------------------------
    [Fact]
    public async Task ConfigureWebhook_ShouldReturnOkWithRegisteredStatus_WhenAllGatesPass()
    {
        var tenantId = Guid.NewGuid();
        var applicationId = Guid.NewGuid();
        var providerAccountId = Guid.NewGuid();
        _tenantContext.SetupGet(c => c.TenantId).Returns(tenantId);
        _tenantContext.SetupGet(c => c.ApplicationId).Returns(applicationId);
        _webhookValidator.Setup(v => v.ValidateAsync(It.IsAny<ConfigureAbacatePayWebhookRequestDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult());

        var registeredResponse = NewWebhookResponse(
            providerAccountId,
            hasWebhookSecret: true,
            remoteRegistrationStatus: "Registered");
        _configureHandler.Setup(h => h.HandleAsync(
                tenantId, applicationId, providerAccountId,
                It.IsAny<ConfigureAbacatePayWebhookRequestDto>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ConfigureWebhookOutcome.Success(registeredResponse));

        var controller = NewController();
        var request = ValidRequest() with { WebhookSecret = "valid-webhook-secret-32-chars-long!", RegisterRemotely = true };

        var result = await controller.ConfigureWebhook(providerAccountId, request, CancellationToken.None);

        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var responseBody = ok.Value.Should().BeOfType<ProviderAccountWebhookResponseDto>().Subject;
        responseBody.RemoteRegistrationStatus.Should().Be("Registered");
    }

    // ---- helpers ----

    private ProviderAccountsController NewController()
        => new(
            Mock.Of<IRegisterProviderAccountHandler>(),
            Mock.Of<IValidator<RegisterProviderAccountRequestDto>>(),
            _tenantContext.Object,
            _webhookValidator.Object,
            _configureHandler.Object,
            _getHandler.Object)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };

    private static ConfigureAbacatePayWebhookRequestDto ValidRequest() => new(
        CallbackUrl: ValidCallbackUrl,
        Events: new[] { "transparent.completed", "transparent.refunded" },
        WebhookSecret: null,
        RegisterRemotely: false);

    private static ProviderAccountWebhookResponseDto NewWebhookResponse(Guid providerAccountId) =>
        NewWebhookResponse(providerAccountId, hasWebhookSecret: false, remoteRegistrationStatus: "NotRegistered");

    private static ProviderAccountWebhookResponseDto NewWebhookResponse(
        Guid providerAccountId,
        bool hasWebhookSecret,
        string remoteRegistrationStatus) =>
        new(
            ProviderAccountId: providerAccountId,
            ProviderCode: ProviderCode.AbacatePay,
            Environment: ProviderEnvironment.Sandbox,
            CallbackUrl: ValidCallbackUrl,
            Events: new[] { "transparent.completed", "transparent.refunded" },
            HasWebhookSecret: hasWebhookSecret,
            RemoteRegistrationStatus: remoteRegistrationStatus,
            ConfiguredAt: DateTime.UtcNow,
            UpdatedAt: DateTime.UtcNow);
}
