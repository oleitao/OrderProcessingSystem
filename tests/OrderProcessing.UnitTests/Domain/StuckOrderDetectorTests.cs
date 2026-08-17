using OrderProcessing.Domain.Enums;
using OrderProcessing.Domain.Rules;

namespace OrderProcessing.UnitTests.Domain;

public class StuckOrderDetectorTests
{
    private static readonly DateTime Now = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
    private static readonly TimeSpan Threshold = TimeSpan.FromMinutes(5);

    [Fact]
    public void IsStuck_WhenProcessingAndOlderThanThreshold_ReturnsTrue()
    {
        var updatedAtUtc = Now - TimeSpan.FromMinutes(10);

        Assert.True(StuckOrderDetector.IsStuck(OrderStatus.Processing, updatedAtUtc, Now, Threshold));
    }

    [Fact]
    public void IsStuck_WhenProcessingButWithinThreshold_ReturnsFalse()
    {
        var updatedAtUtc = Now - TimeSpan.FromMinutes(1);

        Assert.False(StuckOrderDetector.IsStuck(OrderStatus.Processing, updatedAtUtc, Now, Threshold));
    }

    [Theory]
    [InlineData(OrderStatus.Pending)]
    [InlineData(OrderStatus.Completed)]
    [InlineData(OrderStatus.Failed)]
    [InlineData(OrderStatus.Cancelled)]
    public void IsStuck_WhenNotProcessing_ReturnsFalse(OrderStatus status)
    {
        var updatedAtUtc = Now - TimeSpan.FromMinutes(10);

        Assert.False(StuckOrderDetector.IsStuck(status, updatedAtUtc, Now, Threshold));
    }

    [Fact]
    public void IsStuck_WhenUpdatedAtUtcIsNull_ReturnsFalse()
    {
        Assert.False(StuckOrderDetector.IsStuck(OrderStatus.Processing, null, Now, Threshold));
    }
}
