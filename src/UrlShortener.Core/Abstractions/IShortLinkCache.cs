using UrlShortener.Core.Domain;

namespace UrlShortener.Core.Abstractions;

public enum CacheLookup
{
    Unknown,
    Found,
    KnownMissing,
}

/// <summary>Link cache with negative entries, so repeated guesses of absent codes do not hit the database.</summary>
public interface IShortLinkCache
{
    CacheLookup Lookup(string code, out ShortLink? link);

    void StoreFound(ShortLink link);

    void StoreMissing(string code);

    void Invalidate(string code);
}
