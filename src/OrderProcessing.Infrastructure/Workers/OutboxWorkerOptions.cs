namespace OrderProcessing.Infrastructure.Workers;

public sealed class OutboxWorkerOptions
{
    public const string SectionName = "OutboxWorker";

    public int PollingIntervalSeconds { get; set; } = 5;
    public int BatchSize { get; set; } = 20;
}
