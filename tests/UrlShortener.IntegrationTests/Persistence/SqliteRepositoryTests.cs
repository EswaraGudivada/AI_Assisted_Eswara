using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using UrlShortener.Core.Domain;
using UrlShortener.Infrastructure.Persistence;

namespace UrlShortener.IntegrationTests.Persistence;

public sealed class SqliteRepositoryTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 3, 1, 8, 0, 0, TimeSpan.Zero);
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private DbContextOptions<UrlShortenerDbContext> _options = null!;

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();
        _options = new DbContextOptionsBuilder<UrlShortenerDbContext>().UseSqlite(_connection).Options;
        await using var db = new UrlShortenerDbContext(_options);
        await db.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync() => await _connection.DisposeAsync();

    [Fact]
    public async Task TryAdd_rejects_duplicate_codes()
    {
        await using var db = new UrlShortenerDbContext(_options);
        var repository = new EfShortLinkRepository(db);

        Assert.True(await repository.TryAddAsync(Link("dup")));
        Assert.False(await repository.TryAddAsync(Link("dup")));
    }

    [Fact]
    public async Task Unique_index_wins_a_race_that_bypasses_the_existence_check()
    {
        await using (var seed = new UrlShortenerDbContext(_options))
        {
            seed.ShortLinks.Add(Link("raced"));
            await seed.SaveChangesAsync();
        }

        await using var db = new UrlShortenerDbContext(_options);
        db.ShortLinks.Add(Link("raced"));

        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Equal(19, Assert.IsType<SqliteException>(ex.InnerException).SqliteErrorCode);
    }

    [Fact]
    public async Task Round_trips_all_fields_including_utc_timestamps()
    {
        await using (var db = new UrlShortenerDbContext(_options))
        {
            await new EfShortLinkRepository(db).TryAddAsync(Link("rt") with { ExpiresAt = Now.AddDays(1), IsCustomAlias = true });
        }

        await using var readDb = new UrlShortenerDbContext(_options);
        var link = await new EfShortLinkRepository(readDb).GetByCodeAsync("rt");

        Assert.Equal(Now, link!.CreatedAt);
        Assert.Equal(Now.AddDays(1), link.ExpiresAt);
        Assert.True(link.IsCustomAlias);
    }

    [Fact]
    public async Task FindReusable_ignores_aliases_inactive_and_expiring_links()
    {
        await using var db = new UrlShortenerDbContext(_options);
        var repository = new EfShortLinkRepository(db);
        const string url = "https://same.example/";
        await repository.TryAddAsync(Link("alias1", url) with { IsCustomAlias = true });
        await repository.TryAddAsync(Link("expiring", url) with { ExpiresAt = Now.AddDays(1) });
        await repository.TryAddAsync(Link("inactive", url) with { IsActive = false });
        Assert.Null(await repository.FindReusableByUrlAsync(url));

        await repository.TryAddAsync(Link("reusable", url));

        Assert.Equal("reusable", (await repository.FindReusableByUrlAsync(url))!.Code);
    }

    [Fact]
    public async Task Deactivate_and_list()
    {
        await using var db = new UrlShortenerDbContext(_options);
        var repository = new EfShortLinkRepository(db);
        await repository.TryAddAsync(Link("old") with { CreatedAt = Now.AddHours(-1) });
        await repository.TryAddAsync(Link("new"));

        Assert.True(await repository.DeactivateAsync("old"));
        Assert.False(await repository.DeactivateAsync("missing"));
        var page = await repository.ListAsync(1, 10);

        Assert.Equal(["new", "old"], page.Items.Select(l => l.Code));
        Assert.False(page.Items[1].IsActive);
        Assert.Equal(2, page.TotalCount);
    }

    [Fact]
    public async Task RecordBatch_persists_events_and_increments_counters()
    {
        await using (var db = new UrlShortenerDbContext(_options))
        {
            await new EfShortLinkRepository(db).TryAddAsync(Link("clk"));
            var clicks = new EfClickEventRepository(db);
            await clicks.RecordBatchAsync([Click("clk", Now.AddDays(-2)), Click("clk", Now, "ref.example")]);
            await clicks.RecordBatchAsync([Click("clk", Now.AddMinutes(1))]);
        }

        await using var readDb = new UrlShortenerDbContext(_options);
        var link = await new EfShortLinkRepository(readDb).GetByCodeAsync("clk");
        var recent = await new EfClickEventRepository(readDb).GetSinceAsync("clk", Now.AddDays(-1));

        Assert.Equal(3, link!.ClickCount);
        Assert.Equal(Now.AddMinutes(1), link.LastAccessedAt);
        Assert.Equal(2, recent.Count);
        Assert.Contains(recent, c => c.Referrer == "ref.example");
    }

    private static ShortLink Link(string code, string url = "https://example.com/") =>
        new() { Code = code, OriginalUrl = url, CreatedAt = Now };

    private static ClickEvent Click(string code, DateTimeOffset at, string? referrer = null) =>
        new() { Code = code, OccurredAt = at, Referrer = referrer };
}
