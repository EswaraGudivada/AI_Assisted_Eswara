using UrlShortener.Core.Domain;

namespace UrlShortener.Core.Models;

public sealed record CreateShortLinkCommand(string? Url, string? Alias = null, DateTimeOffset? ExpiresAt = null);

public sealed record CreateShortLinkResult(ShortLink Link, bool Created);

public sealed record ClickContext(string? Referrer, string? UserAgent)
{
    public static readonly ClickContext Empty = new(null, null);
}

public sealed record DailyClickCount(DateOnly Date, long Clicks);

public sealed record ReferrerClickCount(string Referrer, long Clicks);

/// <param name="TotalClicks">All-time clicks recorded for the link.</param>
/// <param name="ClicksInRange">Clicks between <paramref name="From"/> and <paramref name="To"/>.</param>
public sealed record LinkStatistics(
    string Code,
    long TotalClicks,
    DateTimeOffset? LastAccessedAt,
    DateTimeOffset From,
    DateTimeOffset To,
    long ClicksInRange,
    IReadOnlyList<DailyClickCount> Daily,
    IReadOnlyList<ReferrerClickCount> TopReferrers);
