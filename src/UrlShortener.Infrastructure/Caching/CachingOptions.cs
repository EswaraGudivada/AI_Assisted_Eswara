namespace UrlShortener.Infrastructure.Caching;

public sealed class CachingOptions
{
    public const string SectionName = "Caching";

    public bool Enabled { get; set; } = true;

    public int TtlSeconds { get; set; } = 60;

    public int MaxEntries { get; set; } = 10_000;
}
