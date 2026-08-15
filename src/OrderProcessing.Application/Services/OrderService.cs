using Microsoft.Extensions.Logging;
using OrderProcessing.Application.DTOs;
using OrderProcessing.Application.Exceptions;
using OrderProcessing.Application.Interfaces;
using OrderProcessing.Domain.Entities;

namespace OrderProcessing.Application.Services;

public sealed class OrderService(
    IOrderRepository orderRepository,
    IIdempotencyRepository idempotencyRepository,
    IOutboxWriter outboxWriter,
    ILogger<OrderService> logger) : IOrderService
{
    public async Task<CreateOrderResult> CreateOrderAsync(CreateOrderCommand command, CancellationToken cancellationToken)
    {
        var idempotencyKey = command.IdempotencyKey;

        if (idempotencyKey is not null)
        {
            var existingOrder = await FindOrderByIdempotencyKeyAsync(idempotencyKey, cancellationToken);
            if (existingOrder is not null)
            {
                logger.LogInformation(
                    "Idempotency-Key {IdempotencyKey} already used. Returning existing OrderId: {OrderId}",
                    idempotencyKey, existingOrder.Id);

                return new CreateOrderResult(ToDto(existingOrder), IsNewOrder: false);
            }
        }

        var itemDrafts = command.Items
            .Select(item => new OrderItemDraft(item.ProductName, item.Quantity, item.UnitPrice))
            .ToList();

        var order = Order.Create(command.CustomerName, command.CustomerEmail, itemDrafts);

        await orderRepository.AddAsync(order, cancellationToken);
        await outboxWriter.AddOrderCreatedEventAsync(order.Id, cancellationToken);

        if (idempotencyKey is not null)
            await idempotencyRepository.AddAsync(IdempotencyRecord.Create(idempotencyKey, order.Id), cancellationToken);

        try
        {
            // Order + OutboxMessage (+ IdempotencyRecord, if a key was given) all go through this
            // single SaveChanges call, so they commit atomically. If RabbitMQ is down, this insert
            // still succeeds — the OutboxMessage just stays unprocessed until the Outbox Worker
            // (Phase 8) is able to publish it.
            await orderRepository.SaveChangesAsync(cancellationToken);
        }
        catch (IdempotencyKeyConflictException) when (idempotencyKey is not null)
        {
            // Lost the race: another request with the same key committed first between our
            // lookup above and this SaveChanges. The unique constraint is the real guard here;
            // the lookup was only a fast path to avoid this branch in the common case.
            logger.LogInformation(
                "Idempotency-Key {IdempotencyKey} lost the insert race. Returning the winning order.",
                idempotencyKey);

            var winningOrder = await FindOrderByIdempotencyKeyAsync(idempotencyKey, cancellationToken)
                ?? throw new InvalidOperationException(
                    $"Idempotency-Key '{idempotencyKey}' conflicted but no winning record was found.");

            return new CreateOrderResult(ToDto(winningOrder), IsNewOrder: false);
        }

        logger.LogInformation("Order created. OrderId: {OrderId}", order.Id);

        return new CreateOrderResult(ToDto(order), IsNewOrder: true);
    }

    private async Task<Order?> FindOrderByIdempotencyKeyAsync(string idempotencyKey, CancellationToken cancellationToken)
    {
        var record = await idempotencyRepository.FindByKeyAsync(idempotencyKey, cancellationToken);
        return record is null ? null : await orderRepository.GetByIdAsync(record.OrderId, cancellationToken);
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
