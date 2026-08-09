namespace OrderProcessing.Domain.Entities;

public sealed record OrderItemDraft(string ProductName, int Quantity, decimal UnitPrice);
