namespace UrlShortener.Infrastructure.ClickTracking;

public sealed class ClickTrackingOptions
{
    public const string SectionName = "ClickTracking";

    public int QueueCapacity { get; set; } = 10_000;

    public int BatchSize { get; set; } = 100;
}
