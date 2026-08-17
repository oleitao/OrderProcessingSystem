using OrderProcessing.Domain.Entities;

namespace OrderProcessing.Application.Interfaces;

public interface IOrderRepository
{
    Task AddAsync(Order order, CancellationToken cancellationToken);
    Task<Order?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Pass null to return every order (Admin); pass a userId to return only that user's orders.</summary>
    Task<IReadOnlyList<Order>> GetAllAsync(Guid? ownerUserId, CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
