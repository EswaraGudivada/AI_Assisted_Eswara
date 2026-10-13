using System.ComponentModel.DataAnnotations;

namespace UrlShortener.Core;

/// <summary>
/// Plain option classes with data annotations. The host binds them and validates on start, so a bad
/// setting stops the service at boot instead of failing a later request.
/// </summary>
public sealed class ShortenerOptions
{
    public const string SectionName = "Shortener";

    /// <summary>Public origin used to build short URLs. Its host is also refused as a target (no loops).</summary>
    [Required]
    [Url]
    public string BaseUrl { get; set; } = "http://localhost:5080";

    [Range(4, 32)]
    public int CodeLength { get; set; } = 7;

    /// <summary>Insert attempts for a generated code before answering 503.</summary>
    [Range(1, 10)]
    public int MaxGenerationAttempts { get; set; } = 3;

    [Range(1, 100)]
    public int DefaultPageSize { get; set; } = 20;

    [Range(1, 500)]
    public int MaxPageSize { get; set; } = 100;

    [Range(32, 2048)]
    public int MaxUrlLength { get; set; } = 2048;

    /// <summary>Return the existing generated link when the same URL is shortened again without alias or expiry.</summary>
    public bool DeduplicateUrls { get; set; } = true;

    /// <summary>Codes that would shadow service routes. Configured values are appended.</summary>
    public IList<string> ReservedAliases { get; set; } = ["api", "health", "openapi", "swagger", "admin", "static"];
}

/// <summary>Abuse controls for link targets. With no login on the API, these and the rate limits are the protection.</summary>
public sealed class SecurityOptions
{
    public const string SectionName = "Security";

    /// <summary>Hosts refused as targets, matched exactly or as a parent domain.</summary>
    public IList<string> DeniedHosts { get; set; } = [];

    /// <summary>Allows localhost/private targets. Development only.</summary>
    public bool AllowPrivateNetworkTargets { get; set; }
}

public sealed class CacheOptions
{
    public const string SectionName = "Cache";

    [Range(1, 86_400)]
    public int LinkTtlSeconds { get; set; } = 300;

    /// <summary>How long "this code does not exist" is remembered. Short, so a new link is not hidden for long.</summary>
    [Range(1, 3_600)]
    public int MissingTtlSeconds { get; set; } = 30;

    [Range(100, 1_000_000)]
    public int SizeLimit { get; set; } = 10_000;
}

public sealed class AnalyticsOptions
{
    public const string SectionName = "Analytics";

    [Range(100, 1_000_000)]
    public int QueueCapacity { get; set; } = 10_000;

    [Range(1, 10_000)]
    public int BatchSize { get; set; } = 200;

    [Range(1, 3_650)]
    public int MaxStatsRangeDays { get; set; } = 366;

    [Range(1, 365)]
    public int DefaultStatsWindowDays { get; set; } = 30;

    [Range(1, 100)]
    public int TopReferrerCount { get; set; } = 10;
}

/// <summary>Per-client-IP limits. The write limit is the main brake on bulk link creation.</summary>
public sealed class RateLimitOptions
{
    public const string SectionName = "RateLimit";

    [Range(1, 100_000)]
    public int ResolutionsPerMinutePerIp { get; set; } = 600;

    [Range(1, 100_000)]
    public int WritesPerMinutePerIp { get; set; } = 30;

    [Range(0, 10_000)]
    public int QueueLimit { get; set; }
}
