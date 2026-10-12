using UrlShortener.Core.Domain;

namespace UrlShortener.Core.Abstractions;

/// <summary>
/// Fire-and-forget sink for click events so that analytics never slow down or fail a redirect.
/// </summary>
public interface IClickTracker
{
    /// <summary>Returns <c>false</c> when the event was dropped (e.g. the buffer is full).</summary>
    bool TryTrack(ClickEvent clickEvent);
}
