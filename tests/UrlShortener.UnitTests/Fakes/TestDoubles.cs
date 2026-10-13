using UrlShortener.Core.Abstractions;
using UrlShortener.Core.Domain;

namespace UrlShortener.UnitTests.Fakes;

internal sealed class SequenceCodeGenerator(params string[] codes) : IShortCodeGenerator
{
    private readonly Queue<string> _codes = new(codes);

    public int Calls { get; private set; }

    public string Generate(int length)
    {
        Calls++;
        return _codes.Count > 0 ? _codes.Dequeue() : throw new InvalidOperationException("No more codes configured.");
    }
}

internal sealed class RecordingClickTracker : IClickTracker
{
    public List<ClickEvent> Events { get; } = [];

    public bool Accept { get; set; } = true;

    public bool TryTrack(ClickEvent clickEvent)
    {
        if (!Accept)
        {
            return false;
        }

        Events.Add(clickEvent);
        return true;
    }
}

internal sealed class DictionaryCache : IShortLinkCache
{
    public Dictionary<string, ShortLink> Entries { get; } = new(StringComparer.Ordinal);

    public HashSet<string> Missing { get; } = new(StringComparer.Ordinal);

    public CacheLookup Lookup(string code, out ShortLink? link)
    {
        if (Entries.TryGetValue(code, out link))
        {
            return CacheLookup.Found;
        }

        return Missing.Contains(code) ? CacheLookup.KnownMissing : CacheLookup.Unknown;
    }

    public void StoreFound(ShortLink link) => Entries[link.Code] = link;

    public void StoreMissing(string code) => Missing.Add(code);

    public void Invalidate(string code)
    {
        Entries.Remove(code);
        Missing.Remove(code);
    }
}
