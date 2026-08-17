using System.Globalization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OrderProcessing.Infrastructure;
using OrderProcessing.OrderWorker;
using OrderProcessing.OrderWorker.Consumers;
using Testcontainers.PostgreSql;
using Testcontainers.RabbitMq;

namespace OrderProcessing.IntegrationTests.Fixtures;

/// <summary>
/// Runs the real OrderCreatedConsumer in-process instead of launching the OrderWorker executable
/// as a separate process — same DI composition (AddPersistence/AddRabbitMqMessaging) the real
/// OrderWorker/Program.cs uses, just hosted inside the test process for speed and easy lifecycle
/// control (tests start/stop it explicitly to control exactly when messages get consumed).
/// </summary>
public sealed class OrderWorkerTestHost(
    PostgreSqlContainer postgres, RabbitMqContainer rabbitMq, double failureProbability = 0)
    : IAsyncDisposable
{
    private IHost? _host;

    public async Task StartAsync()
    {
        // Explicit, non-"Development" environment name for the same reason as
        // OrderProcessingApiFactory: avoids ever loading appsettings.Development.json (which, via
        // the shared test output directory, could be either project's copy) instead of the
        // Testcontainers values set below.
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { EnvironmentName = "Testing" });

        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:OrderDb"] = postgres.GetConnectionString(),
            ["RabbitMQ:HostName"] = rabbitMq.Hostname,
            ["RabbitMQ:Port"] = rabbitMq.GetMappedPublicPort(5672).ToString(CultureInfo.InvariantCulture),
            ["RabbitMQ:UserName"] = ContainersFixture.RabbitMqUser,
            ["RabbitMQ:Password"] = ContainersFixture.RabbitMqPassword,
            ["OrderProcessing:FailureProbability"] = failureProbability.ToString(CultureInfo.InvariantCulture)
        });

        builder.Services.AddPersistence(builder.Configuration);
        builder.Services.AddRabbitMqMessaging(builder.Configuration);
        builder.Services.Configure<OrderProcessingOptions>(
            builder.Configuration.GetSection(OrderProcessingOptions.SectionName));
        builder.Services.AddHostedService<OrderCreatedConsumer>();

        _host = builder.Build();
        await _host.StartAsync();
    }

    public async Task StopAsync()
    {
        if (_host is null)
            return;

        await _host.StopAsync();
        _host.Dispose();
        _host = null;
    }

    public async ValueTask DisposeAsync() => await StopAsync();
}
