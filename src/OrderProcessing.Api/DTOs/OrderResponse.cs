namespace OrderProcessing.Api.DTOs;

public sealed record OrderItemResponse(Guid Id, string ProductName, int Quantity, decimal UnitPrice, decimal LineTotal);

public sealed record OrderResponse(
    Guid Id,
    Guid UserId,
    string CustomerName,
    string CustomerEmail,
    string Status,
    decimal TotalAmount,
    DateTime CreatedAtUtc,
    DateTime? UpdatedAtUtc,
    IReadOnlyCollection<OrderItemResponse> Items);
