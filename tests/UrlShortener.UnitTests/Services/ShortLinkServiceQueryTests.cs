using UrlShortener.Core.Common;
using UrlShortener.Core.Models;
using UrlShortener.Core.Services;
using UrlShortener.UnitTests.Fakes;

namespace UrlShortener.UnitTests.Services;

public class ShortLinkServiceQueryTests
{
    [Fact]
    public async Task Statistics_include_zero_filled_daily_counts_and_top_referrers()
    {
        var fixture = new ServiceFixture();
        var code = (await fixture.Service.CreateAsync(new CreateShortLinkCommand("https://example.com"))).Value.Link.Code;

        await fixture.Service.ResolveAsync(code, new ClickContext("https://a.example/1", null));
        fixture.Time.Advance(TimeSpan.FromDays(2));
        await fixture.Service.ResolveAsync(code, new ClickContext("https://a.example/2", null));
        await fixture.Service.ResolveAsync(code, new ClickContext("https://b.example/", null));
        await fixture.Service.ResolveAsync(code, ClickContext.Empty);
        await fixture.FlushClicksAsync();

        var from = new DateTimeOffset(2026, 1, 15, 0, 0, 0, TimeSpan.Zero);
        var stats = (await fixture.Service.GetStatisticsAsync(code, from, null)).Value;

        Assert.Equal(4, stats.TotalClicks);
        Assert.Equal(4, stats.ClicksInRange);
        Assert.Equal(from, stats.From);
        Assert.Equal(fixture.Time.GetUtcNow(), stats.To);
        Assert.Equal(fixture.Time.GetUtcNow(), stats.LastAccessedAt);
        Assert.Equal(
            [new DailyClickCount(new DateOnly(2026, 1, 15), 1), new DailyClickCount(new DateOnly(2026, 1, 16), 0), new DailyClickCount(new DateOnly(2026, 1, 17), 3)],
            stats.Daily);
        Assert.Equal(new ReferrerClickCount("a.example", 2), stats.TopReferrers[0]);
        Assert.Contains(new ReferrerClickCount(ShortLinkService.DirectReferrer, 1), stats.TopReferrers);
    }

    [Fact]
    public async Task Default_window_is_the_last_30_days_and_total_still_counts_older_clicks()
    {
        var fixture = new ServiceFixture();
        var code = (await fixture.Service.CreateAsync(new CreateShortLinkCommand("https://example.com"))).Value.Link.Code;
        await fixture.Service.ResolveAsync(code, ClickContext.Empty);
        await fixture.FlushClicksAsync();
        fixture.Time.Advance(TimeSpan.FromDays(40));

        var stats = (await fixture.Service.GetStatisticsAsync(code, null, null)).Value;

        Assert.Equal(1, stats.TotalClicks);
        Assert.Equal(0, stats.ClicksInRange);
        Assert.Equal(fixture.Time.GetUtcNow().AddDays(-30), stats.From);
        Assert.Equal(31, stats.Daily.Count);
    }

    [Fact]
    public async Task Statistics_reject_an_inverted_range()
    {
        var fixture = new ServiceFixture();
        var code = (await fixture.Service.CreateAsync(new CreateShortLinkCommand("https://example.com"))).Value.Link.Code;
        var now = fixture.Time.GetUtcNow();

        var result = await fixture.Service.GetStatisticsAsync(code, now, now.AddDays(-1));

        Assert.Equal("range_inverted", result.Error!.Code);
        Assert.Equal(ErrorType.Validation, result.Error.Type);
    }

    [Fact]
    public async Task Statistics_reject_a_range_wider_than_the_maximum()
    {
        var fixture = new ServiceFixture();
        var code = (await fixture.Service.CreateAsync(new CreateShortLinkCommand("https://example.com"))).Value.Link.Code;
        var now = fixture.Time.GetUtcNow();

        var result = await fixture.Service.GetStatisticsAsync(code, now.AddDays(-367), now);

        Assert.Equal("range_too_wide", result.Error!.Code);
    }

    [Fact]
    public async Task Statistics_for_unknown_code_return_not_found()
    {
        var fixture = new ServiceFixture();

        var result = await fixture.Service.GetStatisticsAsync("missing", null, null);

        Assert.Equal(ErrorType.NotFound, result.Error!.Type);
    }

    [Fact]
    public async Task List_returns_newest_first_with_paging()
    {
        var fixture = new ServiceFixture();
        foreach (var i in Enumerable.Range(1, 5))
        {
            await fixture.Service.CreateAsync(new CreateShortLinkCommand($"https://example.com/{i}"));
            fixture.Time.Advance(TimeSpan.FromMinutes(1));
        }

        var page = (await fixture.Service.ListAsync(2, 2)).Value;

        Assert.Equal(5, page.TotalCount);
        Assert.Equal(3, page.TotalPages);
        Assert.True(page.HasNextPage);
        Assert.Equal(["https://example.com/3", "https://example.com/2"], page.Items.Select(l => l.OriginalUrl));
    }

    [Fact]
    public async Task List_uses_the_configured_default_page_size()
    {
        var fixture = new ServiceFixture();

        var page = (await fixture.Service.ListAsync(null, null)).Value;

        Assert.Equal(1, page.Page);
        Assert.Equal(fixture.Options.DefaultPageSize, page.PageSize);
        Assert.False(page.HasNextPage);
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(1, 0)]
    [InlineData(1, 101)]
    public async Task List_rejects_invalid_paging(int page, int pageSize)
    {
        var fixture = new ServiceFixture();

        var result = await fixture.Service.ListAsync(page, pageSize);

        Assert.Equal("paging_invalid", result.Error!.Code);
    }

    [Fact]
    public async Task Get_returns_metadata_including_inactive_links()
    {
        var fixture = new ServiceFixture();
        var code = (await fixture.Service.CreateAsync(new CreateShortLinkCommand("https://example.com"))).Value.Link.Code;
        await fixture.Service.DeactivateAsync(code);

        var result = await fixture.Service.GetAsync(code);

        Assert.False(result.Value.IsActive);
    }
}
