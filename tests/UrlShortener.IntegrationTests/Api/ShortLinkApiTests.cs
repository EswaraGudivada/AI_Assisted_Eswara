using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using UrlShortener.Api.Contracts;
using UrlShortener.Core.Models;

namespace UrlShortener.IntegrationTests.Api;

public class ShortLinkApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string Links = "/api/v1/links";
    private readonly HttpClient _client = factory.CreateNonRedirectingClient();

    [Fact]
    public async Task Post_creates_a_link_and_returns_201_with_location()
    {
        var response = await _client.PostAsJsonAsync(Links, new CreateShortLinkRequest("https://example.com/create"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ShortLinkResponse>();
        Assert.NotNull(body);
        Assert.Equal(7, body.Code.Length);
        Assert.Equal($"https://sho.rt/{body.Code}", body.ShortUrl);
        Assert.Equal("https://example.com/create", body.TargetUrl);
        Assert.Equal("active", body.Status);
        Assert.False(body.IsCustom);
        Assert.Equal($"{Links}/{body.Code}", response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task Post_with_the_same_url_returns_200_and_the_existing_link()
    {
        var first = await _client.CreateLinkAsync("https://example.com/dedupe");

        var response = await _client.PostAsJsonAsync(Links, new CreateShortLinkRequest("https://example.com/dedupe"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(first.Code, (await response.Content.ReadFromJsonAsync<ShortLinkResponse>())!.Code);
    }

    [Theory]
    [InlineData("ftp://example.com", "url_invalid")]
    [InlineData("", "url_invalid")]
    [InlineData("https://sho.rt/loop", "url_host_denied")]
    [InlineData("https://cdn.evil.example/x", "url_host_denied")]
    [InlineData("http://localhost:8080/admin", "url_private_target")]
    [InlineData("http://169.254.169.254/latest/meta-data/", "url_private_target")]
    [InlineData("http://192.168.0.10/", "url_private_target")]
    public async Task Post_with_an_unacceptable_url_returns_422_problem_details(string url, string errorCode)
    {
        var response = await _client.PostAsJsonAsync(Links, new CreateShortLinkRequest(url));

        await AssertProblemAsync(response, HttpStatusCode.UnprocessableEntity, errorCode);
    }

    [Fact]
    public async Task Post_with_a_malformed_body_returns_400()
    {
        using var content = new StringContent("{ not json", System.Text.Encoding.UTF8, "application/json");

        var response = await _client.PostAsync(Links, content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Post_with_a_taken_alias_returns_409()
    {
        await _client.CreateLinkAsync("https://example.com/one", "taken-alias");

        var response = await _client.PostAsJsonAsync(Links, new CreateShortLinkRequest("https://example.com/two", "taken-alias"));

        await AssertProblemAsync(response, HttpStatusCode.Conflict, "alias_taken");
    }

    [Fact]
    public async Task Post_with_a_reserved_alias_returns_422()
    {
        var response = await _client.PostAsJsonAsync(Links, new CreateShortLinkRequest("https://example.com/r", "swagger"));

        await AssertProblemAsync(response, HttpStatusCode.UnprocessableEntity, "alias_reserved");
    }

    [Fact]
    public async Task Lookup_returns_200_with_the_original_url_and_is_never_cached()
    {
        var link = await _client.CreateLinkAsync("https://example.com/target?x=1", "lookup-test");

        var response = await _client.GetAsync($"/{link.Code}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Null(response.Headers.Location);
        Assert.Equal("https://example.com/target?x=1", await response.Content.ReadFromJsonAsync<string>());
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.True(response.Headers.CacheControl?.NoCache);
        Assert.True(response.Headers.CacheControl?.MustRevalidate);
    }

    [Theory]
    [InlineData("/nope123")]
    [InlineData("/favicon.ico")]
    public async Task Lookup_for_an_unknown_code_returns_404(string path)
    {
        var response = await _client.GetAsync(path);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task A_code_looked_up_before_it_exists_resolves_once_created()
    {
        var before = await _client.GetAsync("/later-01");
        await _client.CreateLinkAsync("https://example.com/later", "later-01");
        var after = await _client.GetAsync("/later-01");

        Assert.Equal(HttpStatusCode.NotFound, before.StatusCode);
        Assert.Equal(HttpStatusCode.OK, after.StatusCode);
    }

    [Fact]
    public async Task Deleted_links_return_410_on_lookup_remain_queryable_and_delete_is_repeatable()
    {
        var link = await _client.CreateLinkAsync("https://example.com/delete-me", "delete-me");
        await _client.GetAsync($"/{link.Code}");

        var delete = await _client.DeleteAsync($"{Links}/{link.Code}");
        var again = await _client.DeleteAsync($"{Links}/{link.Code}");
        var lookup = await _client.GetAsync($"/{link.Code}");
        var details = await _client.GetFromJsonAsync<ShortLinkResponse>($"{Links}/{link.Code}");

        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, again.StatusCode);
        await AssertProblemAsync(lookup, HttpStatusCode.Gone, "link_deactivated");
        Assert.Equal("deactivated", details!.Status);
    }

    [Fact]
    public async Task Links_cannot_be_created_with_an_expiry_in_the_past()
    {
        var response = await _client.PostAsJsonAsync(
            Links,
            new CreateShortLinkRequest("https://example.com/expired", ExpiresAt: DateTimeOffset.UtcNow.AddMinutes(-1)));

        await AssertProblemAsync(response, HttpStatusCode.UnprocessableEntity, "expiry_invalid");
    }

    [Fact]
    public async Task Delete_of_an_unknown_code_returns_404()
    {
        var response = await _client.DeleteAsync($"{Links}/unknown-code");

        await AssertProblemAsync(response, HttpStatusCode.NotFound, "link_not_found");
    }

    [Fact]
    public async Task Stats_reflect_lookups_asynchronously_and_store_only_host_and_browser_family()
    {
        var link = await _client.CreateLinkAsync("https://example.com/stats", "stats-test");
        for (var i = 0; i < 3; i++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"/{link.Code}");
            request.Headers.Referrer = new Uri("https://news.example/item/1?utm=secret");
            request.Headers.UserAgent.ParseAdd("curl/8.5.0");
            await _client.SendAsync(request);
        }

        var from = DateTimeOffset.UtcNow.AddDays(-6).ToString("O");
        var stats = await ApiTestHelpers.EventuallyAsync(
            () => _client.GetFromJsonAsync<LinkStatistics>($"{Links}/{link.Code}/stats?from={Uri.EscapeDataString(from)}")!,
            s => s!.TotalClicks == 3);

        Assert.Equal(3, stats!.ClicksInRange);
        Assert.Equal(7, stats.Daily.Count);
        Assert.Equal(3, stats.Daily[^1].Clicks);
        Assert.Equal(new ReferrerClickCount("news.example", 3), Assert.Single(stats.TopReferrers));
    }

    [Theory]
    [InlineData("from=2026-02-01T00:00:00Z&to=2026-01-01T00:00:00Z", "range_inverted")]
    [InlineData("from=2024-01-01T00:00:00Z&to=2026-01-01T00:00:00Z", "range_too_wide")]
    public async Task Stats_with_an_invalid_range_return_422(string query, string errorCode)
    {
        var link = await _client.CreateLinkAsync("https://example.com/stats-window");

        var response = await _client.GetAsync($"{Links}/{link.Code}/stats?{query}");

        await AssertProblemAsync(response, HttpStatusCode.UnprocessableEntity, errorCode);
    }

    [Fact]
    public async Task List_supports_paging_and_validates_page_size()
    {
        await _client.CreateLinkAsync("https://example.com/list-1");
        await _client.CreateLinkAsync("https://example.com/list-2");

        var page = await _client.GetFromJsonAsync<PagedResponse<ShortLinkResponse>>($"{Links}?page=1&pageSize=1");
        var invalid = await _client.GetAsync($"{Links}?pageSize=1000");

        Assert.Single(page!.Items);
        Assert.True(page.TotalCount >= 2);
        Assert.True(page.HasNextPage);
        await AssertProblemAsync(invalid, HttpStatusCode.UnprocessableEntity, "paging_invalid");
    }

    [Fact]
    public async Task Health_endpoint_reports_each_check_as_json()
    {
        var response = await _client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("Healthy", body.RootElement.GetProperty("status").GetString());
        var checks = body.RootElement.GetProperty("checks");
        Assert.Equal("Healthy", checks.GetProperty("database").GetProperty("status").GetString());
        Assert.Equal("Healthy", checks.GetProperty("click-queue").GetProperty("status").GetString());
    }

    [Fact]
    public async Task OpenApi_document_describes_the_endpoints()
    {
        var json = await _client.GetStringAsync("/openapi/v1.json");

        using var document = JsonDocument.Parse(json);
        Assert.Equal("URL Shortener API", document.RootElement.GetProperty("info").GetProperty("title").GetString());
        var paths = document.RootElement.GetProperty("paths");
        Assert.True(paths.TryGetProperty(Links, out _));
        Assert.True(paths.TryGetProperty($"{Links}/{{code}}/stats", out _));
        Assert.True(paths.TryGetProperty("/{code}", out _));
    }

    [Fact]
    public async Task Swagger_ui_is_served_when_enabled_and_points_at_the_openapi_document()
    {
        var page = await _client.GetAsync("/swagger/index.html");
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.Contains("URL Shortener API", await page.Content.ReadAsStringAsync());

        var config = await _client.GetStringAsync("/swagger/index.js");
        Assert.Contains("/openapi/v1.json", config);
    }

    [Fact]
    public async Task Responses_include_security_headers()
    {
        var response = await _client.GetAsync("/health");

        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").Single());
    }

    [Fact]
    public async Task A_safe_caller_correlation_id_is_echoed_and_included_in_errors()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/missing-1");
        request.Headers.Add("X-Correlation-Id", "trace-abc_123");

        var response = await _client.SendAsync(request);

        Assert.Equal("trace-abc_123", response.Headers.GetValues("X-Correlation-Id").Single());
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Equal("trace-abc_123", problem!.Extensions["correlationId"]?.ToString());
    }

    [Theory]
    [InlineData("has spaces")]
    [InlineData("semi;colon")]
    [InlineData("0123456789012345678901234567890123456789012345678901234567890123456789")]
    public async Task An_unsafe_correlation_id_is_replaced(string supplied)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/health");
        request.Headers.TryAddWithoutValidation("X-Correlation-Id", supplied);

        var response = await _client.SendAsync(request);

        var echoed = response.Headers.GetValues("X-Correlation-Id").Single();
        Assert.NotEqual(supplied, echoed);
        Assert.False(string.IsNullOrEmpty(echoed));
    }

    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string errorCode)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Equal((int)status, problem!.Status);
        Assert.Equal(errorCode, problem.Extensions["code"]?.ToString());
        Assert.False(string.IsNullOrEmpty(problem.Extensions["correlationId"]?.ToString()));
    }
}
