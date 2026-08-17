namespace OrderProcessing.Application.Exceptions;

/// <summary>
/// Thrown by the repository when the database's primary key on ProcessedMessages.MessageId
/// rejects an insert — meaning another delivery of the same message already committed first.
/// This, not the prior lookup, is what actually guarantees a message is never processed twice.
/// </summary>
public sealed class DuplicateMessageException(Guid messageId, Exception innerException)
    : Exception($"Message '{messageId}' was already processed by another delivery.", innerException)
{
    public Guid MessageId { get; } = messageId;
}
