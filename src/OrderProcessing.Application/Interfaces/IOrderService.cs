using OrderProcessing.Application.DTOs;

namespace OrderProcessing.Application.Interfaces;

public interface IOrderService
{
    Task<CreateOrderResult> CreateOrderAsync(CreateOrderCommand command, CancellationToken cancellationToken);
    Task<OrderDto?> GetOrderByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Pass null to return every order (Admin); pass a userId to return only that user's orders.</summary>
    Task<IReadOnlyList<OrderDto>> GetOrdersAsync(Guid? ownerUserId, CancellationToken cancellationToken);

    Task<OrderDto?> CancelOrderAsync(Guid id, CancellationToken cancellationToken);
}
