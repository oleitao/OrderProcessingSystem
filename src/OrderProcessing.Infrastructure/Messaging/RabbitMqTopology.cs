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

    public const string OrdersRetryQueue1 = "orders.retry.1";
    public const string OrdersRetryQueue2 = "orders.retry.2";
    public const string OrdersRetryQueue3 = "orders.retry.3";

    // Headers carried on a message so a retry decision never depends on the Order's own DB state
    // (section 19 — "não confiar apenas no estado da Order para determinar retries").
    public const string RetryCountHeader = "x-retry-count";
    public const string LastErrorHeader = "x-last-error";

    /// <summary>
    /// Each retry queue is a plain TTL "waiting room" with no consumer of its own: once a message
    /// sits there for its TTL, RabbitMQ dead-letters it back into orders.exchange with routing key
    /// order.created, which routes it straight back into orders.processing for another attempt.
    /// </summary>
    public static readonly IReadOnlyList<(string QueueName, TimeSpan Ttl)> RetryQueues =
    [
        (OrdersRetryQueue1, TimeSpan.FromSeconds(5)),
        (OrdersRetryQueue2, TimeSpan.FromSeconds(15)),
        (OrdersRetryQueue3, TimeSpan.FromSeconds(45))
    ];

    public static int MaxRetries => RetryQueues.Count;

    public static string GetRetryQueueName(int retryAttempt) => retryAttempt switch
    {
        1 => OrdersRetryQueue1,
        2 => OrdersRetryQueue2,
        3 => OrdersRetryQueue3,
        _ => throw new ArgumentOutOfRangeException(
            nameof(retryAttempt), retryAttempt, $"No retry queue configured for attempt {retryAttempt}.")
    };
}
