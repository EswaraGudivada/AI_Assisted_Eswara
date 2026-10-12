using UrlShortener.Api.Contracts;
using UrlShortener.Api.Infrastructure;
using UrlShortener.Core.Models;
using UrlShortener.Core.Services;

namespace UrlShortener.Api.Endpoints;

public static class ShortLinkEndpoints
{
    public static IEndpointRouteBuilder MapShortLinkEndpoints(this IEndpointRouteBuilder app)
    {
        var links = app.MapGroup("/api/links").WithTags("Links");

        links.MapPost("/", CreateAsync)
            .RequireRateLimiting(RateLimiting.CreatePolicy)
            .WithName("CreateShortLink")
            .WithSummary("Create a short link")
            .WithDescription("Returns 201 for a new link, or 200 with the existing link when an identical URL was already shortened (deduplication).")
            .Produces<ShortLinkResponse>(StatusCodes.Status201Created)
            .Produces<ShortLinkResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        links.MapGet("/", ListAsync)
            .WithName("ListShortLinks")
            .WithSummary("List short links, newest first")
            .Produces<PagedResponse<ShortLinkResponse>>()
            .ProducesProblem(StatusCodes.Status400BadRequest);

        links.MapGet("/{code}", GetAsync)
            .WithName("GetShortLink")
            .WithSummary("Get short link metadata")
            .Produces<ShortLinkResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        links.MapDelete("/{code}", DeactivateAsync)
            .WithName("DeactivateShortLink")
            .WithSummary("Deactivate a short link (soft delete; the code is never reused)")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound);

        links.MapGet("/{code}/stats", GetStatisticsAsync)
            .WithName("GetShortLinkStatistics")
            .WithSummary("Click analytics for a short link")
            .Produces<LinkStatistics>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound);

        app.MapGet("/{code}", RedirectAsync)
            .RequireRateLimiting(RateLimiting.RedirectPolicy)
            .WithTags("Redirect")
            .WithName("RedirectShortLink")
            .WithSummary("Redirect to the original URL (302)")
            .Produces(StatusCodes.Status302Found)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status410Gone)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);

        return app;
    }

    private static async Task<IResult> CreateAsync(
        CreateShortLinkRequest request,
        IShortLinkService service,
        ShortUrlBuilder urls,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        var result = await service.CreateAsync(
            new CreateShortLinkCommand(request.Url, request.CustomAlias, request.ExpiresAt),
            cancellationToken);
        if (!result.IsSuccess)
        {
            return result.Error!.ToProblem();
        }

        var (link, created) = result.Value;
        var response = ShortLinkResponse.From(link, urls.Build(http.Request, link.Code));
        return created
            ? Results.Created($"/api/links/{Uri.EscapeDataString(link.Code)}", response)
            : Results.Ok(response);
    }

    private static async Task<IResult> ListAsync(
        IShortLinkService service,
        ShortUrlBuilder urls,
        HttpContext http,
        CancellationToken cancellationToken,
        int page = 1,
        int pageSize = 20)
    {
        var result = await service.ListAsync(page, pageSize, cancellationToken);
        if (!result.IsSuccess)
        {
            return result.Error!.ToProblem();
        }

        var paged = result.Value;
        var items = paged.Items.Select(l => ShortLinkResponse.From(l, urls.Build(http.Request, l.Code))).ToList();
        return Results.Ok(new PagedResponse<ShortLinkResponse>(items, paged.Page, paged.PageSize, paged.TotalCount));
    }

    private static async Task<IResult> GetAsync(
        string code,
        IShortLinkService service,
        ShortUrlBuilder urls,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        var result = await service.GetAsync(code, cancellationToken);
        return result.IsSuccess
            ? Results.Ok(ShortLinkResponse.From(result.Value, urls.Build(http.Request, result.Value.Code)))
            : result.Error!.ToProblem();
    }

    private static async Task<IResult> DeactivateAsync(string code, IShortLinkService service, CancellationToken cancellationToken)
    {
        var result = await service.DeactivateAsync(code, cancellationToken);
        return result.IsSuccess ? Results.NoContent() : result.Error!.ToProblem();
    }

    private static async Task<IResult> GetStatisticsAsync(
        string code,
        IShortLinkService service,
        CancellationToken cancellationToken,
        int days = 7)
    {
        var result = await service.GetStatisticsAsync(code, days, cancellationToken);
        return result.IsSuccess ? Results.Ok(result.Value) : result.Error!.ToProblem();
    }

    private static async Task<IResult> RedirectAsync(
        string code,
        IShortLinkService service,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        var context = new ClickContext(http.Request.Headers.Referer.ToString(), http.Request.Headers.UserAgent.ToString());
        var result = await service.ResolveAsync(code, context, cancellationToken);
        if (!result.IsSuccess)
        {
            return result.Error!.ToProblem();
        }

        // 302 + no-cache (not 301) so browsers re-hit the service: clicks are counted and deactivation takes effect.
        http.Response.Headers.CacheControl = "private, no-cache";
        return Results.Redirect(result.Value.OriginalUrl, permanent: false);
    }
}
