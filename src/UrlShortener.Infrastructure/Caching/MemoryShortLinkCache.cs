using Microsoft.Extensions.Caching.Memory;
using UrlShortener.Core;
using UrlShortener.Core.Abstractions;
using UrlShortener.Core.Domain;
using UrlShortener.Infrastructure.Diagnostics;

namespace UrlShortener.Infrastructure.Caching;

/// <summary>
/// Dedicated, size-bounded cache for the lookup path. Found links live for minutes; "known missing" tombstones
/// live for seconds so scanners guessing codes do not reach the database, while a newly created code is
/// never hidden for long (creation also evicts the tombstone on this instance).
/// </summary>
internal sealed class MemoryShortLinkCache : IShortLinkCache, IDisposable
{
    private static readonly object Tombstone = new();

    private readonly MemoryCache _cache;
    private readonly MemoryCacheEntryOptions _found;
    private readonly MemoryCacheEntryOptions _missing;
    private readonly ShortenerMetrics _metrics;

    public MemoryShortLinkCache(CacheOptions options, ShortenerMetrics metrics)
    {
        ArgumentNullException.ThrowIfNull(options);
        _metrics = metrics;
        _cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = options.SizeLimit });
        _found = new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(options.LinkTtlSeconds), Size = 1 };
        _missing = new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(options.MissingTtlSeconds), Size = 1 };
    }

    public CacheLookup Lookup(string code, out ShortLink? link)
    {
        link = null;
        var result = CacheLookup.Unknown;
        if (_cache.TryGetValue(code, out var entry))
        {
            link = entry as ShortLink;
            result = ReferenceEquals(entry, Tombstone) ? CacheLookup.KnownMissing
                : link is null ? CacheLookup.Unknown
                : CacheLookup.Found;
        }

        _metrics.CacheLookup(result switch
        {
            CacheLookup.Found => "hit",
            CacheLookup.KnownMissing => "negative_hit",
            _ => "miss",
        });
        return result;
    }

    public void StoreFound(ShortLink link)
    {
        ArgumentNullException.ThrowIfNull(link);
        _cache.Set(link.Code, link, _found);
    }

    public void StoreMissing(string code) => _cache.Set(code, Tombstone, _missing);

    public void Invalidate(string code) => _cache.Remove(code);

    public void Dispose() => _cache.Dispose();
}
