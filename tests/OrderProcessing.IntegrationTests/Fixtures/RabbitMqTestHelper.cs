using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;
using Testcontainers.RabbitMq;

namespace OrderProcessing.IntegrationTests.Fixtures;

/// <summary>
/// Direct AMQP access to the test RabbitMQ container for assertions the application's own code
/// has no reason to expose (queue depths, purging between tests) — deliberately independent of
/// IRabbitMqConnectionFactory so these tests aren't just re-exercising the same code they verify.
/// </summary>
public sealed class RabbitMqTestHelper(RabbitMqContainer rabbitMq) : IAsyncDisposable
{
    private IConnection? _connection;
    private IChannel? _channel;

    private async Task<IChannel> GetChannelAsync()
    {
        if (_channel is not null)
            return _channel;

        var factory = new ConnectionFactory
        {
            HostName = rabbitMq.Hostname,
            Port = rabbitMq.GetMappedPublicPort(5672),
            UserName = ContainersFixture.RabbitMqUser,
            Password = ContainersFixture.RabbitMqPassword
        };

        _connection = await factory.CreateConnectionAsync();
        _channel = await _connection.CreateChannelAsync();
        return _channel;
    }

    public async Task<uint> GetMessageCountAsync(string queueName)
    {
        var channel = await GetChannelAsync();

        try
        {
            var result = await channel.QueueDeclarePassiveAsync(queueName);
            return result.MessageCount;
        }
        catch (OperationInterruptedException)
        {
            // Queue doesn't exist yet (topology not declared by any host in this test) — treat as empty.
            _channel = null;
            return 0;
        }
    }

    public async Task PurgeAllTestQueuesAsync()
    {
        var channel = await GetChannelAsync();

        foreach (var queue in new[]
                 {
                     "orders.processing", "orders.retry.1", "orders.retry.2", "orders.retry.3", "orders.dlq"
                 })
        {
            try
            {
                await channel.QueuePurgeAsync(queue);
            }
            catch (OperationInterruptedException)
            {
                // Queue not declared yet — nothing to purge.
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_channel is not null)
            await _channel.CloseAsync();

        if (_connection is not null)
            await _connection.CloseAsync();
    }
}
