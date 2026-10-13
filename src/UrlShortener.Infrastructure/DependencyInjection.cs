using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using UrlShortener.Core;
using UrlShortener.Core.Abstractions;
using UrlShortener.Core.Safety;
using UrlShortener.Core.Services;
using UrlShortener.Infrastructure.Caching;
using UrlShortener.Infrastructure.ClickTracking;
using UrlShortener.Infrastructure.Diagnostics;
using UrlShortener.Infrastructure.Health;
using UrlShortener.Infrastructure.Persistence;

namespace UrlShortener.Infrastructure;

public static class DependencyInjection
{
    public const string ConnectionStringName = "Default";
    private const string DefaultConnectionString = "Data Source=urlshortener.db";

    /// <summary>
    /// Registers the core service and its infrastructure. Options and the connection string are resolved when
    /// first used, so overrides applied after registration (environment variables, test hosts) are honoured.
    /// </summary>
    public static IServiceCollection AddUrlShortener(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        AddValidatedOptions<ShortenerOptions>(services, configuration, ShortenerOptions.SectionName);
        AddValidatedOptions<SecurityOptions>(services, configuration, SecurityOptions.SectionName);
        AddValidatedOptions<CacheOptions>(services, configuration, CacheOptions.SectionName);
        AddValidatedOptions<AnalyticsOptions>(services, configuration, AnalyticsOptions.SectionName);
        AddValidatedOptions<RateLimitOptions>(services, configuration, RateLimitOptions.SectionName);

        services.TryAddSingleton(TimeProvider.System);
        services.AddMetrics();
        services.AddSingleton<ShortenerMetrics>();

        services.AddSingleton<IUrlSafetyRule, PrivateNetworkRule>();
        services.AddSingleton<IUrlSafetyRule, DeniedHostRule>();
        services.AddSingleton<IUrlSafetyValidator, UrlSafetyValidator>();
        services.AddSingleton<IShortCodeGenerator, RandomShortCodeGenerator>();
        services.AddScoped<IShortLinkService, ShortLinkService>();
        services.AddSingleton<IShortLinkCache, MemoryShortLinkCache>();

        services.AddSingleton<ChannelClickTracker>();
        services.AddSingleton<IClickTracker>(sp => sp.GetRequiredService<ChannelClickTracker>());
        services.AddHostedService<ClickEventProcessor>();

        services.AddDbContext<UrlShortenerDbContext>((sp, db) =>
            db.UseSqlite(sp.GetRequiredService<IConfiguration>().GetConnectionString(ConnectionStringName) ?? DefaultConnectionString));
        services.AddScoped<IShortLinkRepository, EfShortLinkRepository>();
        services.AddScoped<IClickEventRepository, EfClickEventRepository>();

        services.AddHealthChecks()
            .AddCheck<DatabaseHealthCheck>("database", tags: ["ready"])
            .AddCheck<ClickQueueHealthCheck>("click-queue", tags: ["ready"]);

        return services;
    }

    /// <summary>
    /// Validates every option section, then applies pending EF Core migrations unless <c>Database:AutoMigrate</c>
    /// is false (e.g. when migrations are applied by a release pipeline instead).
    /// </summary>
    public static async Task InitializeStorageAsync(
        this IServiceProvider services,
        IConfiguration configuration,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        await using var scope = services.CreateAsyncScope();
        _ = scope.ServiceProvider.GetRequiredService<ShortenerOptions>();

        if (configuration.GetValue("Database:AutoMigrate", defaultValue: true))
        {
            var db = scope.ServiceProvider.GetRequiredService<UrlShortenerDbContext>();
            await LegacySchemaUpgrader.UpgradeIfNeededAsync(db, cancellationToken);
            await db.Database.MigrateAsync(cancellationToken);
        }
    }

    private static void AddValidatedOptions<T>(IServiceCollection services, IConfiguration configuration, string section)
        where T : class
    {
        services.AddOptions<T>()
            .Bind(configuration.GetSection(section))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddSingleton(sp => sp.GetRequiredService<IOptions<T>>().Value);
    }
}
