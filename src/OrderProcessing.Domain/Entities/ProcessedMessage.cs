namespace OrderProcessing.Domain.Entities;

public sealed class ProcessedMessage
{
    public Guid MessageId { get; private set; }
    public DateTime ProcessedAtUtc { get; private set; }

    // Required by EF Core for materialization; kept private so records can only be built through Create.
    private ProcessedMessage() { }

    public static ProcessedMessage Create(Guid messageId)
    {
        if (messageId == Guid.Empty)
            throw new ArgumentException("Message id is required.", nameof(messageId));

        return new ProcessedMessage
        {
            MessageId = messageId,
            ProcessedAtUtc = DateTime.UtcNow
        };
    }
}
