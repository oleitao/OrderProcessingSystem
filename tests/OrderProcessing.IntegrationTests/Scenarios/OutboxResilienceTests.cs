using System.Globalization;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OrderProcessing.Api.DTOs;
using OrderProcessing.Infrastructure.Database;
using OrderProcessing.Infrastructure.Outbox;
using OrderProcessing.IntegrationTests.Fixtures;

namespace OrderProcessing.IntegrationTests.Scenarios;

/// <summary>
/// Simulates "RabbitMQ unreachable" by pointing the Api at a closed local port instead of
/// stopping/restarting the shared RabbitMQ container: Testcontainers reassigns a *new* random
/// host port on every container restart (confirmed empirically — this is standard Docker behavior
/// for dynamic port bindings, not something the app or this test controls), which would silently
/// invalidate the connection info the Api factory already captured at startup. Pointing at a
/// closed port gives the same "unreachable" behavior deterministically, without touching the real
/// container's lifecycle (and risking it) at all.
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public class OutboxResilienceTests(ContainersFixture containers) : IAsyncLifetime
{
    private OrderProcessingApiFactory _unreachableApiFactory = null!;
    private HttpClient _client = null!;

    public Task InitializeAsync()
    {
        _unreachableApiFactory = new OrderProcessingApiFactory(
            containers.Postgres, containers.RabbitMq, rabbitMqPortOverride: GetClosedLocalPort());
        _client = _unreachableApiFactory.CreateClient();
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _unreachableApiFactory.DisposeAsync();
    }

    /// <summary>
    /// Tests 5 and 6 (section 28), as one continuous scenario since 6 only makes sense following 5:
    /// with RabbitMQ unreachable, POST /orders still succeeds and the OutboxMessage stays
    /// unprocessed; once RabbitMQ is reachable again, the Outbox Worker's next poll publishes it
    /// without any retry from the client. This is the whole point of the Outbox Pattern (Phase
    /// 6/8) made observable.
    /// </summary>
    [Fact]
    public async Task WhenRabbitMqIsDown_OrderPersistsAndOutboxMessageStaysPending_ThenPublishesOnceItReturns()
    {
        var request = new CreateOrderRequest(
            "Test 5 Customer", "test5@example.com",
            [new CreateOrderItemRequest("Router", 1, 89.90m)]);

        var response = await _client.PostAsJsonAsync("/api/orders", request);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<OrderResponse>();

        // Give the Outbox Worker a few polls' worth of time to prove it really can't publish.
        await Task.Delay(TimeSpan.FromSeconds(3));

        await using (var scope = _unreachableApiFactory.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<OrderDbContext>();

            var orderExists = await dbContext.Orders.AnyAsync(o => o.Id == body!.Id);
            Assert.True(orderExists, "Order must persist even though RabbitMQ is unreachable.");

            var outboxMessage = await dbContext.OutboxMessages
                .FirstAsync(m => EF.Functions.JsonContains(m.Payload, $$"""{"orderId":"{{body!.Id}}"}"""));
            Assert.Null(outboxMessage.ProcessedAtUtc);
            Assert.True(outboxMessage.RetryCount > 0, "Outbox Worker should have recorded failed publish attempts.");
        }

        // "RabbitMQ comes back" — a fresh Api host pointed at the real, working broker, sharing the
        // same Postgres, so its own Outbox Worker sees the exact same still-pending OutboxMessage.
        await using var recoveredApiFactory = new OrderProcessingApiFactory(containers.Postgres, containers.RabbitMq);
        _ = recoveredApiFactory.Services; // forces host startup (migrations are a no-op here)

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        OutboxMessage? published = null;

        while (!cts.IsCancellationRequested)
        {
            await using var scope = recoveredApiFactory.Services.CreateAsyncScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<OrderDbContext>();
            var message = await dbContext.OutboxMessages
                .AsNoTracking()
                .FirstAsync(m => EF.Functions.JsonContains(m.Payload, $$"""{"orderId":"{{body!.Id}}"}"""), cts.Token);

            if (message.ProcessedAtUtc is not null)
            {
                published = message;
                break;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(250), cts.Token);
        }

        Assert.NotNull(published);
        Assert.NotNull(published.ProcessedAtUtc);
    }

    /// <summary>A port on localhost nothing is listening on, so connecting to it fails immediately.</summary>
    private static int GetClosedLocalPort()
    {
        using var socket = new System.Net.Sockets.Socket(
            System.Net.Sockets.AddressFamily.InterNetwork,
            System.Net.Sockets.SocketType.Stream,
            System.Net.Sockets.ProtocolType.Tcp);
        socket.Bind(new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, 0));
        var port = ((System.Net.IPEndPoint)socket.LocalEndPoint!).Port;
        // Socket closes (and releases the port) when this method returns — nothing will be
        // listening on it, which is exactly the "connection refused" behavior this test wants.
        return port;
    }
}
