using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OrderProcessing.Application.Interfaces;
using OrderProcessing.Infrastructure.Database;
using OrderProcessing.Infrastructure.Messaging;
using OrderProcessing.Infrastructure.Outbox;
using OrderProcessing.Infrastructure.Repositories;
using OrderProcessing.Infrastructure.Workers;

namespace OrderProcessing.Infrastructure;

public static class DependencyInjection
{
    /// <summary>Everything the Api host needs: persistence, RabbitMQ, outbox publishing, health checks.</summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddPersistence(configuration);
        services.AddRabbitMqMessaging(configuration);
        services.AddOutboxPublishing(configuration);
        services.AddApplicationHealthChecks();

        return services;
    }

    /// <summary>DbContext + repositories. Needed by both the Api and the OrderWorker.</summary>
    public static IServiceCollection AddPersistence(this IServiceCollection services, IConfiguration configuration)
    {
        // Reads the connection string lazily, inside the options callback, instead of capturing it
        // in a local variable up front: AddPersistence runs before builder.Build(), and anything
        // that finalizes configuration later — e.g. WebApplicationFactory's test overrides, which
        // only apply at Build() time — would otherwise be invisible to an eagerly-captured value.
        services.AddDbContext<OrderDbContext>((serviceProvider, options) =>
        {
            var connectionString = serviceProvider.GetRequiredService<IConfiguration>().GetConnectionString("OrderDb")
                ?? throw new InvalidOperationException("Connection string 'OrderDb' is not configured.");

            options.UseNpgsql(connectionString);
        });

        services.AddScoped<IOrderRepository, OrderRepository>();
        services.AddScoped<IIdempotencyRepository, IdempotencyRepository>();
        services.AddScoped<IProcessedMessageRepository, ProcessedMessageRepository>();

        return services;
    }

    /// <summary>
    /// Connection factory + topology declaration. Needed by both hosts: the Api publishes into
    /// orders.exchange, the OrderWorker consumes from orders.processing — each declares the
    /// topology defensively on its own startup rather than assuming the other host got there first.
    /// </summary>
    public static IServiceCollection AddRabbitMqMessaging(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<RabbitMqOptions>(configuration.GetSection(RabbitMqOptions.SectionName));
        services.AddSingleton<IRabbitMqConnectionFactory, RabbitMqConnectionFactory>();
        services.AddHostedService<RabbitMqTopologyInitializer>();

        return services;
    }

    /// <summary>
    /// Outbox writing + the background publisher. Api-only — the OrderWorker must NOT also run
    /// this, or both processes would race to publish the same pending messages.
    /// </summary>
    public static IServiceCollection AddOutboxPublishing(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<IOutboxWriter, OutboxWriter>();

        services.Configure<OutboxWorkerOptions>(configuration.GetSection(OutboxWorkerOptions.SectionName));
        services.AddHostedService<OutboxPublisherWorker>();

        return services;
    }

    /// <summary>
    /// Background scan for Orders stuck in Processing. OrderWorker-only — it's the process that
    /// owns Order state transitions past Pending, so it's the natural place to also own recovering
    /// from anomalies in that same state machine.
    /// </summary>
    public static IServiceCollection AddProcessingRecovery(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<ProcessingRecoveryWorkerOptions>(configuration.GetSection(ProcessingRecoveryWorkerOptions.SectionName));
        services.AddHostedService<ProcessingRecoveryWorker>();

        return services;
    }

    /// <summary>
    /// GET /health checks both dependencies the Api can't function without: PostgreSQL (via a
    /// trivial query against OrderDbContext) and RabbitMQ (via RabbitMqHealthCheck).
    /// </summary>
    public static IServiceCollection AddApplicationHealthChecks(this IServiceCollection services)
    {
        services.AddHealthChecks()
            .AddDbContextCheck<OrderDbContext>(name: "postgresql")
            .AddCheck<RabbitMqHealthCheck>("rabbitmq");

        return services;
    }
}
