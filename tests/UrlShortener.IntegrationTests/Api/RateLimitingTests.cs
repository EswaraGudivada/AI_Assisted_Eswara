using System.Net;
using System.Net.Http.Json;
using UrlShortener.Api.Contracts;

namespace UrlShortener.IntegrationTests.Api;

public sealed class RateLimitingTests : IDisposable
{
    private readonly ApiFactory _factory = new();

    public RateLimitingTests()
    {
        _factory.Settings["RateLimiting:CreatePermitsPerMinute"] = "2";
    }

    [Fact]
    public async Task Link_creation_is_rate_limited_per_client()
    {
        var client = _factory.CreateClient();

        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 3; i++)
        {
            var response = await client.PostAsJsonAsync("/api/links", new CreateShortLinkRequest($"https://example.com/rl/{i}"));
            statuses.Add(response.StatusCode);
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                Assert.True(response.Headers.Contains("Retry-After"));
            }
        }

        Assert.Equal([HttpStatusCode.Created, HttpStatusCode.Created, HttpStatusCode.TooManyRequests], statuses);
    }

    [Fact]
    public async Task Redirects_use_a_separate_budget()
    {
        var client = _factory.CreateNonRedirectingClient();
        var link = await client.CreateLinkAsync("https://example.com/rl-redirect");
        await client.CreateLinkAsync("https://example.com/rl-redirect-2");

        var response = await client.GetAsync($"/{link.Code}");

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
    }

    public void Dispose() => _factory.Dispose();
}
