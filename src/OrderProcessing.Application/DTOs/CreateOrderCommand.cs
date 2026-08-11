namespace OrderProcessing.Application.DTOs;

public sealed record CreateOrderItemCommand(string ProductName, int Quantity, decimal UnitPrice);

public sealed record CreateOrderCommand(
    string CustomerName,
    string CustomerEmail,
    IReadOnlyCollection<CreateOrderItemCommand> Items);
