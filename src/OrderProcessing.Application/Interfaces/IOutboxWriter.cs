namespace OrderProcessing.Application.Interfaces;

/// <summary>
/// Records that a domain event needs to be published, without the Application layer knowing
/// anything about how it's stored or delivered (Outbox table, serialization, RabbitMQ, ...).
/// The actual write only becomes durable when the caller's IOrderRepository.SaveChangesAsync
/// runs — both share the same DbContext instance within a request, so Order + outbox record
/// commit atomically.
/// </summary>
public interface IOutboxWriter
{
    Task AddOrderCreatedEventAsync(Guid orderId, CancellationToken cancellationToken);
}
