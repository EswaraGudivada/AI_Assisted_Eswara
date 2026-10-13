using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using UrlShortener.Core;
using UrlShortener.Core.Abstractions;
using UrlShortener.Core.Domain;
using UrlShortener.Infrastructure.ClickTracking;
using UrlShortener.Infrastructure.Diagnostics;
using UrlShortener.UnitTests.Fakes;
using UrlShortener.UnitTests.Fakes.InMemory;

namespace UrlShortener.UnitTests.Infrastructure;

public class ClickPipelineTests
{
    [Fact]
    public void Tracker_drops_events_when_the_buffer_is_full()
    {
        var tracker = CreateTracker(capacity: 2);

        Assert.True(tracker.TryTrack(Click()));
        Assert.True(tracker.TryTrack(Click()));
        Assert.False(tracker.TryTrack(Click()));
        Assert.Equal(1, tracker.DroppedCount);
        Assert.Equal(2, tracker.QueueDepth);
    }

    [Fact]
    public async Task Processor_persists_tracked_events_in_the_background()
    {
        using var provider = BuildProvider(out var tracker, batchSize: 10);
        var processor = provider.GetRequiredService<ClickEventProcessor>();
        var store = provider.GetRequiredService<InMemoryLinkStore>();

        await processor.StartAsync(CancellationToken.None);
        for (var i = 0; i < 25; i++)
        {
            tracker.TryTrack(Click());
        }

        await WaitUntilAsync(() => store.Links["abc"].ClickCount == 25);
        await processor.StopAsync(CancellationToken.None);

        Assert.Equal(25, store.Clicks["abc"].Count);
    }

    [Fact]
    public async Task Processor_drains_buffered_events_on_shutdown()
    {
        using var provider = BuildProvider(out var tracker, batchSize: 1000);
        var processor = provider.GetRequiredService<ClickEventProcessor>();
        var store = provider.GetRequiredService<InMemoryLinkStore>();
        await processor.StartAsync(CancellationToken.None);

        for (var i = 0; i < 500; i++)
        {
            tracker.TryTrack(Click());
        }

        await processor.StopAsync(CancellationToken.None);

        Assert.Equal(500, store.Links["abc"].ClickCount);
    }

    [Fact]
    public async Task Processor_keeps_running_after_a_persistence_failure()
    {
        var failing = new FlakyClickRepository(failuresBeforeSuccess: 1);
        var services = new ServiceCollection();
        services.AddSingleton<IClickEventRepository>(failing);
        services.AddSingleton(new AnalyticsOptions { QueueCapacity = 100, BatchSize = 1 });
        services.AddLogging();
        services.AddMetrics();
        services.AddSingleton<ShortenerMetrics>();
        services.AddSingleton<ChannelClickTracker>();
        services.AddSingleton<ClickEventProcessor>();
        using var provider = services.BuildServiceProvider();
        var tracker = provider.GetRequiredService<ChannelClickTracker>();
        var processor = provider.GetRequiredService<ClickEventProcessor>();

        await processor.StartAsync(CancellationToken.None);
        tracker.TryTrack(Click());
        await WaitUntilAsync(() => failing.Attempts >= 1);
        tracker.TryTrack(Click());
        await WaitUntilAsync(() => failing.Persisted == 1);
        await processor.StopAsync(CancellationToken.None);

        Assert.Equal(1, failing.Persisted);
    }

    private static ServiceProvider BuildProvider(out ChannelClickTracker tracker, int batchSize)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMetrics();
        services.AddSingleton<ShortenerMetrics>();
        services.AddSingleton(new AnalyticsOptions { QueueCapacity = 10_000, BatchSize = batchSize });
        services.AddSingleton<InMemoryLinkStore>();
        services.AddScoped<IClickEventRepository, InMemoryClickEventRepository>();
        services.AddSingleton<ChannelClickTracker>();
        services.AddSingleton<ClickEventProcessor>();
        var provider = services.BuildServiceProvider();
        provider.GetRequiredService<InMemoryLinkStore>().Links["abc"] = new ShortLink { Code = "abc", OriginalUrl = "https://a.example/" };
        tracker = provider.GetRequiredService<ChannelClickTracker>();
        return provider;
    }

    private static ChannelClickTracker CreateTracker(int capacity) =>
        new(new AnalyticsOptions { QueueCapacity = capacity }, TestMetrics.Create(), NullLogger<ChannelClickTracker>.Instance);

    private static ClickEvent Click() => new() { Code = "abc", OccurredAt = DateTimeOffset.UtcNow };

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException("Condition was not met in time.");
            }

            await Task.Delay(10);
        }
    }

    private sealed class FlakyClickRepository(int failuresBeforeSuccess) : IClickEventRepository
    {
        private int _attempts;
        private int _persisted;

        public int Attempts => Volatile.Read(ref _attempts);

        public int Persisted => Volatile.Read(ref _persisted);

        public Task RecordBatchAsync(IReadOnlyCollection<ClickEvent> events, CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref _attempts) <= failuresBeforeSuccess)
            {
                throw new InvalidOperationException("Simulated storage outage.");
            }

            Interlocked.Add(ref _persisted, events.Count);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<ClickEvent>> GetRangeAsync(string code, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ClickEvent>>([]);
    }
}
