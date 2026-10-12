using System.Net;
using System.Net.Http.Json;
using UrlShortener.Api.Contracts;
using UrlShortener.Core.Models;

namespace UrlShortener.IntegrationTests.Api;

public sealed class SqliteEndToEndTests : IDisposable
{
    private readonly ApiFactory _factory = new ApiFactory().UseSqliteFile();

    [Fact]
    public async Task Full_lifecycle_works_against_sqlite()
    {
        var client = _factory.CreateNonRedirectingClient();

        var link = await client.CreateLinkAsync("https://example.com/sqlite", "sqlite-e2e");
        var redirect = await client.GetAsync("/sqlite-e2e");
        var stats = await ApiTestHelpers.EventuallyAsync(
            () => client.GetFromJsonAsync<LinkStatistics>("/api/links/sqlite-e2e/stats")!,
            s => s!.TotalClicks == 1);
        var conflict = await client.PostAsJsonAsync("/api/links", new CreateShortLinkRequest("https://example.com/x", "sqlite-e2e"));
        var delete = await client.DeleteAsync("/api/links/sqlite-e2e");
        var gone = await client.GetAsync("/sqlite-e2e");
        var health = await client.GetAsync("/health");

        Assert.Equal("sqlite-e2e", link.Code);
        Assert.Equal(HttpStatusCode.Found, redirect.StatusCode);
        Assert.NotNull(stats!.LastAccessedAt);
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
        Assert.Equal(HttpStatusCode.Gone, gone.StatusCode);
        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
    }

    public void Dispose() => _factory.Dispose();
}
