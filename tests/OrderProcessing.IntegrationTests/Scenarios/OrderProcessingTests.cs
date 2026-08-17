using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OrderProcessing.Api.DTOs;
using OrderProcessing.Domain.Enums;
using OrderProcessing.Infrastructure.Database;
using OrderProcessing.IntegrationTests.Fixtures;

namespace OrderProcessing.IntegrationTests.Scenarios;

[Collection(IntegrationTestCollection.Name)]
public class OrderProcessingTests(ContainersFixture containers) : IAsyncLifetime
{
    private OrderProcessingApiFactory _apiFactory = null!;
    private HttpClient _client = null!;
    private OrderWorkerTestHost _worker = null!;

    public async Task InitializeAsync()
    {
        _apiFactory = new OrderProcessingApiFactory(containers.Postgres, containers.RabbitMq);
        _client = _apiFactory.CreateClient();

        _worker = new OrderWorkerTestHost(containers.Postgres, containers.RabbitMq);
        await _worker.StartAsync();
    }

    public async Task DisposeAsync()
    {
        await _worker.DisposeAsync();
        _client.Dispose();
        await _apiFactory.DisposeAsync();
    }

    /// <summary>
    /// Test 3 (section 28): OrderCreated processado → Pending → ... → Completed. The intermediate
    /// Processing state is real (StartProcessing runs before MarkAsCompleted) but, since Phase 11,
    /// both commit in a single SaveChanges — so it's never independently observable in the DB. We
    /// assert the reachable part of that contract: the Order ends up Completed.
    /// </summary>
    [Fact]
    public async Task OrderCreatedEvent_IsConsumedAndOrderBecomesCompleted()
    {
        var orderId = await CreateOrderAsync("Test 3 Customer", "test3@example.com");

        var finalStatus = await WaitForOrderStatusAsync(orderId, OrderStatus.Completed, TimeSpan.FromSeconds(15));

        Assert.Equal(OrderStatus.Completed, finalStatus);
    }

    /// <summary>Test 4 (section 28): mesmo EventId entregue de novo → não processado duas vezes.</summary>
    [Fact]
    public async Task DuplicateEventId_IsNotProcessedTwice()
    {
        var orderId = await CreateOrderAsync("Test 4 Customer", "test4@example.com");
        await WaitForOrderStatusAsync(orderId, OrderStatus.Completed, TimeSpan.FromSeconds(15));

        using var scope = _apiFactory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<OrderDbContext>();
        var updatedAtAfterFirstProcessing = (await dbContext.Orders.AsNoTracking().FirstAsync(o => o.Id == orderId))
            .UpdatedAtUtc;

        var eventId = await GetOutboxEventIdAsync(orderId);

        await PublishDuplicateOrderCreatedAsync(eventId, orderId);

        // Give the worker a moment to (not) reprocess it, then confirm nothing changed.
        await Task.Delay(TimeSpan.FromSeconds(3));

        await using var freshScope = _apiFactory.Services.CreateAsyncScope();
        var freshDbContext = freshScope.ServiceProvider.GetRequiredService<OrderDbContext>();

        var processedMessageCount = await freshDbContext.ProcessedMessages.CountAsync(m => m.MessageId == eventId);
        Assert.Equal(1, processedMessageCount);

        var order = await freshDbContext.Orders.AsNoTracking().FirstAsync(o => o.Id == orderId);
        Assert.Equal(OrderStatus.Completed, order.Status);
        Assert.Equal(updatedAtAfterFirstProcessing, order.UpdatedAtUtc);
    }

    private async Task<Guid> CreateOrderAsync(string customerName, string customerEmail)
    {
        var request = new CreateOrderRequest(customerName, customerEmail, [new CreateOrderItemRequest("Widget", 1, 15.00m)]);
        var response = await _client.PostAsJsonAsync("/api/orders", request);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<OrderResponse>();
        return body!.Id;
    }

    private async Task<Guid> GetOutboxEventIdAsync(Guid orderId)
    {
        using var scope = _apiFactory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<OrderDbContext>();

        var message = await dbContext.OutboxMessages
            .AsNoTracking()
            .FirstAsync(m => EF.Functions.JsonContains(m.Payload, $$"""{"orderId":"{{orderId}}"}"""));

        using var document = System.Text.Json.JsonDocument.Parse(message.Payload);
        return document.RootElement.GetProperty("eventId").GetGuid();
    }

    private async Task PublishDuplicateOrderCreatedAsync(Guid eventId, Guid orderId)
    {
        // Publish straight to the exchange the Outbox Worker uses — reproduces exactly what a
        // RabbitMQ redelivery looks like from the consumer's point of view.
        var factory = new RabbitMQ.Client.ConnectionFactory
        {
            HostName = containers.RabbitMq.Hostname,
            Port = containers.RabbitMq.GetMappedPublicPort(5672),
            UserName = ContainersFixture.RabbitMqUser,
            Password = ContainersFixture.RabbitMqPassword
        };

        await using var connection = await factory.CreateConnectionAsync();
        await using var channel = await connection.CreateChannelAsync();

        var payload = System.Text.Json.JsonSerializer.Serialize(new
        {
            eventId,
            orderId,
            createdAtUtc = DateTime.UtcNow
        });

        await channel.BasicPublishAsync(
            exchange: "orders.exchange",
            routingKey: "order.created",
            mandatory: false,
            basicProperties: new RabbitMQ.Client.BasicProperties { Persistent = true, ContentType = "application/json" },
            body: System.Text.Encoding.UTF8.GetBytes(payload));
    }

    private async Task<OrderStatus> WaitForOrderStatusAsync(Guid orderId, OrderStatus expected, TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);

        while (!cts.IsCancellationRequested)
        {
            await using var scope = _apiFactory.Services.CreateAsyncScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<OrderDbContext>();
            var status = await dbContext.Orders.AsNoTracking()
                .Where(o => o.Id == orderId)
                .Select(o => o.Status)
                .FirstAsync(cts.Token);

            if (status == expected)
                return status;

            await Task.Delay(TimeSpan.FromMilliseconds(250), cts.Token);
        }

        throw new TimeoutException($"Order {orderId} did not reach status {expected} within {timeout}.");
    }
}
