using Microsoft.EntityFrameworkCore;
using OrderProcessing.Application.Interfaces;
using OrderProcessing.Domain.Entities;
using OrderProcessing.Infrastructure.Database;

namespace OrderProcessing.Infrastructure.Repositories;

public sealed class IdempotencyRepository(OrderDbContext dbContext) : IIdempotencyRepository
{
    public Task<IdempotencyRecord?> FindByKeyAsync(string key, CancellationToken cancellationToken)
    {
        return dbContext.IdempotencyRecords.FirstOrDefaultAsync(record => record.Key == key, cancellationToken);
    }

    public async Task AddAsync(IdempotencyRecord record, CancellationToken cancellationToken)
    {
        await dbContext.IdempotencyRecords.AddAsync(record, cancellationToken);
    }
}
