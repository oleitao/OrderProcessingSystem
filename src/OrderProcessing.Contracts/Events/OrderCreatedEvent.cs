namespace OrderProcessing.Contracts.Events;

public sealed record OrderCreatedEvent(Guid EventId, Guid OrderId, DateTime CreatedAtUtc);
