using System.Globalization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Testcontainers.PostgreSql;
using Testcontainers.RabbitMq;

namespace OrderProcessing.IntegrationTests.Fixtures;

/// <summary>
/// Hosts the real Api (Program.cs, real controllers, real DI wiring) in-process, pointed at the
/// Testcontainers-backed Postgres/RabbitMQ instead of appsettings.Development.json's localhost
/// values. Building this factory (via CreateClient()) also triggers Program.cs's own
/// Database.MigrateAsync() call — the same migration path used in Docker — so there's no separate
/// test-only schema setup to keep in sync.
/// </summary>
public sealed class OrderProcessingApiFactory(
    PostgreSqlContainer postgres, RabbitMqContainer rabbitMq, int? rabbitMqPortOverride = null)
    : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Deliberately NOT "Development": that environment name would load
        // appsettings.Development.json, whose hardcoded localhost:5432/5672 values could win a
        // configuration-precedence race against the Testcontainers ports below, silently pointing
        // this factory at whatever Postgres/RabbitMQ happen to be running on the developer's own
        // machine instead of the ephemeral, isolated containers this test spun up.
        builder.UseEnvironment("Testing");

        var rabbitMqPort = rabbitMqPortOverride ?? rabbitMq.GetMappedPublicPort(5672);

        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:OrderDb"] = postgres.GetConnectionString(),
                ["RabbitMQ:HostName"] = rabbitMq.Hostname,
                ["RabbitMQ:Port"] = rabbitMqPort.ToString(CultureInfo.InvariantCulture),
                ["RabbitMQ:UserName"] = ContainersFixture.RabbitMqUser,
                ["RabbitMQ:Password"] = ContainersFixture.RabbitMqPassword,
                // Fast polling so Outbox Worker tests don't need to wait out the 5s dev default.
                ["OutboxWorker:PollingIntervalSeconds"] = "1"
            });
        });
    }
}
