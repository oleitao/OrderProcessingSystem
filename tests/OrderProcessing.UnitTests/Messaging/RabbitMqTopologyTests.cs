using OrderProcessing.Infrastructure.Messaging;

namespace OrderProcessing.UnitTests.Messaging;

public class RabbitMqTopologyTests
{
    [Fact]
    public void MaxRetries_MatchesNumberOfConfiguredRetryQueues()
    {
        Assert.Equal(3, RabbitMqTopology.MaxRetries);
        Assert.Equal(RabbitMqTopology.RetryQueues.Count, RabbitMqTopology.MaxRetries);
    }

    [Theory]
    [InlineData(1, RabbitMqTopology.OrdersRetryQueue1)]
    [InlineData(2, RabbitMqTopology.OrdersRetryQueue2)]
    [InlineData(3, RabbitMqTopology.OrdersRetryQueue3)]
    public void GetRetryQueueName_WithValidAttempt_ReturnsExpectedQueue(int attempt, string expectedQueue)
    {
        Assert.Equal(expectedQueue, RabbitMqTopology.GetRetryQueueName(attempt));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    public void GetRetryQueueName_WithAttemptOutsideRange_Throws(int attempt)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => RabbitMqTopology.GetRetryQueueName(attempt));
    }

    [Fact]
    public void GetRetryCount_WithNoHeaders_ReturnsZero()
    {
        Assert.Equal(0, RabbitMqTopology.GetRetryCount(null));
    }

    [Fact]
    public void GetRetryCount_WithoutRetryCountHeader_ReturnsZero()
    {
        var headers = new Dictionary<string, object?> { ["some-other-header"] = "value" };

        Assert.Equal(0, RabbitMqTopology.GetRetryCount(headers));
    }

    [Fact]
    public void GetRetryCount_WithIntHeader_ReturnsValue()
    {
        var headers = new Dictionary<string, object?> { [RabbitMqTopology.RetryCountHeader] = 2 };

        Assert.Equal(2, RabbitMqTopology.GetRetryCount(headers));
    }

    [Fact]
    public void GetRetryCount_WithLongHeader_ReturnsValue()
    {
        // RabbitMQ.Client commonly round-trips integer header values as long, not int.
        var headers = new Dictionary<string, object?> { [RabbitMqTopology.RetryCountHeader] = 3L };

        Assert.Equal(3, RabbitMqTopology.GetRetryCount(headers));
    }
}
