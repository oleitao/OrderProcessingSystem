using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
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
/// </summary>
public sealed class OrderCreatedConsumer(
    IServiceScopeFactory scopeFactory,
    IRabbitMqConnectionFactory connectionFactory,
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
                "Processing event. EventId: {EventId}, OrderId: {OrderId}", @event.EventId, @event.OrderId);

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
            await orderRepository.SaveChangesAsync(stoppingToken);
            logger.LogInformation("Order processing started. OrderId: {OrderId}", order.Id);

            // Placeholder for real business processing. Phase 12 hooks FailureProbability in here
            // to demonstrate retries.
            order.MarkAsCompleted();
            await processedMessageRepository.MarkAsProcessedAsync(@event.EventId, stoppingToken);

            try
            {
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

            // ACK only after the DB commit above — if the process had crashed before this line,
            // RabbitMQ would redeliver the message once it noticed the consumer was gone.
            await channel.BasicAckAsync(eventArgs.DeliveryTag, multiple: false, stoppingToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Order processing failed. EventId: {EventId}, OrderId: {OrderId}",
                @event?.EventId, @event?.OrderId);

            // No retry-queue/TTL routing yet (Phase 12) — requeueing immediately is a stand-in
            // until then, acceptable only because nothing forces a failure in this phase yet.
            await channel.BasicNackAsync(eventArgs.DeliveryTag, multiple: false, requeue: true, stoppingToken);
        }
    }
}
