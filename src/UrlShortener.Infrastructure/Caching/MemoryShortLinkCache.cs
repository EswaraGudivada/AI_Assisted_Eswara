using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using UrlShortener.Core.Abstractions;
using UrlShortener.Core.Domain;

namespace UrlShortener.Infrastructure.Caching;

/// <summary>
/// Bounded, per-instance cache for the redirect hot path. Entries expire after a short TTL so
/// deactivations made on other instances become visible within <see cref="CachingOptions.TtlSeconds"/>.
/// </summary>
internal sealed class MemoryShortLinkCache : IShortLinkCache, IDisposable
{
    private readonly MemoryCache _cache;
    private readonly MemoryCacheEntryOptions _entryOptions;

    public MemoryShortLinkCache(IOptions<CachingOptions> options)
    {
        var value = options.Value;
        _cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = value.MaxEntries });
        _entryOptions = new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(value.TtlSeconds),
            Size = 1,
        };
    }

    public bool TryGet(string code, [NotNullWhen(true)] out ShortLink? link) => _cache.TryGetValue(code, out link);

    public void Set(ShortLink link)
    {
        ArgumentNullException.ThrowIfNull(link);
        _cache.Set(link.Code, link, _entryOptions);
    }

    public void Remove(string code) => _cache.Remove(code);

    public void Dispose() => _cache.Dispose();
}

internal sealed class NullShortLinkCache : IShortLinkCache
{
    public static readonly NullShortLinkCache Instance = new();

    public bool TryGet(string code, [NotNullWhen(true)] out ShortLink? link)
    {
        link = null;
        return false;
    }

    public void Set(ShortLink link)
    {
    }

    public void Remove(string code)
    {
    }
}
