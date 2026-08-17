namespace OrderProcessing.Infrastructure.Workers;

public sealed class ProcessingRecoveryWorkerOptions
{
    public const string SectionName = "ProcessingRecoveryWorker";

    public int PollingIntervalSeconds { get; set; } = 60;
    public int StuckThresholdSeconds { get; set; } = 300;
}
