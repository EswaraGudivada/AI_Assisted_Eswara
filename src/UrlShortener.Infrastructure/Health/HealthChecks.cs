using Microsoft.Extensions.Diagnostics.HealthChecks;
using UrlShortener.Infrastructure.ClickTracking;
using UrlShortener.Infrastructure.Persistence;

namespace UrlShortener.Infrastructure.Health;

internal sealed class DatabaseHealthCheck(UrlShortenerDbContext db) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            return await db.Database.CanConnectAsync(cancellationToken)
                ? HealthCheckResult.Healthy("Database is reachable.")
                : HealthCheckResult.Unhealthy("Database is unreachable.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Database is unreachable.", ex);
        }
    }
}

/// <summary>
/// Degraded, never Unhealthy: a backed-up analytics queue loses clicks but lookups still work, so it must not
/// take the instance out of a load balancer.
/// </summary>
internal sealed class ClickQueueHealthCheck(ChannelClickTracker tracker) : IHealthCheck
{
    internal const double DegradedThreshold = 0.8;

    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var depth = tracker.QueueDepth;
        var data = new Dictionary<string, object>
        {
            ["queueDepth"] = depth,
            ["queueCapacity"] = tracker.Capacity,
            ["droppedClickEvents"] = tracker.DroppedCount,
        };

        return Task.FromResult(depth / (double)tracker.Capacity >= DegradedThreshold
            ? HealthCheckResult.Degraded("Click queue is close to capacity; events may be shed.", data: data)
            : HealthCheckResult.Healthy("Click queue has headroom.", data));
    }
}
