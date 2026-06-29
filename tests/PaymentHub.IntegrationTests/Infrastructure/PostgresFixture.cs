using Microsoft.EntityFrameworkCore;
using PaymentHub.Infrastructure.Postgres;
using Testcontainers.PostgreSql;

namespace PaymentHub.IntegrationTests.Infrastructure;

/// <summary>
/// Spins up a single real PostgreSQL container (via Testcontainers) per test run
/// and applies the EF Core migrations on top of it. Shared by every test class
/// annotated with <c>[Collection("Postgres")]</c> via xUnit's collection fixture
/// mechanism, so the container is reused across all tests in the run.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private const string PostgresImage = "postgres:16-alpine";
    private const string PostgresDatabase = "paymenthub_it";

    private readonly PostgreSqlBuilder _builder = new PostgreSqlBuilder(PostgresImage)
        .WithDatabase(PostgresDatabase)
        .WithUsername("paymenthub_it")
        .WithPassword("paymenthub_it_password")
        .WithCleanUp(true)
        .WithReuse(false);

    private PostgreSqlContainer? _container;

    public string ConnectionString => _container?.GetConnectionString()
        ?? throw new InvalidOperationException(
            "PostgresFixture has not been initialized. Did InitializeAsync run?");

    public async Task InitializeAsync()
    {
        _container = _builder.Build();
        await _container.StartAsync();
        await ApplyMigrationsAsync();
    }

    public async Task DisposeAsync()
    {
        if (_container is not null)
        {
            await _container.DisposeAsync();
            _container = null;
        }
    }

    private async Task ApplyMigrationsAsync()
    {
        await using var context = BuildContext();
        await context.Database.MigrateAsync();
    }

    /// <summary>
    /// Builds a fresh <see cref="PaymentHubDbContext"/> bound to the test container.
    /// Used both by the migration applier and by individual tests via
    /// <see cref="IntegrationTestFactory.CreateDbContext"/> / scope helpers.
    /// </summary>
    internal PaymentHubDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<PaymentHubDbContext>()
            .UseNpgsql(ConnectionString, npgsql => npgsql.MigrationsAssembly(
                typeof(PaymentHubDbContext).Assembly.GetName().Name))
            .Options;
        return new PaymentHubDbContext(options);
    }
}
