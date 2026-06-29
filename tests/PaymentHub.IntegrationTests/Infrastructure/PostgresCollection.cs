namespace PaymentHub.IntegrationTests.Infrastructure;

/// <summary>
/// xUnit collection that shares a single <see cref="PostgresFixture"/>
/// (one real PostgreSQL container per test run) across every test class
/// that opts in via <c>[Collection("Postgres")]</c>. See <c>docs/specs/013-testing-strategy.md</c>
/// for the rationale behind the shared-container + TRUNCATE isolation model.
/// </summary>
[CollectionDefinition(Name)]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>
{
    public const string Name = "Postgres";
}
