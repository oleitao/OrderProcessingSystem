using OrderProcessing.Application.Interfaces;
using OrderProcessing.Domain.Entities;

namespace OrderProcessing.UnitTests.Application.Fakes;

/// <summary>
/// In-memory stand-in for IIdempotencyRepository. RevealOnSecondFind lets a test simulate the
/// exact race OrderService defends against: the first FindByKeyAsync call (the fast-path lookup)
/// finds nothing, a concurrent request wins the insert, and only the second call (after the
/// SaveChanges conflict) sees the winning record.
/// </summary>
internal sealed class FakeIdempotencyRepository : IIdempotencyRepository
{
    private readonly List<IdempotencyRecord> _records = [];
    private int _findCallCount;

    public IdempotencyRecord? RevealOnSecondFind { get; set; }

    public Task<IdempotencyRecord?> FindByKeyAsync(string key, CancellationToken cancellationToken)
    {
        _findCallCount++;

        if (_findCallCount == 1)
            return Task.FromResult(_records.FirstOrDefault(record => record.Key == key));

        var revealed = RevealOnSecondFind is not null && RevealOnSecondFind.Key == key
            ? RevealOnSecondFind
            : _records.FirstOrDefault(record => record.Key == key);

        return Task.FromResult(revealed);
    }

    public Task AddAsync(IdempotencyRecord record, CancellationToken cancellationToken)
    {
        _records.Add(record);
        return Task.CompletedTask;
    }
}
