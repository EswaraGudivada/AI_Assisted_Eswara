using System.Collections.Concurrent;
using UrlShortener.Core.Domain;

namespace UrlShortener.UnitTests.Fakes.InMemory;

/// <summary>Process-local storage for development and tests. Data is lost on restart.</summary>
internal sealed class InMemoryLinkStore
{
    public ConcurrentDictionary<string, ShortLink> Links { get; } = new(StringComparer.Ordinal);

    public ConcurrentDictionary<string, ConcurrentQueue<ClickEvent>> Clicks { get; } = new(StringComparer.Ordinal);
}
