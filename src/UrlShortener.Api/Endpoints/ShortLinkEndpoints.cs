using UrlShortener.Api.Contracts;
using UrlShortener.Api.Infrastructure;
using UrlShortener.Core.Domain;
using UrlShortener.Core.Models;
using UrlShortener.Core.Services;

namespace UrlShortener.Api.Endpoints;

public static class ShortLinkEndpoints
{
    public const string LinksRoute = "/api/v1/links";

    public static IEndpointRouteBuilder MapShortLinkEndpoints(this IEndpointRouteBuilder app)
    {
        var links = app.MapGroup(LinksRoute).WithTags("Links");

        links.MapPost("/", CreateAsync)
            .RequireRateLimiting(RateLimiting.WritePolicy)
            .WithName("CreateShortLink")
            .WithSummary("Create a short link")
            .WithDescription("201 for a new link; 200 with the existing link when the same URL was already shortened without alias or expiry.")
            .Produces<ShortLinkResponse>(StatusCodes.Status201Created)
            .Produces<ShortLinkResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        links.MapGet("/", ListAsync)
            .WithName("ListShortLinks")
            .WithSummary("List short links, newest first")
            .Produces<PagedResponse<ShortLinkResponse>>()
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        links.MapGet("/{code}", GetAsync)
            .WithName("GetShortLink")
            .WithSummary("Get short link details")
            .Produces<ShortLinkResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        links.MapDelete("/{code}", DeactivateAsync)
            .RequireRateLimiting(RateLimiting.WritePolicy)
            .WithName("DeactivateShortLink")
            .WithSummary("Deactivate a short link (soft delete; repeating it is safe; the code is never reused)")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);

        links.MapGet("/{code}/stats", GetStatisticsAsync)
            .RequireRateLimiting(RateLimiting.WritePolicy)
            .WithName("GetShortLinkStatistics")
            .WithSummary("Click statistics for a short link between 'from' and 'to' (default: last 30 days)")
            .Produces<LinkStatistics>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);

        app.MapGet("/{code}", ResolveAsync)
            .RequireRateLimiting(RateLimiting.ResolvePolicy)
            .WithTags("Resolve")
            .WithName("ResolveShortLink")
            .WithSummary("Look up a short code and return the original URL")
            .WithDescription("Returns 200 with the original URL as a JSON string. Deliberately not a redirect, so the result is visible in Swagger and API clients.")
            .Produces<string>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status410Gone)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);

        return app;
    }

    private static async Task<IResult> CreateAsync(
        CreateShortLinkRequest request,
        IShortLinkService service,
        ShortUrlBuilder urls,
        TimeProvider time,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        var result = await service.CreateAsync(
            new CreateShortLinkCommand(request.Url, request.Alias, request.ExpiresAt),
            cancellationToken);
        if (!result.IsSuccess)
        {
            return result.Error!.ToProblem(http);
        }

        var (link, created) = result.Value;
        var response = ToResponse(link, urls, time);
        return created
            ? Results.Created($"{LinksRoute}/{Uri.EscapeDataString(link.Code)}", response)
            : Results.Ok(response);
    }

    private static async Task<IResult> ListAsync(
        IShortLinkService service,
        ShortUrlBuilder urls,
        TimeProvider time,
        HttpContext http,
        CancellationToken cancellationToken,
        int? page = null,
        int? pageSize = null)
    {
        var result = await service.ListAsync(page, pageSize, cancellationToken);
        return result.IsSuccess
            ? Results.Ok(PagedResponse<ShortLinkResponse>.From(result.Value, l => ToResponse(l, urls, time)))
            : result.Error!.ToProblem(http);
    }

    private static async Task<IResult> GetAsync(
        string code,
        IShortLinkService service,
        ShortUrlBuilder urls,
        TimeProvider time,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        var result = await service.GetAsync(code, cancellationToken);
        return result.IsSuccess ? Results.Ok(ToResponse(result.Value, urls, time)) : result.Error!.ToProblem(http);
    }

    private static async Task<IResult> DeactivateAsync(
        string code,
        IShortLinkService service,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        var result = await service.DeactivateAsync(code, cancellationToken);
        return result.IsSuccess ? Results.NoContent() : result.Error!.ToProblem(http);
    }

    private static async Task<IResult> GetStatisticsAsync(
        string code,
        IShortLinkService service,
        HttpContext http,
        CancellationToken cancellationToken,
        DateTimeOffset? from = null,
        DateTimeOffset? to = null)
    {
        var result = await service.GetStatisticsAsync(code, from, to, cancellationToken);
        return result.IsSuccess ? Results.Ok(result.Value) : result.Error!.ToProblem(http);
    }

    private static async Task<IResult> ResolveAsync(
        string code,
        IShortLinkService service,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        var context = new ClickContext(http.Request.Headers.Referer.ToString(), http.Request.Headers.UserAgent.ToString());
        var result = await service.ResolveAsync(code, context, cancellationToken);
        if (!result.IsSuccess)
        {
            return result.Error!.ToProblem(http);
        }

        // Never cached by clients or proxies: every lookup is counted and deactivation takes effect immediately.
        http.Response.Headers.CacheControl = "no-store, no-cache, must-revalidate";
        return Results.Ok(result.Value.OriginalUrl);
    }

    private static ShortLinkResponse ToResponse(ShortLink link, ShortUrlBuilder urls, TimeProvider time) =>
        ShortLinkResponse.From(link, urls.Build(link.Code), time.GetUtcNow());
}
