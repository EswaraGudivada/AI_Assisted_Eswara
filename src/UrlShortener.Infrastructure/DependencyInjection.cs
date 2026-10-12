using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using UrlShortener.Core;
using UrlShortener.Core.Abstractions;
using UrlShortener.Core.Services;
using UrlShortener.Infrastructure.Caching;
using UrlShortener.Infrastructure.ClickTracking;
using UrlShortener.Infrastructure.Health;
using UrlShortener.Infrastructure.InMemory;
using UrlShortener.Infrastructure.Persistence;

namespace UrlShortener.Infrastructure;

public static class DependencyInjection
{
    /// <summary>
    /// Registers the core service and its infrastructure. Options are resolved lazily so configuration
    /// overrides applied after registration (e.g. by test hosts) are honoured.
    /// </summary>
    public static IServiceCollection AddUrlShortener(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<ShortenerOptions>()
            .Bind(configuration.GetSection(ShortenerOptions.SectionName))
            .Validate(o => o.CodeLength is >= 4 and <= 16, "Shortener:CodeLength must be between 4 and 16.")
            .Validate(o => o.MaxGenerationAttempts >= 1, "Shortener:MaxGenerationAttempts must be at least 1.")
            .Validate(o => o.MaxUrlLength is >= 32 and <= 2048, "Shortener:MaxUrlLength must be between 32 and 2048.")
            .Validate(o => o.MaxStatsWindowDays is >= 1 and <= 366, "Shortener:MaxStatsWindowDays must be between 1 and 366.")
            .Validate(
                o => string.IsNullOrWhiteSpace(o.BaseUrl) || Uri.TryCreate(o.BaseUrl, UriKind.Absolute, out _),
                "Shortener:BaseUrl must be an absolute URL.")
            .ValidateOnStart();
        services.AddOptions<StorageOptions>()
            .Bind(configuration.GetSection(StorageOptions.SectionName))
            .Validate(o => o.Provider != StorageProvider.Sqlite || !string.IsNullOrWhiteSpace(o.ConnectionString),
                "Storage:ConnectionString is required for the Sqlite provider.")
            .ValidateOnStart();
        services.AddOptions<CachingOptions>()
            .Bind(configuration.GetSection(CachingOptions.SectionName))
            .Validate(o => o.TtlSeconds > 0 && o.MaxEntries > 0, "Caching:TtlSeconds and Caching:MaxEntries must be positive.")
            .ValidateOnStart();
        services.AddOptions<ClickTrackingOptions>()
            .Bind(configuration.GetSection(ClickTrackingOptions.SectionName))
            .Validate(o => o.QueueCapacity > 0 && o.BatchSize > 0, "ClickTracking:QueueCapacity and ClickTracking:BatchSize must be positive.")
            .ValidateOnStart();

        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton(sp => sp.GetRequiredService<IOptions<ShortenerOptions>>().Value);
        services.AddSingleton<IShortCodeGenerator, RandomShortCodeGenerator>();
        services.AddScoped<IShortLinkService, ShortLinkService>();

        services.AddSingleton<IShortLinkCache>(sp =>
            sp.GetRequiredService<IOptions<CachingOptions>>().Value.Enabled
                ? ActivatorUtilities.CreateInstance<MemoryShortLinkCache>(sp)
                : NullShortLinkCache.Instance);

        services.AddSingleton<ChannelClickTracker>();
        services.AddSingleton<IClickTracker>(sp => sp.GetRequiredService<ChannelClickTracker>());
        services.AddHostedService<ClickEventProcessor>();

        services.AddSingleton<InMemoryLinkStore>();
        services.AddDbContext<UrlShortenerDbContext>((sp, db) =>
            db.UseSqlite(sp.GetRequiredService<IOptions<StorageOptions>>().Value.ConnectionString));
        services.AddScoped<IShortLinkRepository>(sp => UseSqlite(sp)
            ? ActivatorUtilities.CreateInstance<EfShortLinkRepository>(sp)
            : ActivatorUtilities.CreateInstance<InMemoryShortLinkRepository>(sp));
        services.AddScoped<IClickEventRepository>(sp => UseSqlite(sp)
            ? ActivatorUtilities.CreateInstance<EfClickEventRepository>(sp)
            : ActivatorUtilities.CreateInstance<InMemoryClickEventRepository>(sp));

        services.AddHealthChecks().AddCheck<StorageHealthCheck>("storage");

        return services;
    }

    /// <summary>
    /// Creates the SQLite schema when it does not exist. A production deployment would apply EF Core migrations instead.
    /// </summary>
    public static async Task InitializeStorageAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        if (UseSqlite(scope.ServiceProvider))
        {
            var db = scope.ServiceProvider.GetRequiredService<UrlShortenerDbContext>();
            await db.Database.EnsureCreatedAsync(cancellationToken);
        }
    }

    private static bool UseSqlite(IServiceProvider sp) =>
        sp.GetRequiredService<IOptions<StorageOptions>>().Value.Provider == StorageProvider.Sqlite;
}
