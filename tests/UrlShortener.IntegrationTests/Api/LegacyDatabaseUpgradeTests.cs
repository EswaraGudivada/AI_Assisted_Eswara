using System.Net;
using System.Net.Http.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using UrlShortener.Api.Contracts;
using UrlShortener.Core.Models;

namespace UrlShortener.IntegrationTests.Api;

/// <summary>A database created by the pre-migrations version (EnsureCreated) must start cleanly and keep its data.</summary>
public sealed class LegacyDatabaseUpgradeTests : IDisposable
{
    private readonly ApiFactory _factory = new();

    [Fact]
    public async Task Starts_on_a_database_created_without_migrations_and_keeps_its_data()
    {
        await CreateLegacyDatabaseAsync(_factory.DatabasePath);
        var client = _factory.CreateClient();

        var lookup = await client.GetAsync("/legacy-1");
        var link = await client.GetFromJsonAsync<ShortLinkResponse>("/api/v1/links/legacy-1");
        var stats = await client.GetFromJsonAsync<LinkStatistics>(
            "/api/v1/links/legacy-1/stats?from=2020-01-01T00:00:00Z&to=2020-12-31T00:00:00Z");
        var created = await client.PostAsJsonAsync("/api/v1/links", new CreateShortLinkRequest("https://example.com/new"));

        Assert.Equal(HttpStatusCode.OK, lookup.StatusCode);
        Assert.Equal("https://example.com/legacy", await lookup.Content.ReadFromJsonAsync<string>());
        Assert.True(link!.TotalClicks >= 1);
        Assert.Equal(1, stats!.ClicksInRange);
        Assert.Equal(new ReferrerClickCount("news.example", 1), Assert.Single(stats.TopReferrers));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal("chrome", await ScalarAsync("SELECT UserAgentFamily FROM click_events"));
        Assert.EndsWith("_InitialCreate", await ScalarAsync("SELECT MigrationId FROM __EFMigrationsHistory"), StringComparison.Ordinal);
    }

    public void Dispose() => _factory.Dispose();

    private async Task<string?> ScalarAsync(string sql)
    {
        await using var connection = new SqliteConnection($"Data Source={_factory.DatabasePath}");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return (string?)await command.ExecuteScalarAsync();
    }

    // Schema exactly as the previous version's EnsureCreated produced it (Referrer/UserAgent, no history table).
    private static async Task CreateLegacyDatabaseAsync(string path)
    {
        await using var connection = new SqliteConnection($"Data Source={path};Pooling=False");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        var converter = new DateTimeOffsetToBinaryConverter();
        var createdAt = converter.ConvertToProvider(new DateTimeOffset(2020, 6, 1, 0, 0, 0, TimeSpan.Zero));
        var clickedAt = converter.ConvertToProvider(new DateTimeOffset(2020, 6, 2, 0, 0, 0, TimeSpan.Zero));
        command.CommandText = $"""
            CREATE TABLE "click_events" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_click_events" PRIMARY KEY AUTOINCREMENT,
                "Code" TEXT NOT NULL, "OccurredAt" INTEGER NOT NULL, "Referrer" TEXT NULL, "UserAgent" TEXT NULL);
            CREATE TABLE "short_links" (
                "Id" TEXT NOT NULL CONSTRAINT "PK_short_links" PRIMARY KEY, "Code" TEXT NOT NULL, "OriginalUrl" TEXT NOT NULL,
                "CreatedAt" INTEGER NOT NULL, "ExpiresAt" INTEGER NULL, "IsCustomAlias" INTEGER NOT NULL, "IsActive" INTEGER NOT NULL,
                "ClickCount" INTEGER NOT NULL, "LastAccessedAt" INTEGER NULL);
            CREATE INDEX "IX_click_events_Code_OccurredAt" ON "click_events" ("Code", "OccurredAt");
            CREATE UNIQUE INDEX "IX_short_links_Code" ON "short_links" ("Code");
            CREATE INDEX "IX_short_links_CreatedAt" ON "short_links" ("CreatedAt");
            CREATE INDEX "IX_short_links_OriginalUrl" ON "short_links" ("OriginalUrl");
            INSERT INTO short_links VALUES ('{Guid.NewGuid()}', 'legacy-1', 'https://example.com/legacy', {createdAt}, NULL, 1, 1, 1, {clickedAt});
            INSERT INTO click_events (Code, OccurredAt, Referrer, UserAgent)
                VALUES ('legacy-1', {clickedAt}, 'news.example', 'Mozilla/5.0 (Windows NT 10.0) AppleWebKit/537.36 Chrome/126.0 Safari/537.36');
            """;
        await command.ExecuteNonQueryAsync();
    }
}
