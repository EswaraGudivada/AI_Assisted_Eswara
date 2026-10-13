using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using UrlShortener.Api.Contracts;

namespace UrlShortener.IntegrationTests.Api;

public sealed class RateLimitingTests : IDisposable
{
    private readonly ApiFactory _factory = new();

    public RateLimitingTests()
    {
        _factory.Settings["RateLimit:WritesPerMinutePerIp"] = "2";
    }

    [Fact]
    public async Task Writes_are_limited_per_client_with_a_problem_body_and_retry_after()
    {
        var client = _factory.CreateClient();

        var statuses = new List<HttpStatusCode>();
        HttpResponseMessage? rejected = null;
        for (var i = 0; i < 3; i++)
        {
            var response = await client.PostAsJsonAsync("/api/v1/links", new CreateShortLinkRequest($"https://example.com/rl/{i}"));
            statuses.Add(response.StatusCode);
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                rejected = response;
            }
        }

        Assert.Equal([HttpStatusCode.Created, HttpStatusCode.Created, HttpStatusCode.TooManyRequests], statuses);
        Assert.True(rejected!.Headers.Contains("Retry-After"));
        var problem = await rejected.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Equal("rate_limit_exceeded", problem!.Extensions["code"]?.ToString());
        Assert.False(string.IsNullOrEmpty(problem.Extensions["correlationId"]?.ToString()));
    }

    [Fact]
    public async Task Lookups_use_a_separate_budget()
    {
        var client = _factory.CreateClient();
        var link = await client.CreateLinkAsync("https://example.com/rl-lookup");
        await client.CreateLinkAsync("https://example.com/rl-lookup-2");

        var response = await client.GetAsync($"/{link.Code}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    public void Dispose() => _factory.Dispose();
}
