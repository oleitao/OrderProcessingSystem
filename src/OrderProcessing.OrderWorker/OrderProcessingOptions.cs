namespace OrderProcessing.OrderWorker;

/// <summary>
/// Development-only failure simulation. Never set FailureProbability above 0 in Production —
/// this exists purely to make retry/DLQ behavior demonstrable on demand.
/// </summary>
public sealed class OrderProcessingOptions
{
    public const string SectionName = "OrderProcessing";

    public double FailureProbability { get; set; }
}
