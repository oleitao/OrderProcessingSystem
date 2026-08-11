using Microsoft.AspNetCore.Mvc;
using OrderProcessing.Api.DTOs;
using OrderProcessing.Application.DTOs;
using OrderProcessing.Application.Interfaces;

namespace OrderProcessing.Api.Controllers;

[ApiController]
[Route("api/orders")]
public sealed class OrdersController(IOrderService orderService) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType(typeof(OrderResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<OrderResponse>> CreateOrder(
        CreateOrderRequest request,
        CancellationToken cancellationToken)
    {
        var command = new CreateOrderCommand(
            request.CustomerName,
            request.CustomerEmail,
            request.Items
                .Select(item => new CreateOrderItemCommand(item.ProductName, item.Quantity, item.UnitPrice))
                .ToList());

        var order = await orderService.CreateOrderAsync(command, cancellationToken);

        return CreatedAtAction(nameof(GetOrderById), new { id = order.Id }, ToResponse(order));
    }

    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<OrderResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<OrderResponse>>> GetOrders(CancellationToken cancellationToken)
    {
        var orders = await orderService.GetOrdersAsync(cancellationToken);

        return Ok(orders.Select(ToResponse).ToList());
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(OrderResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<OrderResponse>> GetOrderById(Guid id, CancellationToken cancellationToken)
    {
        var order = await orderService.GetOrderByIdAsync(id, cancellationToken);

        return order is null ? NotFound() : Ok(ToResponse(order));
    }

    [HttpPatch("{id:guid}/cancel")]
    [ProducesResponseType(typeof(OrderResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<OrderResponse>> CancelOrder(Guid id, CancellationToken cancellationToken)
    {
        var order = await orderService.CancelOrderAsync(id, cancellationToken);

        return order is null ? NotFound() : Ok(ToResponse(order));
    }

    private static OrderResponse ToResponse(OrderDto order) => new(
        order.Id,
        order.CustomerName,
        order.CustomerEmail,
        order.Status,
        order.TotalAmount,
        order.CreatedAtUtc,
        order.UpdatedAtUtc,
        order.Items
            .Select(item => new OrderItemResponse(item.Id, item.ProductName, item.Quantity, item.UnitPrice, item.LineTotal))
            .ToList());
}
