using System.Net.Http.Json;
using OrderProcessing.Api.DTOs;
using OrderProcessing.IntegrationTests.Fixtures;

namespace OrderProcessing.IntegrationTests.Scenarios;

[Collection(IntegrationTestCollection.Name)]
public class RetryAndDlqTests(ContainersFixture containers) : IAsyncLifetime
{
    private OrderProcessingApiFactory _apiFactory = null!;
    private HttpClient _client = null!;
    private RabbitMqTestHelper _rabbitMqHelper = null!;

    public async Task InitializeAsync()
    {
        _apiFactory = new OrderProcessingApiFactory(containers.Postgres, containers.RabbitMq);
        _client = _apiFactory.CreateClient();
        _rabbitMqHelper = new RabbitMqTestHelper(containers.RabbitMq);

        // Both tests here assert on exact queue depths, so they need a clean slate — otherwise a
        // message left behind by an earlier test in this collection would be miscounted as this
        // test's own.
        await _rabbitMqHelper.PurgeAllTestQueuesAsync();
    }

    public async Task DisposeAsync()
    {
        await _rabbitMqHelper.DisposeAsync();
        _client.Dispose();
        await _apiFactory.DisposeAsync();
    }

    /// <summary>Test 7 (section 28): processamento falha → mensagem vai para a retry queue.</summary>
    [Fact]
    public async Task WhenProcessingFails_MessageIsRoutedToFirstRetryQueue()
    {
        await using var worker = new OrderWorkerTestHost(containers.Postgres, containers.RabbitMq, failureProbability: 1.0);
        await worker.StartAsync();

        var request = new CreateOrderRequest(
            "Test 7 Customer", "test7@example.com",
            [new CreateOrderItemRequest("Widget", 1, 12.00m)]);
        var response = await _client.PostAsJsonAsync("/api/orders", request);
        response.EnsureSuccessStatusCode();

        // orders.retry.1 has a 5s TTL — poll well inside that window so we catch the message
        // before it naturally cycles back to orders.processing for attempt 2.
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(4));
        uint retryQueueDepth = 0;

        while (!cts.IsCancellationRequested && retryQueueDepth == 0)
        {
            retryQueueDepth = await _rabbitMqHelper.GetMessageCountAsync("orders.retry.1");
            if (retryQueueDepth == 0)
                await Task.Delay(TimeSpan.FromMilliseconds(200), cts.Token);
        }

        Assert.Equal(1u, retryQueueDepth);
    }

    /// <summary>
    /// Test 8 (section 28): processamento falha continuamente → esgota os 3 retries (5s+15s+45s)
    /// e a mensagem acaba na DLQ. This test genuinely waits out the real TTL cascade — matches the
    /// same ~65s manual test used to validate Phases 12/13, just automated.
    /// </summary>
    [Fact]
    public async Task WhenProcessingFailsContinuously_MessageEndsUpInDlq()
    {
        await using var worker = new OrderWorkerTestHost(containers.Postgres, containers.RabbitMq, failureProbability: 1.0);
        await worker.StartAsync();

        var request = new CreateOrderRequest(
            "Test 8 Customer", "test8@example.com",
            [new CreateOrderItemRequest("Widget", 1, 12.00m)]);
        var response = await _client.PostAsJsonAsync("/api/orders", request);
        response.EnsureSuccessStatusCode();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(80));
        uint dlqDepth = 0;

        while (!cts.IsCancellationRequested && dlqDepth == 0)
        {
            dlqDepth = await _rabbitMqHelper.GetMessageCountAsync("orders.dlq");
            if (dlqDepth == 0)
                await Task.Delay(TimeSpan.FromSeconds(1), cts.Token);
        }

        Assert.Equal(1u, dlqDepth);

        // Every retry queue and the main queue should be empty by now — nothing left orphaned mid-cascade.
        Assert.Equal(0u, await _rabbitMqHelper.GetMessageCountAsync("orders.processing"));
        Assert.Equal(0u, await _rabbitMqHelper.GetMessageCountAsync("orders.retry.1"));
        Assert.Equal(0u, await _rabbitMqHelper.GetMessageCountAsync("orders.retry.2"));
        Assert.Equal(0u, await _rabbitMqHelper.GetMessageCountAsync("orders.retry.3"));
    }
}
