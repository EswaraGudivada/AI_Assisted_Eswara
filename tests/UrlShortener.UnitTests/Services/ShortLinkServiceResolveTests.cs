using UrlShortener.Core.Common;
using UrlShortener.Core.Domain;
using UrlShortener.Core.Models;
using UrlShortener.UnitTests.Fakes;

namespace UrlShortener.UnitTests.Services;

public class ShortLinkServiceResolveTests
{
    [Fact]
    public async Task Resolves_an_active_link_and_tracks_the_click()
    {
        var fixture = new ServiceFixture();
        var code = (await fixture.Service.CreateAsync(new CreateShortLinkCommand("https://example.com/a"))).Value.Link.Code;

        var result = await fixture.Service.ResolveAsync(code, new ClickContext("https://Twitter.com/some/post?id=1", "Mozilla/5.0 (Windows NT 10.0) AppleWebKit/537.36 Chrome/126.0 Safari/537.36"));

        Assert.True(result.IsSuccess);
        Assert.Equal("https://example.com/a", result.Value.OriginalUrl);
        var click = Assert.Single(fixture.Tracker.Events);
        Assert.Equal(code, click.Code);
        Assert.Equal("twitter.com", click.RefererHost);
        Assert.Equal("chrome", click.UserAgentFamily);
        Assert.Equal(fixture.Time.GetUtcNow(), click.OccurredAt);
    }

    [Fact]
    public async Task Still_resolves_when_the_click_cannot_be_tracked()
    {
        var fixture = new ServiceFixture();
        fixture.Tracker.Accept = false;
        var code = (await fixture.Service.CreateAsync(new CreateShortLinkCommand("https://example.com/a"))).Value.Link.Code;

        var result = await fixture.Service.ResolveAsync(code, ClickContext.Empty);

        Assert.True(result.IsSuccess);
    }

    [Theory]
    [InlineData("missing1")]
    [InlineData("x")]
    [InlineData("has.dot")]
    public async Task Returns_not_found_for_unknown_or_malformed_codes(string code)
    {
        var fixture = new ServiceFixture();

        var result = await fixture.Service.ResolveAsync(code, ClickContext.Empty);

        Assert.Equal(ErrorType.NotFound, result.Error!.Type);
        Assert.Empty(fixture.Tracker.Events);
    }

    [Fact]
    public async Task Remembers_unknown_codes_so_repeated_guesses_skip_storage()
    {
        var fixture = new ServiceFixture();

        await fixture.Service.ResolveAsync("guess01", ClickContext.Empty);
        await fixture.Links.TryAddAsync(new ShortLink { Code = "guess01", OriginalUrl = "https://sneaky.example/" });
        var second = await fixture.Service.ResolveAsync("guess01", ClickContext.Empty);

        Assert.Contains("guess01", fixture.Cache.Missing);
        Assert.Equal(ErrorType.NotFound, second.Error!.Type);
    }

    [Fact]
    public async Task Returns_gone_once_the_link_has_expired()
    {
        var fixture = new ServiceFixture();
        var created = await fixture.Service.CreateAsync(
            new CreateShortLinkCommand("https://example.com", ExpiresAt: fixture.Time.GetUtcNow().AddHours(1)));
        var code = created.Value.Link.Code;

        Assert.True((await fixture.Service.ResolveAsync(code, ClickContext.Empty)).IsSuccess);
        fixture.Time.Advance(TimeSpan.FromHours(1));
        var result = await fixture.Service.ResolveAsync(code, ClickContext.Empty);

        Assert.Equal("link_expired", result.Error!.Code);
        Assert.Equal(ErrorType.Gone, result.Error.Type);
    }

    [Fact]
    public async Task Returns_gone_after_deactivation_even_if_the_link_was_cached()
    {
        var fixture = new ServiceFixture();
        var code = (await fixture.Service.CreateAsync(new CreateShortLinkCommand("https://example.com"))).Value.Link.Code;
        await fixture.Service.ResolveAsync(code, ClickContext.Empty);
        Assert.True(fixture.Cache.Entries.ContainsKey(code));

        var deactivated = await fixture.Service.DeactivateAsync(code);
        var result = await fixture.Service.ResolveAsync(code, ClickContext.Empty);

        Assert.True(deactivated.IsSuccess);
        Assert.Equal("link_deactivated", result.Error!.Code);
    }

    [Fact]
    public async Task Serves_subsequent_resolutions_from_the_cache()
    {
        var fixture = new ServiceFixture();
        var cached = new ShortLink { Code = "cached1", OriginalUrl = "https://cached.example/" };
        fixture.Cache.StoreFound(cached);

        var result = await fixture.Service.ResolveAsync("cached1", ClickContext.Empty);

        Assert.Same(cached, result.Value);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("not-a-url", null)]
    [InlineData("android-app://com.example", null)]
    [InlineData("https://WWW.Google.com/search?q=secret", "www.google.com")]
    public void Normalizes_referrers_to_their_host_only(string? referrer, string? expected)
    {
        Assert.Equal(expected, UrlShortener.Core.Services.ShortLinkService.NormalizeReferrer(referrer));
    }

    [Fact]
    public async Task Deactivating_an_unknown_code_returns_not_found()
    {
        var fixture = new ServiceFixture();

        var result = await fixture.Service.DeactivateAsync("unknown");

        Assert.Equal(ErrorType.NotFound, result.Error!.Type);
    }

    [Fact]
    public async Task Deactivating_twice_is_safe()
    {
        var fixture = new ServiceFixture();
        var code = (await fixture.Service.CreateAsync(new CreateShortLinkCommand("https://example.com"))).Value.Link.Code;

        Assert.True((await fixture.Service.DeactivateAsync(code)).IsSuccess);
        Assert.True((await fixture.Service.DeactivateAsync(code)).IsSuccess);
    }
}
