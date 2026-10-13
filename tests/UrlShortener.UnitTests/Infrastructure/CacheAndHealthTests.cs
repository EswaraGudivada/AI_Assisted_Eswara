using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging.Abstractions;
using UrlShortener.Core;
using UrlShortener.Core.Abstractions;
using UrlShortener.Core.Domain;
using UrlShortener.Infrastructure.Caching;
using UrlShortener.Infrastructure.ClickTracking;
using UrlShortener.Infrastructure.Health;
using UrlShortener.UnitTests.Fakes;

namespace UrlShortener.UnitTests.Infrastructure;

public class CacheAndHealthTests
{
    [Fact]
    public void Cache_distinguishes_found_known_missing_and_unknown()
    {
        using var cache = new MemoryShortLinkCache(new CacheOptions(), TestMetrics.Create());
        var link = new ShortLink { Code = "found01", OriginalUrl = "https://example.com/" };

        cache.StoreFound(link);
        cache.StoreMissing("gone001");

        Assert.Equal(CacheLookup.Found, cache.Lookup("found01", out var hit));
        Assert.Same(link, hit);
        Assert.Equal(CacheLookup.KnownMissing, cache.Lookup("gone001", out var missing));
        Assert.Null(missing);
        Assert.Equal(CacheLookup.Unknown, cache.Lookup("other01", out _));
    }

    [Fact]
    public void Invalidate_removes_both_found_and_missing_entries()
    {
        using var cache = new MemoryShortLinkCache(new CacheOptions(), TestMetrics.Create());
        cache.StoreFound(new ShortLink { Code = "found01", OriginalUrl = "https://example.com/" });
        cache.StoreMissing("gone001");

        cache.Invalidate("found01");
        cache.Invalidate("gone001");

        Assert.Equal(CacheLookup.Unknown, cache.Lookup("found01", out _));
        Assert.Equal(CacheLookup.Unknown, cache.Lookup("gone001", out _));
    }

    [Theory]
    [InlineData(0, HealthStatus.Healthy)]
    [InlineData(79, HealthStatus.Healthy)]
    [InlineData(80, HealthStatus.Degraded)]
    [InlineData(100, HealthStatus.Degraded)]
    public async Task Click_queue_check_degrades_but_never_fails_under_pressure(int queued, HealthStatus expected)
    {
        var tracker = new ChannelClickTracker(new AnalyticsOptions { QueueCapacity = 100 }, TestMetrics.Create(), NullLogger<ChannelClickTracker>.Instance);
        for (var i = 0; i < queued; i++)
        {
            tracker.TryTrack(new ClickEvent { Code = "abc", OccurredAt = DateTimeOffset.UtcNow });
        }

        var result = await new ClickQueueHealthCheck(tracker).CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(expected, result.Status);
        Assert.Equal(queued, result.Data["queueDepth"]);
    }
}
