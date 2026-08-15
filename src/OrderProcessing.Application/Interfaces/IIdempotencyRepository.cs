using OrderProcessing.Domain.Entities;

namespace OrderProcessing.Application.Interfaces;

public interface IIdempotencyRepository
{
    Task<IdempotencyRecord?> FindByKeyAsync(string key, CancellationToken cancellationToken);
    Task AddAsync(IdempotencyRecord record, CancellationToken cancellationToken);
}
