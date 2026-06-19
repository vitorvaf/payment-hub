using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PaymentHub.Application.Abstractions.Bootstrap;
using PaymentHub.Application.Abstractions.Persistence;
using PaymentHub.Domain.Entities;

namespace PaymentHub.Application.Bootstrap;

public sealed class DevelopmentDataSeeder : IDevelopmentDataSeeder
{
    private const string ActorName = "bootstrap-seed";

    private readonly IBootstrapPolicy _policy;
    private readonly BootstrapOptions _options;
    private readonly ITenantRepository _tenants;
    private readonly IApplicationClientRepository _applications;
    private readonly IUnitOfWork _uow;
    private readonly ILogger<DevelopmentDataSeeder> _logger;

    public DevelopmentDataSeeder(
        IBootstrapPolicy policy,
        IOptions<BootstrapOptions> options,
        ITenantRepository tenants,
        IApplicationClientRepository applications,
        IUnitOfWork uow,
        ILogger<DevelopmentDataSeeder> logger)
    {
        _policy = policy;
        _options = options.Value;
        _tenants = tenants;
        _applications = applications;
        _uow = uow;
        _logger = logger;
    }

    public async Task<DevelopmentSeedOutcome> SeedAsync(CancellationToken cancellationToken)
    {
        if (!_policy.ShouldRunDevelopmentSeed)
        {
            var reason = _policy.IsProduction
                ? "Production environment forbids development seed."
                : "Bootstrap:Enabled or Bootstrap:SeedDevelopmentData is false.";
            _logger.LogInformation(
                "Bootstrap development seed skipped in environment {Environment} (policy enabled={Enabled}, production={IsProduction}).",
                _policy.EnvironmentName, _policy.ShouldRunDevelopmentSeed, _policy.IsProduction);
            return DevelopmentSeedOutcome.Skipped(_policy.EnvironmentName, _policy.ShouldRunDevelopmentSeed, reason);
        }

        var slug = _options.DevelopmentTenantSlug;
        var applicationName = _options.DevelopmentApplicationName;

        if (string.IsNullOrWhiteSpace(slug) || string.IsNullOrWhiteSpace(applicationName))
        {
            _logger.LogWarning(
                "Bootstrap development seed cannot run: missing tenant slug or application name in Bootstrap options.");
            return DevelopmentSeedOutcome.Skipped(
                _policy.EnvironmentName,
                _policy.ShouldRunDevelopmentSeed,
                "Bootstrap:DevelopmentTenantSlug or Bootstrap:DevelopmentApplicationName is missing.");
        }

        var existingTenant = await _tenants.GetBySlugAsync(slug, cancellationToken);
        Tenant tenant;
        bool tenantCreated;
        if (existingTenant is not null)
        {
            tenant = existingTenant;
            tenantCreated = false;
            _logger.LogInformation(
                "Bootstrap: dev tenant with slug {Slug} already exists (id={TenantId}). Reusing.",
                slug, tenant.Id);
        }
        else
        {
            tenant = new Tenant(Guid.NewGuid(), $"Development Tenant ({slug})", slug);
            await _tenants.AddAsync(tenant, cancellationToken);
            tenantCreated = true;
            _logger.LogInformation(
                "Bootstrap: created dev tenant with slug {Slug} (id={TenantId}).",
                slug, tenant.Id);
        }

        var existingApplication = await _applications.GetByTenantAndNameAsync(tenant.Id, applicationName, cancellationToken);
        bool applicationCreated;
        if (existingApplication is not null)
        {
            applicationCreated = false;
            _logger.LogInformation(
                "Bootstrap: dev application {ApplicationName} under tenant {TenantId} already exists (id={ApplicationId}). Reusing.",
                applicationName, tenant.Id, existingApplication.Id);
        }
        else
        {
            var app = new ApplicationClient(
                Guid.NewGuid(),
                tenant.Id,
                applicationName,
                webhookUrl: null);
            await _applications.AddAsync(app, cancellationToken);
            applicationCreated = true;
            _logger.LogInformation(
                "Bootstrap: created dev application {ApplicationName} (id={ApplicationId}) under tenant {TenantId}.",
                applicationName, app.Id, tenant.Id);
        }

        if (tenantCreated || applicationCreated)
        {
            await _uow.SaveChangesAsync(cancellationToken);
        }

        var outcome = new DevelopmentSeedOutcome(
            EnvironmentName: _policy.EnvironmentName,
            BootstrapEnabled: _policy.ShouldRunDevelopmentSeed,
            SeedRequested: true,
            SeedExecuted: tenantCreated || applicationCreated,
            TenantCreated: tenantCreated,
            ApplicationCreated: applicationCreated,
            Reason: tenantCreated || applicationCreated
                ? "Seed created or refreshed dev data idempotently."
                : "Seed found existing dev data; nothing to do.");

        _logger.LogInformation(
            "Bootstrap development seed completed in environment {Environment}: tenantCreated={TenantCreated}, applicationCreated={ApplicationCreated}, seedExecuted={SeedExecuted}.",
            outcome.EnvironmentName, outcome.TenantCreated, outcome.ApplicationCreated, outcome.SeedExecuted);

        return outcome;
    }
}
