using OrderProcessing.Application.Exceptions;
using OrderProcessing.Application.Interfaces;
using OrderProcessing.Domain.Entities;

namespace OrderProcessing.UnitTests.Application.Fakes;

/// <summary>
/// In-memory stand-in for IOrderRepository — no database involved. Good enough for exercising
/// OrderService's own branching logic; real persistence/constraint behavior is covered by the
/// Testcontainers-backed integration tests instead.
/// </summary>
internal sealed class FakeOrderRepository : IOrderRepository
{
    private readonly List<Order> _orders = [];

    public bool ThrowIdempotencyConflictOnNextSave { get; set; }
    public bool ThrowDuplicateMessageOnNextSave { get; set; }
    public int SaveChangesCallCount { get; private set; }

    public void Seed(Order order) => _orders.Add(order);

    public Task AddAsync(Order order, CancellationToken cancellationToken)
    {
        _orders.Add(order);
        return Task.CompletedTask;
    }

    public Task<Order?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(_orders.FirstOrDefault(order => order.Id == id));

    public Task<IReadOnlyList<Order>> GetAllAsync(Guid? ownerUserId, CancellationToken cancellationToken)
    {
        var query = ownerUserId is { } userId
            ? _orders.Where(order => order.UserId == userId)
            : _orders.AsEnumerable();

        return Task.FromResult<IReadOnlyList<Order>>(query.OrderByDescending(order => order.CreatedAtUtc).ToList());
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        SaveChangesCallCount++;

        if (ThrowIdempotencyConflictOnNextSave)
        {
            ThrowIdempotencyConflictOnNextSave = false;
            throw new IdempotencyKeyConflictException("key", new InvalidOperationException("simulated"));
        }

        if (ThrowDuplicateMessageOnNextSave)
        {
            ThrowDuplicateMessageOnNextSave = false;
            throw new DuplicateMessageException(Guid.NewGuid(), new InvalidOperationException("simulated"));
        }

        return Task.CompletedTask;
    }
}
