using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using UrlShortener.Core;
using UrlShortener.Core.Abstractions;
using UrlShortener.Core.Domain;
using UrlShortener.Infrastructure.Diagnostics;

namespace UrlShortener.Infrastructure.ClickTracking;

/// <summary>
/// Bounded in-process buffer between the lookup path and <see cref="ClickEventProcessor"/>.
/// When full, events are shed (and counted) instead of slowing lookups: analytics is best-effort.
/// </summary>
internal sealed partial class ChannelClickTracker : IClickTracker
{
    private readonly Channel<ClickEvent> _channel;
    private readonly ShortenerMetrics _metrics;
    private readonly ILogger<ChannelClickTracker> _logger;
    private long _dropped;

    public ChannelClickTracker(AnalyticsOptions options, ShortenerMetrics metrics, ILogger<ChannelClickTracker> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        _metrics = metrics;
        _logger = logger;
        Capacity = options.QueueCapacity;
        // FullMode.Wait makes TryWrite return false when full (DropWrite would report success while discarding).
        _channel = Channel.CreateBounded<ClickEvent>(new BoundedChannelOptions(options.QueueCapacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false,
        });
        metrics.ObserveQueueDepth(() => QueueDepth);
    }

    public ChannelReader<ClickEvent> Reader => _channel.Reader;

    public int Capacity { get; }

    public int QueueDepth => _channel.Reader.Count;

    public long DroppedCount => Interlocked.Read(ref _dropped);

    public bool TryTrack(ClickEvent clickEvent)
    {
        if (_channel.Writer.TryWrite(clickEvent))
        {
            _metrics.ClickEnqueued();
            return true;
        }

        _metrics.ClickDropped();
        var dropped = Interlocked.Increment(ref _dropped);
        if (dropped == 1 || dropped % 1000 == 0)
        {
            LogDropped(dropped);
        }

        return false;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Click queue is full; {DroppedCount} click events dropped so far")]
    private partial void LogDropped(long droppedCount);
}
