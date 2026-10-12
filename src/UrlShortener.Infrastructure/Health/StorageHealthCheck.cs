using Microsoft.Extensions.Diagnostics.HealthChecks;
using UrlShortener.Core.Abstractions;
using UrlShortener.Infrastructure.ClickTracking;

namespace UrlShortener.Infrastructure.Health;

internal sealed class StorageHealthCheck(IShortLinkRepository repository, ChannelClickTracker tracker) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var data = new Dictionary<string, object> { ["droppedClickEvents"] = tracker.DroppedCount };
        try
        {
            await repository.GetByCodeAsync("__healthcheck__", cancellationToken);
            return HealthCheckResult.Healthy("Storage is reachable.", data);
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Storage is unreachable.", ex, data);
        }
    }
}
