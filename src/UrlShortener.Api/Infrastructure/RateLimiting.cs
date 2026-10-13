using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using UrlShortener.Api.Http;
using UrlShortener.Core;

namespace UrlShortener.Api.Infrastructure;

public static class RateLimiting
{
    public const string WritePolicy = "writes";
    public const string ResolvePolicy = "resolutions";

    /// <summary>
    /// Per-client-IP limits read from <see cref="RateLimitOptions"/> at request time. Lookups use a fixed window;
    /// writes use a token bucket that refills gradually, so a client cannot burst twice across a window edge.
    /// </summary>
    public static IServiceCollection AddApiRateLimiting(this IServiceCollection services)
    {
        services.AddRateLimiter(limiter =>
        {
            limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            limiter.OnRejected = async (context, cancellationToken) =>
            {
                var http = context.HttpContext;
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    http.Response.Headers.RetryAfter =
                        ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
                }

                var problem = new ProblemDetails
                {
                    Status = StatusCodes.Status429TooManyRequests,
                    Title = "Too Many Requests",
                    Detail = "Rate limit exceeded. Retry later.",
                };
                problem.Extensions["code"] = "rate_limit_exceeded";
                problem.Extensions["correlationId"] = CorrelationIdMiddleware.Get(http);
                await http.Response.WriteAsJsonAsync(problem, (System.Text.Json.JsonSerializerOptions?)null, "application/problem+json", cancellationToken);
            };

            limiter.AddPolicy(ResolvePolicy, http =>
            {
                var limits = http.RequestServices.GetRequiredService<RateLimitOptions>();
                return RateLimitPartition.GetFixedWindowLimiter(ClientKey(http), _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = limits.ResolutionsPerMinutePerIp,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = limits.QueueLimit,
                });
            });

            limiter.AddPolicy(WritePolicy, http =>
            {
                var limits = http.RequestServices.GetRequiredService<RateLimitOptions>();
                return RateLimitPartition.GetTokenBucketLimiter(ClientKey(http), _ => new TokenBucketRateLimiterOptions
                {
                    TokenLimit = limits.WritesPerMinutePerIp,
                    TokensPerPeriod = limits.WritesPerMinutePerIp,
                    ReplenishmentPeriod = TimeSpan.FromMinutes(1),
                    QueueLimit = limits.QueueLimit,
                    AutoReplenishment = true,
                });
            });
        });

        return services;
    }

    // Behind a proxy, configure ForwardedHeaders so this is the real client address, not the proxy's.
    private static string ClientKey(HttpContext http) => http.Connection.RemoteIpAddress?.ToString() ?? "unknown";
}
