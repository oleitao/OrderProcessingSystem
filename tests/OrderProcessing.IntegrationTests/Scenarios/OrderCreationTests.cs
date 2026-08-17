using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OrderProcessing.Api.DTOs;
using OrderProcessing.Infrastructure.Database;
using OrderProcessing.IntegrationTests.Fixtures;

namespace OrderProcessing.IntegrationTests.Scenarios;

[Collection(IntegrationTestCollection.Name)]
public class OrderCreationTests(ContainersFixture containers) : IAsyncLifetime
{
    private OrderProcessingApiFactory _factory = null!;
    private HttpClient _client = null!;

    public Task InitializeAsync()
    {
        _factory = new OrderProcessingApiFactory(containers.Postgres, containers.RabbitMq);
        _client = _factory.CreateClient();
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    /// <summary>Test 1 (section 28): POST /orders → Order criada, OutboxMessage criada.</summary>
    [Fact]
    public async Task PostOrders_CreatesOrderAndOutboxMessage()
    {
        var request = new CreateOrderRequest(
            "Test 1 Customer", "test1@example.com",
            [new CreateOrderItemRequest("Keyboard", 1, 49.90m)]);

        var response = await _client.PostAsJsonAsync("/api/orders", request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<OrderResponse>();
        Assert.NotNull(body);
        Assert.Equal("Pending", body.Status);
        Assert.Equal(49.90m, body.TotalAmount);

        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<OrderDbContext>();

        var orderExists = await dbContext.Orders.AnyAsync(o => o.Id == body.Id);
        Assert.True(orderExists, "Order was not persisted.");

        var outboxMessageExists = await dbContext.OutboxMessages
            .AnyAsync(m => EF.Functions.JsonContains(m.Payload, $$"""{"orderId":"{{body.Id}}"}"""));
        Assert.True(outboxMessageExists, "OutboxMessage was not created for the new Order.");
    }

    /// <summary>Test 2 (section 28): mesmo Idempotency-Key duas vezes → uma única Order.</summary>
    [Fact]
    public async Task PostOrders_WithSameIdempotencyKeyTwice_CreatesOnlyOneOrder()
    {
        var idempotencyKey = $"test2-{Guid.NewGuid()}";
        var request = new CreateOrderRequest(
            "Test 2 Customer", "test2@example.com",
            [new CreateOrderItemRequest("Mouse", 1, 25.00m)]);

        using var firstRequest = new HttpRequestMessage(HttpMethod.Post, "/api/orders")
        {
            Content = JsonContent.Create(request)
        };
        firstRequest.Headers.Add("Idempotency-Key", idempotencyKey);
        var firstResponse = await _client.SendAsync(firstRequest);
        Assert.Equal(HttpStatusCode.Created, firstResponse.StatusCode);
        var firstBody = await firstResponse.Content.ReadFromJsonAsync<OrderResponse>();

        using var secondRequest = new HttpRequestMessage(HttpMethod.Post, "/api/orders")
        {
            Content = JsonContent.Create(request)
        };
        secondRequest.Headers.Add("Idempotency-Key", idempotencyKey);
        var secondResponse = await _client.SendAsync(secondRequest);
        Assert.Equal(HttpStatusCode.OK, secondResponse.StatusCode);
        var secondBody = await secondResponse.Content.ReadFromJsonAsync<OrderResponse>();

        Assert.Equal(firstBody!.Id, secondBody!.Id);

        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<OrderDbContext>();
        var matchingOrders = await dbContext.Orders.CountAsync(o => o.CustomerEmail == "test2@example.com");

        Assert.Equal(1, matchingOrders);
    }
}
