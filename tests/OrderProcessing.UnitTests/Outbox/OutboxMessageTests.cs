using OrderProcessing.Infrastructure.Outbox;

namespace OrderProcessing.UnitTests.Outbox;

public class OutboxMessageTests
{
    [Fact]
    public void Create_WithValidData_SetsFieldsAndLeavesUnprocessed()
    {
        var message = OutboxMessage.Create("OrderCreated", "{\"orderId\":\"...\"}");

        Assert.Equal("OrderCreated", message.Type);
        Assert.Null(message.ProcessedAtUtc);
        Assert.Equal(0, message.RetryCount);
        Assert.Null(message.LastError);
    }

    [Fact]
    public void MarkAsProcessed_SetsProcessedAtUtc()
    {
        var message = OutboxMessage.Create("OrderCreated", "{}");

        message.MarkAsProcessed();

        Assert.NotNull(message.ProcessedAtUtc);
    }

    [Fact]
    public void RecordFailure_IncrementsRetryCountAndStoresError()
    {
        var message = OutboxMessage.Create("OrderCreated", "{}");

        message.RecordFailure("RabbitMQ unavailable");
        message.RecordFailure("RabbitMQ unavailable");

        Assert.Equal(2, message.RetryCount);
        Assert.Equal("RabbitMQ unavailable", message.LastError);
    }

    [Theory]
    [InlineData("", "{}")]
    [InlineData(null, "{}")]
    [InlineData("OrderCreated", "")]
    [InlineData("OrderCreated", null)]
    public void Create_WithMissingTypeOrPayload_Throws(string? type, string? payload)
    {
        Assert.Throws<ArgumentException>(() => OutboxMessage.Create(type!, payload!));
    }
}
