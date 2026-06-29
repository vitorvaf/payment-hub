using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PaymentHub.IntegrationTests.Infrastructure;

namespace PaymentHub.IntegrationTests.Migrations;

/// <summary>
/// Verifies that the EF Core migration bundle applies cleanly on a real
/// PostgreSQL container and produces every table declared in the
/// <c>PaymentHubDbContextModelSnapshot</c> (tenants, application_clients,
/// provider_accounts, api_keys, payments, payment_attempts, webhook_events,
/// outbox_events, audit_logs, idempotency_keys).
/// </summary>
[Collection(PostgresCollection.Name)]
[Trait("Category", "Integration")]
public sealed class MigrationSmokeTests
{
    private readonly IntegrationTestFactory _factory;

    public MigrationSmokeTests(PostgresFixture fixture)
    {
        _factory = new IntegrationTestFactory(fixture);
    }

    [Fact]
    public async Task Migrations_ShouldApplySuccessfully_OnEmptyPostgresDatabase()
    {
        // The fixture has already applied the migrations in
        // PostgresFixture.InitializeAsync(), so we just validate that
        // applying them AGAIN is a no-op and that the expected tables exist.
        await using var context = _factory.CreateDbContext();

        // Calling MigrateAsync again on an up-to-date database is a no-op.
        var act = async () => await context.Database.MigrateAsync();
        await act.Should().NotThrowAsync();

        var expectedTables = new[]
        {
            "tenants",
            "application_clients",
            "provider_accounts",
            "api_keys",
            "payments",
            "payment_attempts",
            "webhook_events",
            "outbox_events",
            "audit_logs",
            "idempotency_keys",
        };

        var connection = context.Database.GetDbConnection();
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = @"
SELECT table_name
FROM information_schema.tables
WHERE table_schema = 'public'
  AND table_type = 'BASE TABLE';";

        var foundTables = new List<string>();
        await using (var reader = await command.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                foundTables.Add(reader.GetString(0));
            }
        }

        foreach (var expected in expectedTables)
        {
            foundTables.Should().Contain(expected,
                because: $"the migration snapshot must create the {expected} table");
        }
    }
}
