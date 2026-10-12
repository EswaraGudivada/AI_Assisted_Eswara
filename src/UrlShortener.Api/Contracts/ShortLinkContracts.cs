using UrlShortener.Core.Domain;

namespace UrlShortener.Api.Contracts;

/// <param name="Url">Absolute http(s) URL to shorten.</param>
/// <param name="CustomAlias">Optional custom code (3-32 chars: letters, digits, '-', '_').</param>
/// <param name="ExpiresAt">Optional expiry; must be in the future.</param>
public sealed record CreateShortLinkRequest(string? Url, string? CustomAlias = null, DateTimeOffset? ExpiresAt = null);

public sealed record ShortLinkResponse(
    string Code,
    string ShortUrl,
    string OriginalUrl,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ExpiresAt,
    bool IsActive,
    bool IsCustomAlias,
    long ClickCount,
    DateTimeOffset? LastAccessedAt)
{
    public static ShortLinkResponse From(ShortLink link, string shortUrl)
    {
        ArgumentNullException.ThrowIfNull(link);
        return new(
            link.Code,
            shortUrl,
            link.OriginalUrl,
            link.CreatedAt,
            link.ExpiresAt,
            link.IsActive,
            link.IsCustomAlias,
            link.ClickCount,
            link.LastAccessedAt);
    }
}

public sealed record PagedResponse<T>(IReadOnlyList<T> Items, int Page, int PageSize, long TotalCount);
