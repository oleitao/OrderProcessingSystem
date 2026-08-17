using Testcontainers.PostgreSql;
using Testcontainers.RabbitMq;

namespace OrderProcessing.IntegrationTests.Fixtures;

/// <summary>
/// Real Postgres + RabbitMQ in ephemeral Docker containers — same images as docker-compose.yml
/// (postgres:16-alpine, rabbitmq:3.13-management-alpine). Started once for the whole test run and
/// shared by every test in IntegrationTestCollection, which is what makes that sharing safe: xUnit
/// never runs two test classes in the same collection concurrently, so tests never race over the
/// same broker/database state.
///
/// Why Testcontainers instead of pointing at the docker-compose stack on the host: these tests
/// need to freely stop/restart the broker (Tests 5-6) and inspect exact queue depths (Tests 7-8)
/// without disturbing — or depending on the state of — whatever the developer already has running
/// locally, and without requiring any manually-run infrastructure in CI.
/// </summary>
public sealed class ContainersFixture : IAsyncLifetime
{
    public const string RabbitMqUser = "orderprocessing";
    public const string RabbitMqPassword = "orderprocessing";

    public PostgreSqlContainer Postgres { get; } = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("orderprocessing")
        .WithUsername("orderprocessing")
        .WithPassword("orderprocessing")
        .Build();

    public RabbitMqContainer RabbitMq { get; } = new RabbitMqBuilder("rabbitmq:3.13-management-alpine")
        .WithUsername(RabbitMqUser)
        .WithPassword(RabbitMqPassword)
        .Build();

    public async Task InitializeAsync()
    {
        await Task.WhenAll(Postgres.StartAsync(), RabbitMq.StartAsync());
    }

    public async Task DisposeAsync()
    {
        await Task.WhenAll(Postgres.StopAsync(), RabbitMq.StopAsync());
    }
}

[CollectionDefinition(Name)]
public sealed class IntegrationTestCollection : ICollectionFixture<ContainersFixture>
{
    public const string Name = "Integration";
}
