using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace UrlShortener.Api.Infrastructure;

public sealed class RateLimitingOptions
{
    public const string SectionName = "RateLimiting";

    public int CreatePermitsPerMinute { get; set; } = 30;

    public int RedirectPermitsPerMinute { get; set; } = 600;
}

public static class RateLimiting
{
    public const string CreatePolicy = "create";
    public const string RedirectPolicy = "redirect";

    /// <summary>Per-client-IP fixed window limits; link creation is limited much more tightly than redirects.</summary>
    public static IServiceCollection AddApiRateLimiting(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<RateLimitingOptions>()
            .Bind(configuration.GetSection(RateLimitingOptions.SectionName))
            .Validate(o => o.CreatePermitsPerMinute > 0 && o.RedirectPermitsPerMinute > 0, "Rate limits must be positive.")
            .ValidateOnStart();

        services.AddRateLimiter(limiter =>
        {
            limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            limiter.OnRejected = (context, _) =>
            {
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    context.HttpContext.Response.Headers.RetryAfter =
                        ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
                }

                return ValueTask.CompletedTask;
            };
            limiter.AddPolicy(CreatePolicy, http => FixedWindowPerClient(http, o => o.CreatePermitsPerMinute));
            limiter.AddPolicy(RedirectPolicy, http => FixedWindowPerClient(http, o => o.RedirectPermitsPerMinute));
        });

        return services;
    }

    private static RateLimitPartition<string> FixedWindowPerClient(HttpContext http, Func<RateLimitingOptions, int> permits)
    {
        var options = http.RequestServices.GetRequiredService<IOptions<RateLimitingOptions>>().Value;
        var clientKey = http.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return RateLimitPartition.GetFixedWindowLimiter(clientKey, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = permits(options),
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
        });
    }
}
