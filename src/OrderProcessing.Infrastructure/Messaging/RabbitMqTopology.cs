namespace OrderProcessing.Infrastructure.Messaging;

/// <summary>
/// Names for the exchange/queue/routing-key that both the publisher (Outbox Worker, Phase 8)
/// and the consumer (OrderProcessing.OrderWorker, Phase 9) declare and use. Centralized here so
/// both sides can only ever agree, never drift into using different literal strings.
/// </summary>
public static class RabbitMqTopology
{
    public const string OrdersExchange = "orders.exchange";
    public const string OrdersProcessingQueue = "orders.processing";
    public const string OrderCreatedRoutingKey = "order.created";
}
