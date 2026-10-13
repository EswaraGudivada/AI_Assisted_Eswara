using Microsoft.Extensions.Time.Testing;
using UrlShortener.Core;
using UrlShortener.Core.Abstractions;
using UrlShortener.Core.Safety;
using UrlShortener.Core.Services;
using UrlShortener.UnitTests.Fakes.InMemory;

namespace UrlShortener.UnitTests.Fakes;

internal sealed class ServiceFixture
{
    public ServiceFixture(IShortCodeGenerator? generator = null, ShortenerOptions? options = null, SecurityOptions? security = null)
    {
        Store = new InMemoryLinkStore();
        Links = new InMemoryShortLinkRepository(Store);
        Clicks = new InMemoryClickEventRepository(Store);
        Options = options ?? new ShortenerOptions { BaseUrl = "https://sho.rt" };
        Security = security ?? new SecurityOptions();
        var safety = new UrlSafetyValidator([new PrivateNetworkRule(Security), new DeniedHostRule(Security, Options)]);
        Service = new ShortLinkService(
            Links,
            Clicks,
            generator ?? new RandomShortCodeGenerator(),
            Cache,
            Tracker,
            safety,
            Options,
            Analytics,
            Time);
    }

    public FakeTimeProvider Time { get; } = new(new DateTimeOffset(2026, 1, 15, 12, 0, 0, TimeSpan.Zero));

    public InMemoryLinkStore Store { get; }

    public InMemoryShortLinkRepository Links { get; }

    public InMemoryClickEventRepository Clicks { get; }

    public DictionaryCache Cache { get; } = new();

    public RecordingClickTracker Tracker { get; } = new();

    public ShortenerOptions Options { get; }

    public SecurityOptions Security { get; }

    public AnalyticsOptions Analytics { get; } = new();

    public ShortLinkService Service { get; }

    /// <summary>Simulates the background processor flushing tracked clicks.</summary>
    public async Task FlushClicksAsync()
    {
        await Clicks.RecordBatchAsync(Tracker.Events.ToList());
        Tracker.Events.Clear();
    }
}
