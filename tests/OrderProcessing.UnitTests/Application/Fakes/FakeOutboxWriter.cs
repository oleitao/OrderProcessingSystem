using OrderProcessing.Application.Interfaces;

namespace OrderProcessing.UnitTests.Application.Fakes;

internal sealed class FakeOutboxWriter : IOutboxWriter
{
    public List<Guid> AddedForOrderIds { get; } = [];

    public Task AddOrderCreatedEventAsync(Guid orderId, CancellationToken cancellationToken)
    {
        AddedForOrderIds.Add(orderId);
        return Task.CompletedTask;
    }
}
