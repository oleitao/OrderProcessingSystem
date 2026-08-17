using OrderProcessing.Domain.Enums;
using OrderProcessing.Domain.Rules;

namespace OrderProcessing.Domain.Entities;

public sealed class Order
{
    private readonly List<OrderItem> _items = new();

    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public string CustomerName { get; private set; } = string.Empty;
    public string CustomerEmail { get; private set; } = string.Empty;
    public OrderStatus Status { get; private set; }
    public decimal TotalAmount { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? UpdatedAtUtc { get; private set; }

    public IReadOnlyCollection<OrderItem> Items => _items.AsReadOnly();

    // Required by EF Core for materialization; kept private so orders can only be built through Create.
    private Order() { }

    public static Order Create(Guid userId, string customerName, string customerEmail, IReadOnlyCollection<OrderItemDraft> items)
    {
        if (userId == Guid.Empty)
            throw new ArgumentException("UserId is required.", nameof(userId));

        if (items is null || items.Count == 0)
            throw new ArgumentException("Order must contain at least one item.", nameof(items));

        if (string.IsNullOrWhiteSpace(customerName))
            throw new ArgumentException("Customer name is required.", nameof(customerName));

        if (string.IsNullOrWhiteSpace(customerEmail) || !OrderValidationRules.IsValidEmail(customerEmail))
            throw new ArgumentException("Customer email is invalid.", nameof(customerEmail));

        var order = new Order
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            CustomerName = customerName.Trim(),
            CustomerEmail = customerEmail.Trim(),
            Status = OrderStatus.Pending,
            CreatedAtUtc = DateTime.UtcNow
        };

        foreach (var draft in items)
            order._items.Add(OrderItem.Create(order.Id, draft.ProductName, draft.Quantity, draft.UnitPrice));

        order.TotalAmount = order._items.Sum(item => item.LineTotal);

        return order;
    }

    public void Cancel()
    {
        if (Status == OrderStatus.Completed)
            throw new InvalidOperationException($"Order {Id} is already Completed and cannot be cancelled.");

        if (Status == OrderStatus.Cancelled)
            return;

        Status = OrderStatus.Cancelled;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    public void StartProcessing()
    {
        if (Status == OrderStatus.Cancelled)
            throw new InvalidOperationException($"Order {Id} is Cancelled and cannot be processed.");

        if (Status != OrderStatus.Pending)
            throw new InvalidOperationException($"Order {Id} cannot start processing from status {Status}.");

        Status = OrderStatus.Processing;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    public void MarkAsCompleted()
    {
        if (Status != OrderStatus.Processing)
            throw new InvalidOperationException($"Order {Id} cannot be completed from status {Status}.");

        Status = OrderStatus.Completed;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    public void MarkAsFailed()
    {
        if (Status != OrderStatus.Processing)
            throw new InvalidOperationException($"Order {Id} cannot be marked as failed from status {Status}.");

        Status = OrderStatus.Failed;
        UpdatedAtUtc = DateTime.UtcNow;
    }
}
