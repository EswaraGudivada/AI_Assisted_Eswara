using UrlShortener.Core.Domain;

namespace UrlShortener.Core.Models;

public sealed record CreateShortLinkCommand(string? Url, string? CustomAlias = null, DateTimeOffset? ExpiresAt = null);

public sealed record CreateShortLinkResult(ShortLink Link, bool Created);

public sealed record ClickContext(string? Referrer, string? UserAgent)
{
    public static readonly ClickContext Empty = new(null, null);
}

public sealed record DailyClickCount(DateOnly Date, int Clicks);

public sealed record ReferrerCount(string Referrer, int Clicks);

public sealed record LinkStatistics(
    string Code,
    long TotalClicks,
    DateTimeOffset? LastAccessedAt,
    int WindowDays,
    IReadOnlyList<DailyClickCount> DailyClicks,
    IReadOnlyList<ReferrerCount> TopReferrers);
