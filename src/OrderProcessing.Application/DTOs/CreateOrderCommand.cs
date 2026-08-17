namespace OrderProcessing.Application.DTOs;

public sealed record CreateOrderItemCommand(string ProductName, int Quantity, decimal UnitPrice);

public sealed record CreateOrderCommand(
    Guid UserId,
    string CustomerName,
    string CustomerEmail,
    IReadOnlyCollection<CreateOrderItemCommand> Items,
    string? IdempotencyKey = null);

public sealed record CreateOrderResult(OrderDto Order, bool IsNewOrder);
