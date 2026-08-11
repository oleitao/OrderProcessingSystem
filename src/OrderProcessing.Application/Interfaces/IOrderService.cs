using OrderProcessing.Application.DTOs;

namespace OrderProcessing.Application.Interfaces;

public interface IOrderService
{
    Task<OrderDto> CreateOrderAsync(CreateOrderCommand command, CancellationToken cancellationToken);
    Task<OrderDto?> GetOrderByIdAsync(Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyList<OrderDto>> GetOrdersAsync(CancellationToken cancellationToken);
    Task<OrderDto?> CancelOrderAsync(Guid id, CancellationToken cancellationToken);
}
