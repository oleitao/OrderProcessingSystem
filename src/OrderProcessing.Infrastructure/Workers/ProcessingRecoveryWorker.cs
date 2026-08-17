using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OrderProcessing.Domain.Enums;
using OrderProcessing.Domain.Rules;
using OrderProcessing.Infrastructure.Database;

namespace OrderProcessing.Infrastructure.Workers;

/// <summary>
/// Periodically scans for Orders stuck in Processing (section 22) and marks them Failed instead
/// of leaving them hung forever. Under the current architecture (Phase 11 commits Pending→
/// Processing→Completed in a single transaction) this should normally find nothing — a crash
/// mid-processing rolls back to Pending rather than leaving Processing durably persisted. This
/// worker exists as a defensive safety net for anomalies the transaction design doesn't cover:
/// manual DB intervention, a future long-running processing step split across commits, bugs.
/// It never guesses whether processing actually succeeded — MarkAsFailed is the only safe move
/// when we genuinely don't know, since it's a defined, non-destructive Domain transition that
/// leaves the Order in a known, actionable state instead of an ambiguous one.
/// </summary>
public sealed class ProcessingRecoveryWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<ProcessingRecoveryWorkerOptions> options,
    ILogger<ProcessingRecoveryWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var pollingInterval = TimeSpan.FromSeconds(options.Value.PollingIntervalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RecoverStuckOrdersAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Unexpected error while scanning for stuck Orders.");
            }

            try
            {
                await Task.Delay(pollingInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                // Shutting down.
            }
        }
    }

    private async Task RecoverStuckOrdersAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<OrderDbContext>();

        // Pushing only the cheap, always-translatable filter (Status == Processing) to Postgres,
        // then applying the exact threshold rule in memory via the same StuckOrderDetector that's
        // unit-tested in isolation — no risk of the SQL and the "what counts as stuck" rule drifting.
        var processingOrders = await dbContext.Orders
            .Where(order => order.Status == OrderStatus.Processing)
            .ToListAsync(cancellationToken);

        var now = DateTime.UtcNow;
        var threshold = TimeSpan.FromSeconds(options.Value.StuckThresholdSeconds);

        var stuckOrders = processingOrders
            .Where(order => StuckOrderDetector.IsStuck(order.Status, order.UpdatedAtUtc, now, threshold))
            .ToList();

        if (stuckOrders.Count == 0)
            return;

        foreach (var order in stuckOrders)
        {
            var stuckFor = now - order.UpdatedAtUtc!.Value;

            logger.LogWarning(
                "Stuck Order detected. OrderId: {OrderId}, Status: {Status}, UpdatedAtUtc: {UpdatedAtUtc}, StuckFor: {StuckFor}.",
                order.Id, order.Status, order.UpdatedAtUtc, stuckFor);

            order.MarkAsFailed();

            logger.LogWarning("Stuck Order recovered. OrderId: {OrderId} marked as Failed.", order.Id);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
