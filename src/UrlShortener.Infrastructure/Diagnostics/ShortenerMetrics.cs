using System.Diagnostics.Metrics;

namespace UrlShortener.Infrastructure.Diagnostics;

/// <summary>
/// Counters on the standard .NET Meter API, so any OpenTelemetry/Prometheus exporter or
/// <c>dotnet-counters monitor --counters UrlShortener</c> can read them without code changes.
/// </summary>
public sealed class ShortenerMetrics : IDisposable
{
    public const string MeterName = "UrlShortener";

    private readonly Meter _meter;
    private readonly Counter<long> _resolutions;
    private readonly Counter<long> _linksCreated;
    private readonly Counter<long> _cacheLookups;
    private readonly Counter<long> _clicksEnqueued;
    private readonly Counter<long> _clicksDropped;
    private readonly Counter<long> _clicksPersisted;

    public ShortenerMetrics(IMeterFactory meterFactory)
    {
        ArgumentNullException.ThrowIfNull(meterFactory);
        _meter = meterFactory.Create(MeterName);
        _resolutions = _meter.CreateCounter<long>("urlshortener.resolutions", description: "Short-code lookups by outcome.");
        _linksCreated = _meter.CreateCounter<long>("urlshortener.links.created", description: "Links created, by generated/custom.");
        _cacheLookups = _meter.CreateCounter<long>("urlshortener.cache.lookups", description: "Link cache lookups by result.");
        _clicksEnqueued = _meter.CreateCounter<long>("urlshortener.clicks.enqueued");
        _clicksDropped = _meter.CreateCounter<long>("urlshortener.clicks.dropped", description: "Clicks shed because the queue was full.");
        _clicksPersisted = _meter.CreateCounter<long>("urlshortener.clicks.persisted");
    }

    public void ObserveQueueDepth(Func<int> depth) =>
        _meter.CreateObservableGauge("urlshortener.clicks.queue_depth", depth, description: "Clicks waiting to be written.");

    public void Resolution(string outcome) => _resolutions.Add(1, new KeyValuePair<string, object?>("outcome", outcome));

    public void LinkCreated(bool isCustom) =>
        _linksCreated.Add(1, new KeyValuePair<string, object?>("type", isCustom ? "custom" : "generated"));

    public void CacheLookup(string result) => _cacheLookups.Add(1, new KeyValuePair<string, object?>("result", result));

    public void ClickEnqueued() => _clicksEnqueued.Add(1);

    public void ClickDropped() => _clicksDropped.Add(1);

    public void ClicksPersisted(int count) => _clicksPersisted.Add(count);

    public void Dispose() => _meter.Dispose();
}
