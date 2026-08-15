using OrderProcessing.Domain.Entities;

namespace OrderProcessing.UnitTests.Domain;

public class IdempotencyRecordTests
{
    [Fact]
    public void Create_WithValidKey_SetsFields()
    {
        var orderId = Guid.NewGuid();

        var record = IdempotencyRecord.Create("abc-123", orderId);

        Assert.Equal("abc-123", record.Key);
        Assert.Equal(orderId, record.OrderId);
        Assert.NotEqual(Guid.Empty, record.Id);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Create_WithMissingKey_Throws(string? key)
    {
        Assert.Throws<ArgumentException>(() => IdempotencyRecord.Create(key!, Guid.NewGuid()));
    }
}
