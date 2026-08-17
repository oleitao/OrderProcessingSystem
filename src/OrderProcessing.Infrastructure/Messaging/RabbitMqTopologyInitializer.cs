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

            // Each retry queue is a TTL "waiting room": no consumer ever reads from it directly.
            // Once a message sits here for x-message-ttl, RabbitMQ dead-letters it back into
            // orders.exchange with order.created — which routes it straight back into
            // orders.processing for another attempt, this time with a higher x-retry-count header.
            foreach (var (queueName, ttl) in RabbitMqTopology.RetryQueues)
            {
                await channel.QueueDeclareAsync(
                    queue: queueName,
                    durable: true,
                    exclusive: false,
                    autoDelete: false,
                    arguments: new Dictionary<string, object?>
                    {
                        ["x-message-ttl"] = (int)ttl.TotalMilliseconds,
                        ["x-dead-letter-exchange"] = RabbitMqTopology.OrdersExchange,
                        ["x-dead-letter-routing-key"] = RabbitMqTopology.OrderCreatedRoutingKey
                    },
                    cancellationToken: cancellationToken);
            }

            // Terminal queue for messages that exceeded MaxRetries. No TTL/DLX arguments — nothing
            // ever auto-moves out of here; a human has to look at it.
            await channel.QueueDeclareAsync(
                queue: RabbitMqTopology.OrdersDlqQueue,
                durable: true,
                exclusive: false,
                autoDelete: false,
                cancellationToken: cancellationToken);

            logger.LogInformation(
                "RabbitMQ topology declared. Exchange: {Exchange} (durable), Queue: {Queue} (durable), " +
                "RoutingKey: {RoutingKey}, RetryQueues: {RetryQueues}, Dlq: {Dlq}",
                RabbitMqTopology.OrdersExchange, RabbitMqTopology.OrdersProcessingQueue,
                RabbitMqTopology.OrderCreatedRoutingKey,
                string.Join(", ", RabbitMqTopology.RetryQueues.Select(q => $"{q.QueueName}({q.Ttl.TotalSeconds}s)")),
                RabbitMqTopology.OrdersDlqQueue);
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
