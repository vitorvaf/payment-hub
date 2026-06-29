using FluentValidation;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;
using PaymentHub.Api.Auth;
using PaymentHub.Application.Abstractions.Bootstrap;
using PaymentHub.Application.Abstractions.Context;
using PaymentHub.Application.Abstractions.Providers;
using PaymentHub.Application.Bootstrap;
using PaymentHub.Application.Checkouts;
using PaymentHub.Application.Orchestration;
using PaymentHub.Application.Payments;
using PaymentHub.Application.Tenants;
using PaymentHub.Application.Webhooks;
using Swashbuckle.AspNetCore.SwaggerGen;
using Microsoft.OpenApi.Models;
using PaymentHub.Infrastructure.Postgres;
using PaymentHub.Infrastructure.Providers;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((ctx, lc) => lc
    .ReadFrom.Configuration(ctx.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console());

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Payment Hub API",
        Version = "v1",
        Description = "Payment Gateway MVP - multitenant orchestrator"
    });
    c.AddSecurityDefinition("ApiKey", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.ApiKey,
        In = ParameterLocation.Header,
        Description = "Use 'Bearer <api_key>'."
    });
    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "ApiKey"
                }
            },
            Array.Empty<string>()
        }
    });
});

builder.Services.AddHttpContextAccessor();

builder.Services.AddPaymentHubPostgres(builder.Configuration);
builder.Services.AddPaymentHubProviders(builder.Configuration);

builder.Services.AddScoped<ITenantContext, HttpTenantContext>();
builder.Services.AddSingleton<IRuntimeEnvironment, HostRuntimeEnvironment>();
builder.Services.AddSingleton<IBootstrapPolicy, HostBootstrapPolicy>();
builder.Services.Configure<BootstrapOptions>(builder.Configuration.GetSection(BootstrapOptions.SectionName));
builder.Services.AddScoped<IDevelopmentDataSeeder, DevelopmentDataSeeder>();

builder.Services.AddScoped<IRegisterTenantHandler, RegisterTenantHandler>();
builder.Services.AddScoped<IRegisterApplicationClientHandler, RegisterApplicationClientHandler>();
builder.Services.AddScoped<IRegisterProviderAccountHandler, RegisterProviderAccountHandler>();
builder.Services.AddScoped<ICreateCheckoutHandler, CreateCheckoutHandler>();
builder.Services.AddScoped<IGetPaymentByIdHandler, GetPaymentByIdHandler>();
builder.Services.AddScoped<IListPaymentsHandler, ListPaymentsHandler>();
builder.Services.AddScoped<IReceiveProviderWebhookHandler, ReceiveProviderWebhookHandler>();
builder.Services.AddScoped<IProcessWebhookEventHandler, ProcessWebhookEventHandler>();
builder.Services.AddScoped<IPaymentOrchestrator, PaymentOrchestrator>();

builder.Services.AddValidatorsFromAssemblyContaining<RegisterTenantValidator>();

builder.Services.AddHealthChecks();

var app = builder.Build();

{
    using var scope = app.Services.CreateScope();
    var seeder = scope.ServiceProvider.GetRequiredService<IDevelopmentDataSeeder>();
    _ = await seeder.SeedAsync(CancellationToken.None);
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseExceptionHandler(handler => handler.Run(async context =>
{
    var feature = context.Features.Get<IExceptionHandlerFeature>();
    var ex = feature?.Error;
    var logger = context.RequestServices.GetRequiredService<ILogger<Program>>();
    logger.LogError(ex, "Unhandled exception in request {Path}", context.Request.Path);

    context.Response.StatusCode = StatusCodes.Status500InternalServerError;
    context.Response.ContentType = "application/json";

    var payload = app.Environment.IsDevelopment()
        ? (object)new { error = "internal_error", message = ex?.Message, type = ex?.GetType().Name }
        : new { error = "internal_error" };

    await context.Response.WriteAsJsonAsync(payload);
}));

app.UseMiddleware<ApiKeyAuthenticationMiddleware>();

app.MapControllers();

app.Run();

public partial class Program { }
