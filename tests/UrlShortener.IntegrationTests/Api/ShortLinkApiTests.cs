using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using UrlShortener.Api.Contracts;
using UrlShortener.Core.Models;

namespace UrlShortener.IntegrationTests.Api;

public class ShortLinkApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private readonly HttpClient _client = factory.CreateNonRedirectingClient();

    [Fact]
    public async Task Post_creates_a_link_and_returns_201_with_location()
    {
        var response = await _client.PostAsJsonAsync("/api/links", new CreateShortLinkRequest("https://example.com/create"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ShortLinkResponse>();
        Assert.NotNull(body);
        Assert.Equal(7, body.Code.Length);
        Assert.Equal($"https://sho.rt/{body.Code}", body.ShortUrl);
        Assert.Equal($"/api/links/{body.Code}", response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task Post_with_the_same_url_returns_200_and_the_existing_link()
    {
        var first = await _client.CreateLinkAsync("https://example.com/dedupe");

        var response = await _client.PostAsJsonAsync("/api/links", new CreateShortLinkRequest("https://example.com/dedupe"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(first.Code, (await response.Content.ReadFromJsonAsync<ShortLinkResponse>())!.Code);
    }

    [Theory]
    [InlineData("ftp://example.com", "url.invalid")]
    [InlineData("https://sho.rt/loop", "url.invalid")]
    [InlineData("", "url.invalid")]
    public async Task Post_with_an_invalid_url_returns_400_problem_details(string url, string errorCode)
    {
        var response = await _client.PostAsJsonAsync("/api/links", new CreateShortLinkRequest(url));

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, errorCode);
    }

    [Fact]
    public async Task Post_with_a_malformed_body_returns_400()
    {
        using var content = new StringContent("{ not json", System.Text.Encoding.UTF8, "application/json");

        var response = await _client.PostAsync("/api/links", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Post_with_a_taken_alias_returns_409()
    {
        await _client.CreateLinkAsync("https://example.com/one", "taken-alias");

        var response = await _client.PostAsJsonAsync("/api/links", new CreateShortLinkRequest("https://example.com/two", "taken-alias"));

        await AssertProblemAsync(response, HttpStatusCode.Conflict, "alias.conflict");
    }

    [Fact]
    public async Task Redirect_returns_302_to_the_original_url_without_permanent_caching()
    {
        var link = await _client.CreateLinkAsync("https://example.com/target?x=1", "redir-test");

        var response = await _client.GetAsync($"/{link.Code}");

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Equal("https://example.com/target?x=1", response.Headers.Location?.ToString());
        Assert.True(response.Headers.CacheControl?.NoCache);
    }

    [Theory]
    [InlineData("/nope123")]
    [InlineData("/favicon.ico")]
    public async Task Redirect_for_an_unknown_code_returns_404(string path)
    {
        var response = await _client.GetAsync(path);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Deleted_links_return_410_on_redirect_but_remain_queryable()
    {
        var link = await _client.CreateLinkAsync("https://example.com/delete-me", "delete-me");
        await _client.GetAsync($"/{link.Code}");

        var delete = await _client.DeleteAsync($"/api/links/{link.Code}");
        var redirect = await _client.GetAsync($"/{link.Code}");
        var metadata = await _client.GetFromJsonAsync<ShortLinkResponse>($"/api/links/{link.Code}");

        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
        await AssertProblemAsync(redirect, HttpStatusCode.Gone, "link.deactivated");
        Assert.False(metadata!.IsActive);
    }

    [Fact]
    public async Task Expired_links_cannot_be_created_in_the_past()
    {
        var response = await _client.PostAsJsonAsync(
            "/api/links",
            new CreateShortLinkRequest("https://example.com/expired", ExpiresAt: DateTimeOffset.UtcNow.AddMinutes(-1)));

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "expiry.invalid");
    }

    [Fact]
    public async Task Delete_of_an_unknown_code_returns_404()
    {
        var response = await _client.DeleteAsync("/api/links/unknown-code");

        await AssertProblemAsync(response, HttpStatusCode.NotFound, "link.not_found");
    }

    [Fact]
    public async Task Stats_reflect_redirects_asynchronously()
    {
        var link = await _client.CreateLinkAsync("https://example.com/stats", "stats-test");
        for (var i = 0; i < 3; i++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"/{link.Code}");
            request.Headers.Referrer = new Uri("https://news.example/item/1");
            await _client.SendAsync(request);
        }

        var stats = await ApiTestHelpers.EventuallyAsync(
            () => _client.GetFromJsonAsync<LinkStatistics>($"/api/links/{link.Code}/stats?days=7")!,
            s => s!.TotalClicks == 3);

        Assert.Equal(7, stats!.DailyClicks.Count);
        Assert.Equal(3, stats.DailyClicks[^1].Clicks);
        Assert.Equal(new ReferrerCount("news.example", 3), Assert.Single(stats.TopReferrers));
    }

    [Fact]
    public async Task Stats_with_an_invalid_window_return_400()
    {
        var link = await _client.CreateLinkAsync("https://example.com/stats-window");

        var response = await _client.GetAsync($"/api/links/{link.Code}/stats?days=0");

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "stats.window_invalid");
    }

    [Fact]
    public async Task List_supports_paging_and_validates_page_size()
    {
        await _client.CreateLinkAsync("https://example.com/list-1");
        await _client.CreateLinkAsync("https://example.com/list-2");

        var page = await _client.GetFromJsonAsync<PagedResponse<ShortLinkResponse>>("/api/links?page=1&pageSize=1");
        var invalid = await _client.GetAsync("/api/links?pageSize=1000");

        Assert.Single(page!.Items);
        Assert.True(page.TotalCount >= 2);
        await AssertProblemAsync(invalid, HttpStatusCode.BadRequest, "paging.invalid");
    }

    [Fact]
    public async Task Health_endpoint_reports_healthy()
    {
        var response = await _client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task OpenApi_document_describes_the_endpoints()
    {
        var json = await _client.GetStringAsync("/openapi/v1.json");

        using var document = JsonDocument.Parse(json);
        var paths = document.RootElement.GetProperty("paths");
        Assert.True(paths.TryGetProperty("/api/links", out _));
        Assert.True(paths.TryGetProperty("/api/links/{code}/stats", out _));
        Assert.True(paths.TryGetProperty("/{code}", out _));
    }

    [Fact]
    public async Task Responses_include_security_headers()
    {
        var response = await _client.GetAsync("/health");

        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").Single());
    }

    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string errorCode)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Equal((int)status, problem!.Status);
        Assert.Equal(errorCode, problem.Extensions["errorCode"]?.ToString());
    }
}
