using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OrderProcessing.Infrastructure.Database;
using OrderProcessing.Infrastructure.Messaging;
using OrderProcessing.Infrastructure.Outbox;
using RabbitMQ.Client;

namespace OrderProcessing.Infrastructure.Workers;

/// <summary>
/// Polls for pending OutboxMessages and publishes them to RabbitMQ with publisher confirms.
/// Database → OutboxMessage → (this worker) → RabbitMQ → Publisher Confirm → ProcessedAtUtc.
/// If RabbitMQ is unreachable, messages simply stay pending — nothing is lost, and the next poll
/// tries again. This is the other half of the Outbox Pattern started in Phase 6.
/// </summary>
public sealed class OutboxPublisherWorker(
    IServiceScopeFactory scopeFactory,
    IRabbitMqConnectionFactory connectionFactory,
    IOptions<OutboxWorkerOptions> options,
    ILogger<OutboxPublisherWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var pollingInterval = TimeSpan.FromSeconds(options.Value.PollingIntervalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PublishPendingMessagesAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Unexpected error while polling the outbox.");
            }

            try
            {
                // A fixed delay between polls — not Task.Delay(0) in a tight loop — is what keeps
                // this from hammering PostgreSQL when the outbox is empty (rule 8 of section 34).
                await Task.Delay(pollingInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                // Shutting down; ExecuteAsync's while condition will now exit the loop.
            }
        }
    }

    private async Task PublishPendingMessagesAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<OrderDbContext>();

        var pendingMessages = await dbContext.OutboxMessages
            .Where(message => message.ProcessedAtUtc == null)
            .OrderBy(message => message.OccurredAtUtc)
            .Take(options.Value.BatchSize)
            .ToListAsync(cancellationToken);

        if (pendingMessages.Count == 0)
            return;

        IConnection? connection = null;
        IChannel? channel = null;
        var attemptedMessageIds = new HashSet<Guid>();

        try
        {
            connection = await connectionFactory.CreateConnectionAsync(cancellationToken);
            channel = await connection.CreateChannelAsync(
                new CreateChannelOptions(publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true),
                cancellationToken);

            foreach (var message in pendingMessages)
            {
                attemptedMessageIds.Add(message.Id);
                await PublishAsync(channel, message, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not publish outbox messages to RabbitMQ. Will retry on the next poll.");

            // The message that actually threw already recorded its own failure inside PublishAsync.
            // Everything else here never got attempted (connection never opened, or broke before
            // reaching it) — "RabbitMQ is unreachable" applies to those just as much, so their
            // RetryCount/LastError should reflect that too, not stay untouched at 0/null forever.
            foreach (var message in pendingMessages.Where(message => !attemptedMessageIds.Contains(message.Id)))
                message.RecordFailure(ex.Message);
        }
        finally
        {
            if (channel is not null)
                await channel.CloseAsync(cancellationToken);

            if (connection is not null)
                await connection.CloseAsync(cancellationToken);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task PublishAsync(IChannel channel, OutboxMessage message, CancellationToken cancellationToken)
    {
        try
        {
            var routingKey = GetRoutingKey(message.Type);
            var body = Encoding.UTF8.GetBytes(message.Payload);

            var properties = new BasicProperties
            {
                Persistent = true,
                ContentType = "application/json"
            };

            // With publisherConfirmationsEnabled, this awaits the broker's ack before returning —
            // that confirmation, not just a successful TCP write, is what "publisher confirm" means.
            await channel.BasicPublishAsync(
                exchange: RabbitMqTopology.OrdersExchange,
                routingKey: routingKey,
                mandatory: false,
                basicProperties: properties,
                body: body,
                cancellationToken: cancellationToken);

            message.MarkAsProcessed();

            logger.LogInformation(
                "Event published. OutboxMessageId: {OutboxMessageId}, Type: {Type}, RoutingKey: {RoutingKey}",
                message.Id, message.Type, routingKey);
        }
        catch (Exception ex)
        {
            message.RecordFailure(ex.Message);

            logger.LogWarning(ex,
                "Failed to publish outbox message. OutboxMessageId: {OutboxMessageId}, RetryCount: {RetryCount}",
                message.Id, message.RetryCount);

            // Stop this batch here: the channel/connection is almost certainly broken for every
            // remaining message too, so retrying them now would just be N more guaranteed failures.
            throw;
        }
    }

    private static string GetRoutingKey(string outboxMessageType) => outboxMessageType switch
    {
        OutboxMessageTypes.OrderCreated => RabbitMqTopology.OrderCreatedRoutingKey,
        _ => throw new InvalidOperationException($"No routing key mapping for outbox message type '{outboxMessageType}'.")
    };
}
