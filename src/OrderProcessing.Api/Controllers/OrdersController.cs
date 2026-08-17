using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OrderProcessing.Api.Authorization;
using OrderProcessing.Api.DTOs;
using OrderProcessing.Application.DTOs;
using OrderProcessing.Application.Interfaces;

namespace OrderProcessing.Api.Controllers;

[ApiController]
[Route("api/orders")]
[Authorize]
public sealed class OrdersController(IOrderService orderService) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType(typeof(OrderResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(OrderResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<OrderResponse>> CreateOrder(
        CreateOrderRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        var command = new CreateOrderCommand(
            GetUserId(),
            request.CustomerName,
            request.CustomerEmail,
            request.Items
                .Select(item => new CreateOrderItemCommand(item.ProductName, item.Quantity, item.UnitPrice))
                .ToList(),
            idempotencyKey);

        var result = await orderService.CreateOrderAsync(command, cancellationToken);

        // A replayed Idempotency-Key returns the order that already exists (200), instead of
        // pretending to create a new resource (201) a second time.
        if (!result.IsNewOrder)
            return Ok(ToResponse(result.Order));

        return CreatedAtAction(nameof(GetOrderById), new { id = result.Order.Id }, ToResponse(result.Order));
    }

    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<OrderResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<OrderResponse>>> GetOrders(CancellationToken cancellationToken)
    {
        // Admin sees every order; everyone else only ever sees their own.
        var ownerFilter = IsAdmin() ? null : (Guid?)GetUserId();
        var orders = await orderService.GetOrdersAsync(ownerFilter, cancellationToken);

        return Ok(orders.Select(ToResponse).ToList());
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(OrderResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<OrderResponse>> GetOrderById(Guid id, CancellationToken cancellationToken)
    {
        var order = await orderService.GetOrderByIdAsync(id, cancellationToken);
        if (order is null)
            return NotFound();

        if (!CanAccess(order))
            return Forbid();

        return Ok(ToResponse(order));
    }

    [HttpPatch("{id:guid}/cancel")]
    [ProducesResponseType(typeof(OrderResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<OrderResponse>> CancelOrder(Guid id, CancellationToken cancellationToken)
    {
        // Ownership must be checked before mutating — fetched once here for that check, and the
        // actual cancel below re-fetches inside OrderService (which stays HTTP/claims-agnostic).
        var existingOrder = await orderService.GetOrderByIdAsync(id, cancellationToken);
        if (existingOrder is null)
            return NotFound();

        if (!CanAccess(existingOrder))
            return Forbid();

        var order = await orderService.CancelOrderAsync(id, cancellationToken);

        return order is null ? NotFound() : Ok(ToResponse(order));
    }

    private Guid GetUserId()
    {
        var subject = User.FindFirstValue(JwtRegisteredClaimNames.Sub) ?? User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw new InvalidOperationException("Authenticated request is missing a 'sub' claim.");

        return Guid.Parse(subject);
    }

    private bool IsAdmin() => User.IsInRole(Roles.Admin);

    private bool CanAccess(OrderDto order) => IsAdmin() || order.UserId == GetUserId();

    private static OrderResponse ToResponse(OrderDto order) => new(
        order.Id,
        order.UserId,
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
