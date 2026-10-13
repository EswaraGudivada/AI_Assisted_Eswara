using UrlShortener.Core;
using UrlShortener.Core.Common;
using UrlShortener.Core.Domain;
using UrlShortener.Core.Models;
using UrlShortener.UnitTests.Fakes;

namespace UrlShortener.UnitTests.Services;

public class ShortLinkServiceCreateTests
{
    [Fact]
    public async Task Creates_a_link_with_a_generated_code()
    {
        var fixture = new ServiceFixture(new SequenceCodeGenerator("Abc1234"));

        var result = await fixture.Service.CreateAsync(new CreateShortLinkCommand("https://example.com/page"));

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.Created);
        var link = result.Value.Link;
        Assert.Equal("Abc1234", link.Code);
        Assert.Equal("https://example.com/page", link.OriginalUrl);
        Assert.Equal(fixture.Time.GetUtcNow(), link.CreatedAt);
        Assert.False(link.IsCustomAlias);
        Assert.True(link.IsActive);
        Assert.Same(link, await fixture.Links.GetByCodeAsync("Abc1234"));
    }

    [Fact]
    public async Task Rejects_invalid_urls_without_touching_storage()
    {
        var fixture = new ServiceFixture();

        var result = await fixture.Service.CreateAsync(new CreateShortLinkCommand("not a url"));

        AssertError(result, "url_invalid", ErrorType.Validation);
        Assert.Empty(fixture.Store.Links);
    }

    [Fact]
    public async Task Retries_on_code_collision()
    {
        var generator = new SequenceCodeGenerator("Taken01", "Free001");
        var fixture = new ServiceFixture(generator);
        await fixture.Links.TryAddAsync(new ShortLink { Code = "Taken01", OriginalUrl = "https://other.example/" });

        var result = await fixture.Service.CreateAsync(new CreateShortLinkCommand("https://example.com"));

        Assert.True(result.IsSuccess);
        Assert.Equal("Free001", result.Value.Link.Code);
        Assert.Equal(2, generator.Calls);
    }

    [Fact]
    public async Task Fails_with_unavailable_after_exhausting_generation_attempts()
    {
        var options = new ShortenerOptions { MaxGenerationAttempts = 2 };
        var fixture = new ServiceFixture(new SequenceCodeGenerator("Taken01", "Taken01"), options);
        await fixture.Links.TryAddAsync(new ShortLink { Code = "Taken01", OriginalUrl = "https://other.example/" });

        var result = await fixture.Service.CreateAsync(new CreateShortLinkCommand("https://example.com"));

        AssertError(result, "code_generation_exhausted", ErrorType.Unavailable);
    }

    [Fact]
    public async Task Skips_generated_codes_that_collide_with_reserved_words()
    {
        var generator = new SequenceCodeGenerator("swagger", "Ok12345");
        var fixture = new ServiceFixture(generator);

        var result = await fixture.Service.CreateAsync(new CreateShortLinkCommand("https://example.com"));

        Assert.Equal("Ok12345", result.Value.Link.Code);
    }

    [Fact]
    public async Task Returns_the_existing_link_when_the_same_url_is_shortened_again()
    {
        var fixture = new ServiceFixture();
        var first = await fixture.Service.CreateAsync(new CreateShortLinkCommand("https://example.com/x"));

        var second = await fixture.Service.CreateAsync(new CreateShortLinkCommand("HTTPS://EXAMPLE.com/x"));

        Assert.False(second.Value.Created);
        Assert.Equal(first.Value.Link.Code, second.Value.Link.Code);
        Assert.Single(fixture.Store.Links);
    }

    [Fact]
    public async Task Does_not_deduplicate_when_disabled()
    {
        var fixture = new ServiceFixture(options: new ShortenerOptions { DeduplicateUrls = false });

        await fixture.Service.CreateAsync(new CreateShortLinkCommand("https://example.com/x"));
        var second = await fixture.Service.CreateAsync(new CreateShortLinkCommand("https://example.com/x"));

        Assert.True(second.Value.Created);
        Assert.Equal(2, fixture.Store.Links.Count);
    }

    [Fact]
    public async Task Does_not_reuse_deactivated_or_expiring_links()
    {
        var fixture = new ServiceFixture();
        var first = await fixture.Service.CreateAsync(new CreateShortLinkCommand("https://example.com/x"));
        await fixture.Service.DeactivateAsync(first.Value.Link.Code);
        var expiring = await fixture.Service.CreateAsync(
            new CreateShortLinkCommand("https://example.com/x", ExpiresAt: fixture.Time.GetUtcNow().AddDays(1)));

        var third = await fixture.Service.CreateAsync(new CreateShortLinkCommand("https://example.com/x"));

        Assert.True(expiring.Value.Created);
        Assert.True(third.Value.Created);
        Assert.Equal(3, fixture.Store.Links.Count);
    }

    [Fact]
    public async Task Creates_a_link_with_a_custom_alias()
    {
        var fixture = new ServiceFixture();

        var result = await fixture.Service.CreateAsync(new CreateShortLinkCommand("https://example.com", " launch-2026 "));

        Assert.True(result.Value.Created);
        Assert.Equal("launch-2026", result.Value.Link.Code);
        Assert.True(result.Value.Link.IsCustomAlias);
    }

    [Fact]
    public async Task Rejects_a_custom_alias_that_is_already_taken()
    {
        var fixture = new ServiceFixture();
        await fixture.Service.CreateAsync(new CreateShortLinkCommand("https://a.example", "promo"));

        var result = await fixture.Service.CreateAsync(new CreateShortLinkCommand("https://b.example", "promo"));

        AssertError(result, "alias_taken", ErrorType.Conflict);
        Assert.Equal("https://a.example/", fixture.Store.Links["promo"].OriginalUrl);
    }

    [Theory]
    [InlineData("admin", "alias_reserved")]
    [InlineData("Swagger", "alias_reserved")]
    [InlineData("abc", "alias_invalid")]
    [InlineData("bad alias", "alias_invalid")]
    public async Task Rejects_invalid_or_reserved_aliases(string alias, string expectedCode)
    {
        var fixture = new ServiceFixture();

        var result = await fixture.Service.CreateAsync(new CreateShortLinkCommand("https://example.com", alias));

        AssertError(result, expectedCode, ErrorType.Validation);
    }

    [Theory]
    [InlineData("http://localhost:8080/admin")]
    [InlineData("http://127.0.0.1/")]
    [InlineData("http://10.1.2.3/")]
    [InlineData("http://169.254.169.254/latest/meta-data/")]
    [InlineData("http://[::1]/")]
    public async Task Rejects_private_network_targets(string url)
    {
        var fixture = new ServiceFixture();

        var result = await fixture.Service.CreateAsync(new CreateShortLinkCommand(url));

        AssertError(result, "url_private_target", ErrorType.Validation);
        Assert.Empty(fixture.Store.Links);
    }

    [Fact]
    public async Task Allows_private_targets_when_configured_for_development()
    {
        var fixture = new ServiceFixture(security: new SecurityOptions { AllowPrivateNetworkTargets = true });

        var result = await fixture.Service.CreateAsync(new CreateShortLinkCommand("http://localhost:3000/"));

        Assert.True(result.IsSuccess);
    }

    [Theory]
    [InlineData("https://sho.rt/abc")]
    [InlineData("https://evil.example/x")]
    [InlineData("https://sub.evil.example/x")]
    public async Task Rejects_denied_hosts_and_the_service_itself(string url)
    {
        var fixture = new ServiceFixture(security: new SecurityOptions { DeniedHosts = ["evil.example"] });

        var result = await fixture.Service.CreateAsync(new CreateShortLinkCommand(url));

        AssertError(result, "url_host_denied", ErrorType.Validation);
    }

    [Fact]
    public async Task Creating_a_link_clears_a_cached_not_found_entry()
    {
        var fixture = new ServiceFixture();
        await fixture.Service.ResolveAsync("newcode", ClickContext.Empty);
        Assert.Contains("newcode", fixture.Cache.Missing);

        await fixture.Service.CreateAsync(new CreateShortLinkCommand("https://example.com", "newcode"));
        var resolved = await fixture.Service.ResolveAsync("newcode", ClickContext.Empty);

        Assert.True(resolved.IsSuccess);
    }

    [Fact]
    public async Task Rejects_an_expiry_in_the_past()
    {
        var fixture = new ServiceFixture();

        var result = await fixture.Service.CreateAsync(
            new CreateShortLinkCommand("https://example.com", ExpiresAt: fixture.Time.GetUtcNow()));

        AssertError(result, "expiry_invalid", ErrorType.Validation);
    }

    [Fact]
    public async Task Stores_the_expiry_in_utc()
    {
        var fixture = new ServiceFixture();
        var localExpiry = new DateTimeOffset(2026, 2, 1, 10, 0, 0, TimeSpan.FromHours(5));

        var result = await fixture.Service.CreateAsync(new CreateShortLinkCommand("https://example.com", ExpiresAt: localExpiry));

        Assert.Equal(TimeSpan.Zero, result.Value.Link.ExpiresAt!.Value.Offset);
        Assert.Equal(localExpiry, result.Value.Link.ExpiresAt);
    }

    private static void AssertError<T>(Result<T> result, string code, ErrorType type)
    {
        Assert.False(result.IsSuccess);
        Assert.Equal(code, result.Error!.Code);
        Assert.Equal(type, result.Error.Type);
    }
}
