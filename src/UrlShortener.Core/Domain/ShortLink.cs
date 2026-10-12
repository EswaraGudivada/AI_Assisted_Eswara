namespace UrlShortener.Core.Domain;

/// <summary>
/// A short code mapped to a destination URL. Immutable: state changes produce a new instance via <c>with</c>.
/// </summary>
public sealed record ShortLink
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public required string Code { get; init; }

    public required string OriginalUrl { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset? ExpiresAt { get; init; }

    public bool IsCustomAlias { get; init; }

    public bool IsActive { get; init; } = true;

    public long ClickCount { get; init; }

    public DateTimeOffset? LastAccessedAt { get; init; }

    public bool IsExpired(DateTimeOffset now) => ExpiresAt is { } expiresAt && expiresAt <= now;
}
