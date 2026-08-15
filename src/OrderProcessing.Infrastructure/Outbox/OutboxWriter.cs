using System.Text.Json;
using Microsoft.Extensions.Logging;
using OrderProcessing.Application.Interfaces;
using OrderProcessing.Contracts.Events;
using OrderProcessing.Infrastructure.Database;

namespace OrderProcessing.Infrastructure.Outbox;

public sealed class OutboxWriter(OrderDbContext dbContext, ILogger<OutboxWriter> logger) : IOutboxWriter
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public async Task AddOrderCreatedEventAsync(Guid orderId, CancellationToken cancellationToken)
    {
        var @event = new OrderCreatedEvent(Guid.NewGuid(), orderId, DateTime.UtcNow);
        var payload = JsonSerializer.Serialize(@event, SerializerOptions);

        var message = OutboxMessage.Create(OutboxMessageTypes.OrderCreated, payload);

        await dbContext.OutboxMessages.AddAsync(message, cancellationToken);

        // Staged only — not durable until the caller's SaveChangesAsync commits. Logged here
        // (not after commit) purely because this is where the OutboxMessageId/EventId are known;
        // if the surrounding transaction rolls back, this log line is the only slightly misleading
        // artifact, and the DB is still the source of truth for what was actually persisted.
        logger.LogInformation(
            "Outbox message created. OutboxMessageId: {OutboxMessageId}, EventId: {EventId}, OrderId: {OrderId}",
            message.Id, @event.EventId, orderId);
    }
}
