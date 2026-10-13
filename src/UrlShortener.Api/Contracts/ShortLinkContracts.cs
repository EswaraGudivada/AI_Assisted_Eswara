using UrlShortener.Core.Common;
using UrlShortener.Core.Domain;

namespace UrlShortener.Api.Contracts;

/// <param name="Url">Absolute http(s) URL to shorten.</param>
/// <param name="Alias">Optional custom code (4-32 chars: letters, digits, '-', '_').</param>
/// <param name="ExpiresAt">Optional expiry; must be in the future.</param>
public sealed record CreateShortLinkRequest(string? Url, string? Alias = null, DateTimeOffset? ExpiresAt = null);

/// <param name="Status">active, expired or deactivated.</param>
public sealed record ShortLinkResponse(
    string Code,
    string ShortUrl,
    string TargetUrl,
    string Status,
    bool IsCustom,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ExpiresAt,
    long TotalClicks,
    DateTimeOffset? LastAccessedAt)
{
    public static ShortLinkResponse From(ShortLink link, string shortUrl, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(link);
        var status = !link.IsActive ? "deactivated" : link.IsExpired(now) ? "expired" : "active";
        return new(
            link.Code,
            shortUrl,
            link.OriginalUrl,
            status,
            link.IsCustomAlias,
            link.CreatedAt,
            link.ExpiresAt,
            link.ClickCount,
            link.LastAccessedAt);
    }
}

public sealed record PagedResponse<T>(IReadOnlyList<T> Items, int Page, int PageSize, long TotalCount, int TotalPages, bool HasNextPage)
{
    public static PagedResponse<T> From<TSource>(PagedResult<TSource> page, Func<TSource, T> map)
    {
        ArgumentNullException.ThrowIfNull(page);
        return new([.. page.Items.Select(map)], page.Page, page.PageSize, page.TotalCount, page.TotalPages, page.HasNextPage);
    }
}
