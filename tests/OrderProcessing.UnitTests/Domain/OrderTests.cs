using OrderProcessing.Domain.Entities;
using OrderProcessing.Domain.Enums;

namespace OrderProcessing.UnitTests.Domain;

public class OrderTests
{
    private static readonly OrderItemDraft[] ValidItems =
    [
        new("Keyboard", 2, 49.90m),
        new("Mouse", 1, 25.10m)
    ];

    [Fact]
    public void Create_WithValidData_CalculatesTotalAmountFromAllItems()
    {
        var order = Order.Create("John Doe", "john@example.com", ValidItems);

        Assert.Equal(124.90m, order.TotalAmount);
        Assert.Equal(OrderStatus.Pending, order.Status);
        Assert.Equal(2, order.Items.Count);
        Assert.Null(order.UpdatedAtUtc);
    }

    [Fact]
    public void Create_WithNoItems_Throws()
    {
        Assert.Throws<ArgumentException>(() => Order.Create("John Doe", "john@example.com", []));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Create_WithMissingCustomerName_Throws(string? customerName)
    {
        Assert.Throws<ArgumentException>(() => Order.Create(customerName!, "john@example.com", ValidItems));
    }

    [Theory]
    [InlineData("not-an-email")]
    [InlineData("missing-at.com")]
    [InlineData("")]
    public void Create_WithInvalidEmail_Throws(string customerEmail)
    {
        Assert.Throws<ArgumentException>(() => Order.Create("John Doe", customerEmail, ValidItems));
    }

    [Fact]
    public void Create_WithZeroQuantity_Throws()
    {
        OrderItemDraft[] items = [new("Keyboard", 0, 49.90m)];

        Assert.Throws<ArgumentOutOfRangeException>(() => Order.Create("John Doe", "john@example.com", items));
    }

    [Fact]
    public void Create_WithZeroUnitPrice_Throws()
    {
        OrderItemDraft[] items = [new("Keyboard", 1, 0m)];

        Assert.Throws<ArgumentOutOfRangeException>(() => Order.Create("John Doe", "john@example.com", items));
    }

    [Fact]
    public void Cancel_WhenPending_TransitionsToCancelled()
    {
        var order = Order.Create("John Doe", "john@example.com", ValidItems);

        order.Cancel();

        Assert.Equal(OrderStatus.Cancelled, order.Status);
        Assert.NotNull(order.UpdatedAtUtc);
    }

    [Fact]
    public void Cancel_WhenAlreadyCancelled_IsIdempotent()
    {
        var order = Order.Create("John Doe", "john@example.com", ValidItems);
        order.Cancel();
        var firstCancelTimestamp = order.UpdatedAtUtc;

        order.Cancel();

        Assert.Equal(OrderStatus.Cancelled, order.Status);
        Assert.Equal(firstCancelTimestamp, order.UpdatedAtUtc);
    }

    [Fact]
    public void Cancel_WhenCompleted_Throws()
    {
        var order = Order.Create("John Doe", "john@example.com", ValidItems);
        order.StartProcessing();
        order.MarkAsCompleted();

        Assert.Throws<InvalidOperationException>(order.Cancel);
    }

    [Fact]
    public void StartProcessing_WhenCancelled_Throws()
    {
        var order = Order.Create("John Doe", "john@example.com", ValidItems);
        order.Cancel();

        Assert.Throws<InvalidOperationException>(order.StartProcessing);
    }

    [Fact]
    public void StartProcessing_WhenPending_TransitionsToProcessing()
    {
        var order = Order.Create("John Doe", "john@example.com", ValidItems);

        order.StartProcessing();

        Assert.Equal(OrderStatus.Processing, order.Status);
    }

    [Fact]
    public void MarkAsCompleted_WhenPending_Throws()
    {
        var order = Order.Create("John Doe", "john@example.com", ValidItems);

        Assert.Throws<InvalidOperationException>(order.MarkAsCompleted);
    }

    [Fact]
    public void MarkAsFailed_WhenProcessing_TransitionsToFailed()
    {
        var order = Order.Create("John Doe", "john@example.com", ValidItems);
        order.StartProcessing();

        order.MarkAsFailed();

        Assert.Equal(OrderStatus.Failed, order.Status);
    }
}
