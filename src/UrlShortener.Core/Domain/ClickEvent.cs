namespace UrlShortener.Core.Domain;

/// <summary>
/// One successful lookup. Stores no IP address, no user id and no full browser string: only the referring
/// site's host and a coarse browser family.
/// </summary>
public sealed record ClickEvent
{
    public long Id { get; init; }

    public required string Code { get; init; }

    public DateTimeOffset OccurredAt { get; init; }

    public string? RefererHost { get; init; }

    public string? UserAgentFamily { get; init; }
}
