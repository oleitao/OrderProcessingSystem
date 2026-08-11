namespace OrderProcessing.Application.DTOs;

public sealed record OrderItemDto(Guid Id, string ProductName, int Quantity, decimal UnitPrice, decimal LineTotal);

public sealed record OrderDto(
    Guid Id,
    string CustomerName,
    string CustomerEmail,
    string Status,
    decimal TotalAmount,
    DateTime CreatedAtUtc,
    DateTime? UpdatedAtUtc,
    IReadOnlyCollection<OrderItemDto> Items);
