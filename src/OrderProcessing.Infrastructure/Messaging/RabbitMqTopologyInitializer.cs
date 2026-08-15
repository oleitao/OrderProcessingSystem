using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;

namespace OrderProcessing.Infrastructure.Messaging;

/// <summary>
/// Declares the orders.exchange / orders.processing / order.created topology on startup.
/// Declaring is idempotent — re-declaring the same exchange/queue with identical properties is a
/// no-op, so this can safely run every time the Api starts without needing external provisioning.
/// </summary>
public sealed class RabbitMqTopologyInitializer(
    IRabbitMqConnectionFactory connectionFactory,
    ILogger<RabbitMqTopologyInitializer> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = await connectionFactory.CreateConnectionAsync(cancellationToken);
            await using var channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken);

            await channel.ExchangeDeclareAsync(
                exchange: RabbitMqTopology.OrdersExchange,
                type: ExchangeType.Direct,
                durable: true,
                autoDelete: false,
                cancellationToken: cancellationToken);

            await channel.QueueDeclareAsync(
                queue: RabbitMqTopology.OrdersProcessingQueue,
                durable: true,
                exclusive: false,
                autoDelete: false,
                cancellationToken: cancellationToken);

            await channel.QueueBindAsync(
                queue: RabbitMqTopology.OrdersProcessingQueue,
                exchange: RabbitMqTopology.OrdersExchange,
                routingKey: RabbitMqTopology.OrderCreatedRoutingKey,
                cancellationToken: cancellationToken);

            logger.LogInformation(
                "RabbitMQ topology declared. Exchange: {Exchange} (durable), Queue: {Queue} (durable), RoutingKey: {RoutingKey}",
                RabbitMqTopology.OrdersExchange, RabbitMqTopology.OrdersProcessingQueue, RabbitMqTopology.OrderCreatedRoutingKey);
        }
        catch (Exception ex)
        {
            // RabbitMQ being unreachable at startup must not stop the Api from serving HTTP
            // requests: Orders and OutboxMessages still persist to PostgreSQL regardless (Phase 6).
            // The Outbox Worker (Phase 8) retries publishing once the broker comes back.
            logger.LogWarning(ex,
                "Could not declare RabbitMQ topology at startup — the broker may be unavailable.");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
