namespace UrlShortener.Core.Domain;

/// <summary>
/// A single successful redirect. Only the referrer host and a truncated user agent are kept (no IP addresses).
/// </summary>
public sealed record ClickEvent
{
    public long Id { get; init; }

    public required string Code { get; init; }

    public DateTimeOffset OccurredAt { get; init; }

    public string? Referrer { get; init; }

    public string? UserAgent { get; init; }
}
