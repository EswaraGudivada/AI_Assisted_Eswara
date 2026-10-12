using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UrlShortener.Core.Abstractions;
using UrlShortener.Core.Domain;

namespace UrlShortener.Infrastructure.ClickTracking;

/// <summary>
/// Bounded in-process buffer between the redirect path and <see cref="ClickEventProcessor"/>.
/// When full, events are dropped instead of blocking redirects (analytics is best-effort).
/// </summary>
internal sealed partial class ChannelClickTracker : IClickTracker
{
    private readonly Channel<ClickEvent> _channel;
    private readonly ILogger<ChannelClickTracker> _logger;
    private long _dropped;

    public ChannelClickTracker(IOptions<ClickTrackingOptions> options, ILogger<ChannelClickTracker> logger)
    {
        _logger = logger;
        // FullMode.Wait makes TryWrite return false when full (DropWrite would report success while discarding).
        _channel = Channel.CreateBounded<ClickEvent>(new BoundedChannelOptions(options.Value.QueueCapacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false,
        });
    }

    public ChannelReader<ClickEvent> Reader => _channel.Reader;

    public long DroppedCount => Interlocked.Read(ref _dropped);

    public bool TryTrack(ClickEvent clickEvent)
    {
        if (_channel.Writer.TryWrite(clickEvent))
        {
            return true;
        }

        var dropped = Interlocked.Increment(ref _dropped);
        if (dropped == 1 || dropped % 1000 == 0)
        {
            LogDropped(dropped);
        }

        return false;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Click buffer is full; {DroppedCount} click events dropped so far")]
    private partial void LogDropped(long droppedCount);
}
