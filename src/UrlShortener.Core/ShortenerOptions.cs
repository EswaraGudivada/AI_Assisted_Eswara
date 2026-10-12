namespace UrlShortener.Core;

public sealed class ShortenerOptions
{
    public const string SectionName = "Shortener";

    /// <summary>Public base URL used to build short URLs. When empty the request scheme/host is used.</summary>
    public string? BaseUrl { get; set; }

    public int CodeLength { get; set; } = 7;

    public int MaxGenerationAttempts { get; set; } = 5;

    /// <summary>Return the existing generated link when the same URL is shortened again without alias or expiry.</summary>
    public bool DeduplicateUrls { get; set; } = true;

    public int MaxUrlLength { get; set; } = 2048;

    public int MaxStatsWindowDays { get; set; } = 90;

    /// <summary>Aliases that would shadow API routes or are otherwise misleading. Configured values are appended.</summary>
    public IList<string> ReservedAliases { get; set; } = ["api", "health", "openapi", "swagger", "admin", "static"];

    /// <summary>Hosts (and their subdomains) that cannot be shortened. The BaseUrl host is always blocked.</summary>
    public IList<string> BlockedHosts { get; set; } = [];
}
