namespace OrderProcessing.Infrastructure.Outbox;

public sealed class OutboxMessage
{
    public Guid Id { get; private set; }
    public string Type { get; private set; } = string.Empty;
    public string Payload { get; private set; } = string.Empty;
    public DateTime OccurredAtUtc { get; private set; }
    public DateTime? ProcessedAtUtc { get; private set; }
    public int RetryCount { get; private set; }
    public string? LastError { get; private set; }

    // Required by EF Core for materialization; kept private so messages can only be built through Create.
    private OutboxMessage() { }

    public static OutboxMessage Create(string type, string payload)
    {
        if (string.IsNullOrWhiteSpace(type))
            throw new ArgumentException("Outbox message type is required.", nameof(type));

        if (string.IsNullOrWhiteSpace(payload))
            throw new ArgumentException("Outbox message payload is required.", nameof(payload));

        return new OutboxMessage
        {
            Id = Guid.NewGuid(),
            Type = type,
            Payload = payload,
            OccurredAtUtc = DateTime.UtcNow,
            RetryCount = 0
        };
    }

    // Used by the Outbox Worker (Phase 8) once it actually publishes to RabbitMQ.
    public void MarkAsProcessed()
    {
        ProcessedAtUtc = DateTime.UtcNow;
    }

    public void RecordFailure(string error)
    {
        RetryCount++;
        LastError = error;
    }
}
