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
    /// <summary>Everything the Api host needs: persistence, RabbitMQ, and outbox publishing.</summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddPersistence(configuration);
        services.AddRabbitMqMessaging(configuration);
        services.AddOutboxPublishing(configuration);

        return services;
    }

    /// <summary>DbContext + repositories. Needed by both the Api and the OrderWorker.</summary>
    public static IServiceCollection AddPersistence(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("OrderDb")
            ?? throw new InvalidOperationException("Connection string 'OrderDb' is not configured.");

        services.AddDbContext<OrderDbContext>(options => options.UseNpgsql(connectionString));

        services.AddScoped<IOrderRepository, OrderRepository>();
        services.AddScoped<IIdempotencyRepository, IdempotencyRepository>();

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
}
