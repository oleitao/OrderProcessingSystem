using Microsoft.Extensions.Logging.Abstractions;
using OrderProcessing.Application.DTOs;
using OrderProcessing.Application.Services;
using OrderProcessing.Domain.Entities;
using OrderProcessing.UnitTests.Application.Fakes;

namespace OrderProcessing.UnitTests.Application;

public class OrderServiceTests
{
    private static readonly Guid ValidUserId = Guid.NewGuid();

    private static readonly CreateOrderItemCommand[] ValidItems = [new("Keyboard", 1, 49.90m)];

    private static OrderService CreateSut(
        FakeOrderRepository? orderRepository = null,
        FakeIdempotencyRepository? idempotencyRepository = null,
        FakeOutboxWriter? outboxWriter = null) =>
        new(
            orderRepository ?? new FakeOrderRepository(),
            idempotencyRepository ?? new FakeIdempotencyRepository(),
            outboxWriter ?? new FakeOutboxWriter(),
            NullLogger<OrderService>.Instance);

    [Fact]
    public async Task CreateOrderAsync_WithNoIdempotencyKey_CreatesNewOrderAndWritesOutboxEvent()
    {
        var outboxWriter = new FakeOutboxWriter();
        var sut = CreateSut(outboxWriter: outboxWriter);
        var command = new CreateOrderCommand(ValidUserId, "John Doe", "john@example.com", ValidItems);

        var result = await sut.CreateOrderAsync(command, CancellationToken.None);

        Assert.True(result.IsNewOrder);
        Assert.Equal("Pending", result.Order.Status);
        Assert.Equal(49.90m, result.Order.TotalAmount);
        Assert.Equal([result.Order.Id], outboxWriter.AddedForOrderIds);
    }

    [Fact]
    public async Task CreateOrderAsync_WithNewIdempotencyKey_CreatesOrder()
    {
        var sut = CreateSut();
        var command = new CreateOrderCommand(ValidUserId, "John Doe", "john@example.com", ValidItems, "fresh-key");

        var result = await sut.CreateOrderAsync(command, CancellationToken.None);

        Assert.True(result.IsNewOrder);
    }

    [Fact]
    public async Task CreateOrderAsync_WithAlreadyUsedIdempotencyKey_ReturnsExistingOrderWithoutCreatingAnother()
    {
        var orderRepository = new FakeOrderRepository();
        var idempotencyRepository = new FakeIdempotencyRepository();
        var outboxWriter = new FakeOutboxWriter();

        var existingOrder = Order.Create(ValidUserId, "Jane Doe", "jane@example.com", [new("Widget", 1, 10m)]);
        orderRepository.Seed(existingOrder);
        await idempotencyRepository.AddAsync(IdempotencyRecord.Create("dup-key", existingOrder.Id), CancellationToken.None);

        var sut = CreateSut(orderRepository, idempotencyRepository, outboxWriter);
        var command = new CreateOrderCommand(ValidUserId, "John Doe", "john@example.com", ValidItems, "dup-key");

        var result = await sut.CreateOrderAsync(command, CancellationToken.None);

        Assert.False(result.IsNewOrder);
        Assert.Equal(existingOrder.Id, result.Order.Id);
        Assert.Empty(outboxWriter.AddedForOrderIds);
        Assert.Equal(0, orderRepository.SaveChangesCallCount);
    }

    [Fact]
    public async Task CreateOrderAsync_WhenSaveChangesLosesIdempotencyRace_ReturnsTheWinningOrder()
    {
        var orderRepository = new FakeOrderRepository();
        var idempotencyRepository = new FakeIdempotencyRepository();

        var winningOrder = Order.Create(ValidUserId, "Winner", "winner@example.com", [new("Widget", 1, 10m)]);
        orderRepository.Seed(winningOrder);
        idempotencyRepository.RevealOnSecondFind = IdempotencyRecord.Create("race-key", winningOrder.Id);
        orderRepository.ThrowIdempotencyConflictOnNextSave = true;

        var sut = CreateSut(orderRepository, idempotencyRepository);
        var command = new CreateOrderCommand(ValidUserId, "Loser", "loser@example.com", ValidItems, "race-key");

        var result = await sut.CreateOrderAsync(command, CancellationToken.None);

        Assert.False(result.IsNewOrder);
        Assert.Equal(winningOrder.Id, result.Order.Id);
    }

    [Fact]
    public async Task GetOrderByIdAsync_WhenOrderDoesNotExist_ReturnsNull()
    {
        var sut = CreateSut();

        var result = await sut.GetOrderByIdAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetOrdersAsync_WithNullOwnerFilter_ReturnsAllSeededOrders()
    {
        var orderRepository = new FakeOrderRepository();
        orderRepository.Seed(Order.Create(ValidUserId, "A", "a@example.com", [new("Widget", 1, 1m)]));
        orderRepository.Seed(Order.Create(Guid.NewGuid(), "B", "b@example.com", [new("Widget", 1, 1m)]));

        var sut = CreateSut(orderRepository);

        var result = await sut.GetOrdersAsync(null, CancellationToken.None);

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public async Task GetOrdersAsync_WithOwnerFilter_ReturnsOnlyThatUsersOrders()
    {
        var orderRepository = new FakeOrderRepository();
        var ownedOrder = Order.Create(ValidUserId, "A", "a@example.com", [new("Widget", 1, 1m)]);
        orderRepository.Seed(ownedOrder);
        orderRepository.Seed(Order.Create(Guid.NewGuid(), "B", "b@example.com", [new("Widget", 1, 1m)]));

        var sut = CreateSut(orderRepository);

        var result = await sut.GetOrdersAsync(ValidUserId, CancellationToken.None);

        Assert.Equal([ownedOrder.Id], result.Select(order => order.Id));
    }

    [Fact]
    public async Task CancelOrderAsync_WhenOrderExists_CancelsAndReturnsDto()
    {
        var orderRepository = new FakeOrderRepository();
        var order = Order.Create(ValidUserId, "John Doe", "john@example.com", ValidItems.Select(i => new OrderItemDraft(i.ProductName, i.Quantity, i.UnitPrice)).ToList());
        orderRepository.Seed(order);

        var sut = CreateSut(orderRepository);

        var result = await sut.CancelOrderAsync(order.Id, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("Cancelled", result.Status);
        Assert.Equal(1, orderRepository.SaveChangesCallCount);
    }

    [Fact]
    public async Task CancelOrderAsync_WhenOrderDoesNotExist_ReturnsNull()
    {
        var sut = CreateSut();

        var result = await sut.CancelOrderAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Null(result);
    }
}
