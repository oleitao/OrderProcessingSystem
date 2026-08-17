using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OrderProcessing.Application.Exceptions;
using OrderProcessing.Application.Interfaces;
using OrderProcessing.Contracts.Events;
using OrderProcessing.Infrastructure.Messaging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace OrderProcessing.OrderWorker.Consumers;

/// <summary>
/// Consumes OrderCreatedEvent from orders.processing and drives the Order from Pending to
/// Completed. Manual ACK only after the DB commit — never before (section 4, "ACK tardio").
/// RabbitMQ guarantees at-least-once delivery, never exactly-once — the same EventId can arrive
/// more than once (redelivery after a dropped connection, a requeue, etc). ProcessedMessages is
/// what turns that at-least-once delivery into idempotent, effectively-once processing.
///
/// The Order transition and the ProcessedMessage insert commit in a single SaveChanges call
/// (one transaction): if anything fails before that commit, nothing persists and the Order is
/// left exactly as it was. On failure, we never NACK-with-requeue (that would hammer this same
/// consumer again immediately) — instead we publish the message onto the next orders.retry.N
/// queue ourselves, tracking the attempt count in a header, and ACK the original. TTL + dead-
/// lettering on the retry queue brings it back to orders.processing later with backoff.
/// </summary>
public sealed class OrderCreatedConsumer(
    IServiceScopeFactory scopeFactory,
    IRabbitMqConnectionFactory connectionFactory,
    IOptions<OrderProcessingOptions> options,
    ILogger<OrderCreatedConsumer> logger) : BackgroundService
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await ConnectAndConsumeAsync(stoppingToken);

        // RabbitMQ.Client's automatic connection recovery (enabled by default) keeps the
        // connection/consumer alive across transient broker outages after the initial connect —
        // this just has to keep the BackgroundService itself alive until shutdown.
        try
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
        }
    }

    private async Task ConnectAndConsumeAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var connection = await connectionFactory.CreateConnectionAsync(stoppingToken);
                var channel = await connection.CreateChannelAsync(cancellationToken: stoppingToken);

                // Only let up to 10 unacknowledged messages be delivered to this consumer at a
                // time — without this, RabbitMQ pushes the whole queue at once regardless of how
                // fast we can actually process it.
                await channel.BasicQosAsync(prefetchSize: 0, prefetchCount: 10, global: false, stoppingToken);

                var consumer = new AsyncEventingBasicConsumer(channel);
                consumer.ReceivedAsync += (_, eventArgs) => HandleMessageAsync(channel, eventArgs, stoppingToken);

                await channel.BasicConsumeAsync(
                    queue: RabbitMqTopology.OrdersProcessingQueue,
                    autoAck: false,
                    consumer: consumer,
                    cancellationToken: stoppingToken);

                logger.LogInformation(
                    "OrderWorker connected. Listening on queue {Queue}.", RabbitMqTopology.OrdersProcessingQueue);

                return;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Could not connect to RabbitMQ. Retrying in 5s.");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }

    private async Task HandleMessageAsync(IChannel channel, BasicDeliverEventArgs eventArgs, CancellationToken stoppingToken)
    {
        OrderCreatedEvent? @event = null;

        try
        {
            var json = Encoding.UTF8.GetString(eventArgs.Body.ToArray());
            @event = JsonSerializer.Deserialize<OrderCreatedEvent>(json, SerializerOptions)
                ?? throw new InvalidOperationException("OrderCreatedEvent payload deserialized to null.");

            logger.LogInformation(
                "Processing event. EventId: {EventId}, OrderId: {OrderId}, RetryCount: {RetryCount}",
                @event.EventId, @event.OrderId, GetRetryCount(eventArgs.BasicProperties));

            using var scope = scopeFactory.CreateScope();
            var orderRepository = scope.ServiceProvider.GetRequiredService<IOrderRepository>();
            var processedMessageRepository = scope.ServiceProvider.GetRequiredService<IProcessedMessageRepository>();

            // Fast path: skip processing entirely for a message we've already handled. Not the
            // actual guarantee (see the DuplicateMessageException catch below) — just avoids
            // redoing real work in the common redelivery case.
            if (await processedMessageRepository.HasBeenProcessedAsync(@event.EventId, stoppingToken))
            {
                logger.LogInformation(
                    "Event already processed. EventId: {EventId}, OrderId: {OrderId}. Skipping.",
                    @event.EventId, @event.OrderId);

                await channel.BasicAckAsync(eventArgs.DeliveryTag, multiple: false, stoppingToken);
                return;
            }

            var order = await orderRepository.GetByIdAsync(@event.OrderId, stoppingToken);
            if (order is null)
            {
                // Nothing to retry here — the Order will never appear. ACK so this doesn't loop
                // forever; a real system would route this to the DLQ (Phase 13) instead.
                logger.LogWarning(
                    "Order not found for event. EventId: {EventId}, OrderId: {OrderId}. Discarding.",
                    @event.EventId, @event.OrderId);

                await channel.BasicAckAsync(eventArgs.DeliveryTag, multiple: false, stoppingToken);
                return;
            }

            order.StartProcessing();
            logger.LogInformation("Order processing started. OrderId: {OrderId}", order.Id);

            SimulateProcessingFailure();

            order.MarkAsCompleted();
            await processedMessageRepository.MarkAsProcessedAsync(@event.EventId, stoppingToken);

            try
            {
                // Single commit for Pending→Processing→Completed + the ProcessedMessage insert —
                // exactly the "BEGIN TRANSACTION ... COMMIT" block from section 17. Either all of
                // it lands, or none of it does. A throw from SimulateProcessingFailure above never
                // even reaches this line, so StartProcessing never persists either in that case.
                await orderRepository.SaveChangesAsync(stoppingToken);
            }
            catch (DuplicateMessageException)
            {
                // Lost the race: another delivery of this exact message (e.g. a second consumer
                // instance, or a redelivery overlapping with this one) already inserted the
                // ProcessedMessage row first. That delivery is the one whose completion counts;
                // it's still safe to ACK this one instead of retrying it forever.
                logger.LogInformation(
                    "Event lost the processed-message insert race. EventId: {EventId}. Skipping.", @event.EventId);
            }

            logger.LogInformation("Order processing completed. OrderId: {OrderId}", order.Id);

            // THE FAILURE WINDOW: the DB commit above has already happened by this point. If the
            // process crashes right here — after COMMIT, before this ACK reaches RabbitMQ — the
            // broker never receives it and will redeliver the message once it notices this
            // consumer is gone. On redelivery, HasBeenProcessedAsync (checked earlier in this
            // method) finds the ProcessedMessage row that already committed, so the redelivered
            // message is skipped and ACKed without ever touching the Order again.
            await channel.BasicAckAsync(eventArgs.DeliveryTag, multiple: false, stoppingToken);
        }
        catch (Exception ex)
        {
            await RouteToRetryAsync(channel, eventArgs, @event, ex, stoppingToken);
        }
    }

    /// <summary>
    /// Development-only: throws with the configured probability so retries/DLQ can be
    /// demonstrated on demand instead of waiting for a real failure. Never enabled in Production.
    /// </summary>
    private void SimulateProcessingFailure()
    {
        var failureProbability = options.Value.FailureProbability;
        if (failureProbability > 0 && Random.Shared.NextDouble() < failureProbability)
            throw new InvalidOperationException($"Simulated processing failure (FailureProbability={failureProbability}).");
    }

    private async Task RouteToRetryAsync(
        IChannel channel,
        BasicDeliverEventArgs eventArgs,
        OrderCreatedEvent? @event,
        Exception failure,
        CancellationToken stoppingToken)
    {
        var nextRetryCount = GetRetryCount(eventArgs.BasicProperties) + 1;

        if (nextRetryCount > RabbitMqTopology.MaxRetries)
        {
            // TODO (Phase 13): route to orders.dlq instead of dropping. For now this is the one
            // deliberately unfinished edge of this phase — retries beyond MaxRetries have nowhere
            // durable to go yet, so the message would otherwise sit forever bouncing through
            // retry.3. Dropping it here is explicit and logged, never silent.
            logger.LogError(failure,
                "Order processing failed permanently after {RetryCount} retries. EventId: {EventId}, " +
                "OrderId: {OrderId}. Dropping (DLQ arrives in Phase 13).",
                RabbitMqTopology.MaxRetries, @event?.EventId, @event?.OrderId);

            await channel.BasicNackAsync(eventArgs.DeliveryTag, multiple: false, requeue: false, stoppingToken);
            return;
        }

        var retryQueue = RabbitMqTopology.GetRetryQueueName(nextRetryCount);

        var properties = new BasicProperties
        {
            Persistent = true,
            ContentType = eventArgs.BasicProperties.ContentType,
            Headers = new Dictionary<string, object?>
            {
                [RabbitMqTopology.RetryCountHeader] = nextRetryCount,
                [RabbitMqTopology.LastErrorHeader] = Truncate(failure.Message, 500)
            }
        };

        // Publish directly to the retry queue via the default (nameless) exchange, whose routing
        // key convention is "deliver to the queue named exactly this". No binding needed for this
        // leg — only the TTL+DLX configured on the queue itself (Phase 12 topology) matters for
        // what happens once it lands there.
        await channel.BasicPublishAsync(
            exchange: string.Empty,
            routingKey: retryQueue,
            mandatory: false,
            basicProperties: properties,
            body: eventArgs.Body,
            cancellationToken: stoppingToken);

        logger.LogWarning(failure,
            "Order processing failed. EventId: {EventId}, OrderId: {OrderId}. Retry {RetryCount}/{MaxRetries} scheduled on {RetryQueue}.",
            @event?.EventId, @event?.OrderId, nextRetryCount, RabbitMqTopology.MaxRetries, retryQueue);

        // We've taken ownership of retrying this message ourselves by publishing a copy onto the
        // retry queue — ACK the original delivery so RabbitMQ doesn't also try to redeliver it.
        await channel.BasicAckAsync(eventArgs.DeliveryTag, multiple: false, stoppingToken);
    }

    private static int GetRetryCount(IReadOnlyBasicProperties properties)
    {
        if (properties.Headers is not null &&
            properties.Headers.TryGetValue(RabbitMqTopology.RetryCountHeader, out var value))
        {
            return value switch
            {
                int i => i,
                long l => (int)l,
                _ => 0
            };
        }

        return 0;
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];
}
