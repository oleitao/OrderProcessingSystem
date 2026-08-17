using Microsoft.EntityFrameworkCore;
using OrderProcessing.Application.Interfaces;
using OrderProcessing.Domain.Entities;
using OrderProcessing.Infrastructure.Database;

namespace OrderProcessing.Infrastructure.Repositories;

public sealed class ProcessedMessageRepository(OrderDbContext dbContext) : IProcessedMessageRepository
{
    public Task<bool> HasBeenProcessedAsync(Guid messageId, CancellationToken cancellationToken)
    {
        return dbContext.ProcessedMessages.AnyAsync(message => message.MessageId == messageId, cancellationToken);
    }

    public async Task MarkAsProcessedAsync(Guid messageId, CancellationToken cancellationToken)
    {
        await dbContext.ProcessedMessages.AddAsync(ProcessedMessage.Create(messageId), cancellationToken);
    }
}
