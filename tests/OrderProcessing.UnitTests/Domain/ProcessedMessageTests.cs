using OrderProcessing.Domain.Entities;

namespace OrderProcessing.UnitTests.Domain;

public class ProcessedMessageTests
{
    [Fact]
    public void Create_WithValidMessageId_SetsFields()
    {
        var messageId = Guid.NewGuid();

        var record = ProcessedMessage.Create(messageId);

        Assert.Equal(messageId, record.MessageId);
        Assert.True(record.ProcessedAtUtc <= DateTime.UtcNow);
    }

    [Fact]
    public void Create_WithEmptyMessageId_Throws()
    {
        Assert.Throws<ArgumentException>(() => ProcessedMessage.Create(Guid.Empty));
    }
}
