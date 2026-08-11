using Microsoft.Extensions.Logging;
using OrderProcessing.Application.DTOs;
using OrderProcessing.Application.Interfaces;
using OrderProcessing.Domain.Entities;

namespace OrderProcessing.Application.Services;

public sealed class OrderService(IOrderRepository orderRepository, ILogger<OrderService> logger) : IOrderService
{
    public async Task<OrderDto> CreateOrderAsync(CreateOrderCommand command, CancellationToken cancellationToken)
    {
        var itemDrafts = command.Items
            .Select(item => new OrderItemDraft(item.ProductName, item.Quantity, item.UnitPrice))
            .ToList();

        var order = Order.Create(command.CustomerName, command.CustomerEmail, itemDrafts);

        await orderRepository.AddAsync(order, cancellationToken);
        await orderRepository.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Order created. OrderId: {OrderId}", order.Id);

        return ToDto(order);
    }

    public async Task<OrderDto?> GetOrderByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        var order = await orderRepository.GetByIdAsync(id, cancellationToken);
        return order is null ? null : ToDto(order);
    }

    public async Task<IReadOnlyList<OrderDto>> GetOrdersAsync(CancellationToken cancellationToken)
    {
        var orders = await orderRepository.GetAllAsync(cancellationToken);
        return orders.Select(ToDto).ToList();
    }

    public async Task<OrderDto?> CancelOrderAsync(Guid id, CancellationToken cancellationToken)
    {
        var order = await orderRepository.GetByIdAsync(id, cancellationToken);
        if (order is null)
            return null;

        order.Cancel();
        await orderRepository.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Order cancelled. OrderId: {OrderId}", order.Id);

        return ToDto(order);
    }

    private static OrderDto ToDto(Order order) => new(
        order.Id,
        order.CustomerName,
        order.CustomerEmail,
        order.Status.ToString(),
        order.TotalAmount,
        order.CreatedAtUtc,
        order.UpdatedAtUtc,
        order.Items
            .Select(item => new OrderItemDto(item.Id, item.ProductName, item.Quantity, item.UnitPrice, item.LineTotal))
            .ToList());
}
