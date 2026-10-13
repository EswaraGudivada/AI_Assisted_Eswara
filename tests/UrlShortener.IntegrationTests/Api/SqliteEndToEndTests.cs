using System.Net;
using System.Net.Http.Json;
using Microsoft.Data.Sqlite;
using UrlShortener.Api.Contracts;
using UrlShortener.Core.Models;

namespace UrlShortener.IntegrationTests.Api;

public sealed class SqliteEndToEndTests : IDisposable
{
    private readonly ApiFactory _factory = new();

    [Fact]
    public async Task Full_lifecycle_works_against_sqlite()
    {
        var client = _factory.CreateClient();

        var link = await client.CreateLinkAsync("https://example.com/sqlite", "sqlite-e2e");
        var lookup = await client.GetAsync("/sqlite-e2e");
        var stats = await ApiTestHelpers.EventuallyAsync(
            () => client.GetFromJsonAsync<LinkStatistics>("/api/v1/links/sqlite-e2e/stats")!,
            s => s!.TotalClicks == 1);
        var conflict = await client.PostAsJsonAsync("/api/v1/links", new CreateShortLinkRequest("https://example.com/x", "sqlite-e2e"));
        var delete = await client.DeleteAsync("/api/v1/links/sqlite-e2e");
        var gone = await client.GetAsync("/sqlite-e2e");

        Assert.Equal("sqlite-e2e", link.Code);
        Assert.Equal(HttpStatusCode.OK, lookup.StatusCode);
        Assert.NotNull(stats!.LastAccessedAt);
        Assert.Equal(1, stats.ClicksInRange);
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
        Assert.Equal(HttpStatusCode.Gone, gone.StatusCode);
    }

    [Fact]
    public async Task Startup_applies_the_ef_core_migrations()
    {
        _ = _factory.CreateClient();

        await using var connection = new SqliteConnection($"Data Source={_factory.DatabasePath}");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT MigrationId FROM __EFMigrationsHistory";
        var migration = (string?)await command.ExecuteScalarAsync();

        Assert.EndsWith("_InitialCreate", migration, StringComparison.Ordinal);
    }

    public void Dispose() => _factory.Dispose();
}
