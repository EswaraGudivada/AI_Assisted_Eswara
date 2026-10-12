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

        var stats = (await fixture.Service.GetStatisticsAsync(code, 3)).Value;

        Assert.Equal(4, stats.TotalClicks);
        Assert.Equal(fixture.Time.GetUtcNow(), stats.LastAccessedAt);
        Assert.Equal(
            [new DailyClickCount(new DateOnly(2026, 1, 15), 1), new DailyClickCount(new DateOnly(2026, 1, 16), 0), new DailyClickCount(new DateOnly(2026, 1, 17), 3)],
            stats.DailyClicks);
        Assert.Equal(new ReferrerCount("a.example", 2), stats.TopReferrers[0]);
        Assert.Contains(new ReferrerCount(ShortLinkService.DirectReferrer, 1), stats.TopReferrers);
    }

    [Fact]
    public async Task Statistics_window_excludes_older_clicks_but_total_includes_them()
    {
        var fixture = new ServiceFixture();
        var code = (await fixture.Service.CreateAsync(new CreateShortLinkCommand("https://example.com"))).Value.Link.Code;
        await fixture.Service.ResolveAsync(code, ClickContext.Empty);
        fixture.Time.Advance(TimeSpan.FromDays(10));
        await fixture.FlushClicksAsync();

        var stats = (await fixture.Service.GetStatisticsAsync(code, 7)).Value;

        Assert.Equal(1, stats.TotalClicks);
        Assert.Equal(7, stats.DailyClicks.Count);
        Assert.All(stats.DailyClicks, d => Assert.Equal(0, d.Clicks));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(91)]
    public async Task Statistics_reject_out_of_range_windows(int days)
    {
        var fixture = new ServiceFixture();

        var result = await fixture.Service.GetStatisticsAsync("whatever", days);

        Assert.Equal("stats.window_invalid", result.Error!.Code);
    }

    [Fact]
    public async Task Statistics_for_unknown_code_return_not_found()
    {
        var fixture = new ServiceFixture();

        var result = await fixture.Service.GetStatisticsAsync("missing", 7);

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
        Assert.Equal(["https://example.com/3", "https://example.com/2"], page.Items.Select(l => l.OriginalUrl));
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(1, 0)]
    [InlineData(1, 101)]
    public async Task List_rejects_invalid_paging(int page, int pageSize)
    {
        var fixture = new ServiceFixture();

        var result = await fixture.Service.ListAsync(page, pageSize);

        Assert.Equal("paging.invalid", result.Error!.Code);
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
