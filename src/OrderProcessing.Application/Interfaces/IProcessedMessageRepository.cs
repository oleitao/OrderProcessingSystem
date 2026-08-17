namespace OrderProcessing.Application.Interfaces;

public interface IProcessedMessageRepository
{
    Task<bool> HasBeenProcessedAsync(Guid messageId, CancellationToken cancellationToken);
    Task MarkAsProcessedAsync(Guid messageId, CancellationToken cancellationToken);
}
