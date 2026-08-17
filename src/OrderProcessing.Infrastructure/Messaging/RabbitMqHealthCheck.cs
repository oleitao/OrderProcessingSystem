using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace OrderProcessing.Infrastructure.Messaging;

public sealed class RabbitMqHealthCheck(IRabbitMqConnectionFactory connectionFactory) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(5));

            await using var connection = await connectionFactory.CreateConnectionAsync(timeoutCts.Token);

            return connection.IsOpen
                ? HealthCheckResult.Healthy("RabbitMQ connection established.")
                : HealthCheckResult.Unhealthy("RabbitMQ connection could not be opened.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Could not connect to RabbitMQ.", ex);
        }
    }
}
