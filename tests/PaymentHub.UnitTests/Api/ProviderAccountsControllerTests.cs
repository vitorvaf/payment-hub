using System.Text.Json;
using System.Text.Json.Serialization;
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

public class ProviderAccountsControllerTests
{
    private readonly Mock<IRegisterProviderAccountHandler> _handler = new(MockBehavior.Strict);
    private readonly Mock<IValidator<RegisterProviderAccountRequestDto>> _validator = new();
    private readonly Mock<ITenantContext> _tenantContext = new();

    private static readonly RegisterProviderAccountRequestDto ValidBody = new(
        ProviderCode.Fake,
        ProviderEnvironment.Sandbox,
        "primary",
        "raw-api-key",
        null,
        false);

    [Fact]
    public async Task Register_ShouldUseTenantAndApplicationFromAuthenticatedContext()
    {
        var tenantId = Guid.NewGuid();
        var applicationId = Guid.NewGuid();
        _tenantContext.SetupGet(c => c.TenantId).Returns(tenantId);
        _tenantContext.SetupGet(c => c.ApplicationId).Returns(applicationId);
        _validator.Setup(v => v.ValidateAsync(It.IsAny<RegisterProviderAccountRequestDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult());
        _handler.Setup(h => h.HandleAsync(tenantId, applicationId, It.IsAny<RegisterProviderAccountRequestDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(NewResponse(tenantId, applicationId));

        var controller = new ProviderAccountsController(_handler.Object, _validator.Object, _tenantContext.Object)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };

        var result = await controller.Register(ValidBody, CancellationToken.None);

        var created = result.Result.Should().BeOfType<CreatedResult>().Subject;
        created.StatusCode.Should().Be(201);
        _handler.Verify(h => h.HandleAsync(tenantId, applicationId, It.IsAny<RegisterProviderAccountRequestDto>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Register_ShouldIgnoreTenantAndApplicationFieldsInBodyWhenPresent()
    {
        var tenantId = Guid.NewGuid();
        var applicationId = Guid.NewGuid();
        var spoofedTenant = Guid.NewGuid();
        var spoofedApp = Guid.NewGuid();
        _tenantContext.SetupGet(c => c.TenantId).Returns(tenantId);
        _tenantContext.SetupGet(c => c.ApplicationId).Returns(applicationId);
        _validator.Setup(v => v.ValidateAsync(It.IsAny<RegisterProviderAccountRequestDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult());
        _handler.Setup(h => h.HandleAsync(tenantId, applicationId, It.IsAny<RegisterProviderAccountRequestDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(NewResponse(tenantId, applicationId));

        var controller = new ProviderAccountsController(_handler.Object, _validator.Object, _tenantContext.Object)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };

        var extraJson = JsonSerializer.SerializeToElement(new
        {
            providerCode = "Fake",
            environment = "Sandbox",
            name = "primary",
            apiKey = "raw-api-key",
            secret = (string?)null,
            isDefault = false,
            tenantId = spoofedTenant,
            applicationId = spoofedApp
        });
        var deserialized = extraJson.Deserialize<RegisterProviderAccountRequestDto>(new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            Converters = { new JsonStringEnumConverter() }
        })!;

        await controller.Register(deserialized, CancellationToken.None);

        _handler.Verify(
            h => h.HandleAsync(tenantId, applicationId, It.IsAny<RegisterProviderAccountRequestDto>(), It.IsAny<CancellationToken>()),
            Times.Once);
        _handler.Verify(
            h => h.HandleAsync(spoofedTenant, spoofedApp, It.IsAny<RegisterProviderAccountRequestDto>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Register_ShouldReturnUnauthorizedWhenTenantContextIsMissing()
    {
        _validator.Setup(v => v.ValidateAsync(It.IsAny<RegisterProviderAccountRequestDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult());
        _tenantContext.SetupGet(c => c.TenantId).Throws(new InvalidOperationException("Tenant id not resolved."));

        var controller = new ProviderAccountsController(_handler.Object, _validator.Object, _tenantContext.Object)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };

        var result = await controller.Register(ValidBody, CancellationToken.None);

        var unauthorized = result.Result.Should().BeOfType<UnauthorizedObjectResult>().Subject;
        unauthorized.StatusCode.Should().Be(401);
        _handler.Verify(
            h => h.HandleAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<RegisterProviderAccountRequestDto>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Register_ShouldReturnUnauthorizedWhenApplicationContextIsMissing()
    {
        var tenantId = Guid.NewGuid();
        _validator.Setup(v => v.ValidateAsync(It.IsAny<RegisterProviderAccountRequestDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult());
        _tenantContext.SetupGet(c => c.TenantId).Returns(tenantId);
        _tenantContext.SetupGet(c => c.ApplicationId).Throws(new InvalidOperationException("Application id not resolved."));

        var controller = new ProviderAccountsController(_handler.Object, _validator.Object, _tenantContext.Object)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };

        var result = await controller.Register(ValidBody, CancellationToken.None);

        result.Result.Should().BeOfType<UnauthorizedObjectResult>();
        _handler.Verify(
            h => h.HandleAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<RegisterProviderAccountRequestDto>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Register_ShouldReturnBadRequestWhenValidationFails()
    {
        var failures = new List<ValidationFailure> { new("Name", "Name is required.") };
        _validator.Setup(v => v.ValidateAsync(It.IsAny<RegisterProviderAccountRequestDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult(failures));

        var controller = new ProviderAccountsController(_handler.Object, _validator.Object, _tenantContext.Object)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };

        var result = await controller.Register(
            new RegisterProviderAccountRequestDto(ProviderCode.Fake, ProviderEnvironment.Sandbox, "", "k", null, false),
            CancellationToken.None);

        result.Result.Should().BeOfType<BadRequestObjectResult>();
        _handler.Verify(
            h => h.HandleAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<RegisterProviderAccountRequestDto>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _tenantContext.VerifyGet(c => c.TenantId, Times.Never);
    }

    private static ProviderAccountResponseDto NewResponse(Guid tenantId, Guid applicationId)
        => new(
            Guid.NewGuid(),
            tenantId,
            applicationId,
            ProviderCode.Fake,
            ProviderEnvironment.Sandbox,
            "primary",
            false,
            true,
            DateTime.UtcNow);
}
