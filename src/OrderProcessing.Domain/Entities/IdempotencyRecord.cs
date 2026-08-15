namespace OrderProcessing.Domain.Entities;

public sealed class IdempotencyRecord
{
    public Guid Id { get; private set; }
    public string Key { get; private set; } = string.Empty;
    public Guid OrderId { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }

    // Required by EF Core for materialization; kept private so records can only be built through Create.
    private IdempotencyRecord() { }

    public static IdempotencyRecord Create(string key, Guid orderId)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Idempotency key is required.", nameof(key));

        return new IdempotencyRecord
        {
            Id = Guid.NewGuid(),
            Key = key.Trim(),
            OrderId = orderId,
            CreatedAtUtc = DateTime.UtcNow
        };
    }
}
