using UrlShortener.Core;
using UrlShortener.Core.Validation;

namespace UrlShortener.UnitTests.Validation;

public class TargetUrlValidatorTests
{
    private static readonly ShortenerOptions Options = new()
    {
        BaseUrl = "https://sho.rt",
        BlockedHosts = ["evil.example"],
        MaxUrlLength = 100,
    };

    [Theory]
    [InlineData("https://example.com", "https://example.com/")]
    [InlineData("  http://Example.COM/Path?q=1#frag  ", "http://example.com/Path?q=1#frag")]
    [InlineData("https://example.com:8443/a b", "https://example.com:8443/a%20b")]
    public void Accepts_and_normalizes_valid_urls(string input, string expected)
    {
        Assert.True(TargetUrlValidator.TryNormalize(input, Options, out var normalized, out var error));
        Assert.Equal(expected, normalized);
        Assert.Null(error);
    }

    [Theory]
    [InlineData(null, "required")]
    [InlineData("", "required")]
    [InlineData("   ", "required")]
    [InlineData("example.com", "absolute")]
    [InlineData("/relative/path", "http and https")]
    [InlineData("ftp://example.com/file", "http and https")]
    [InlineData("javascript:alert(1)", "http and https")]
    [InlineData("file:///etc/passwd", "http and https")]
    [InlineData("https://user:pass@example.com", "credentials")]
    [InlineData("https://sho.rt/abc", "not allowed")]
    [InlineData("https://evil.example/x", "not allowed")]
    [InlineData("https://sub.evil.example/x", "not allowed")]
    public void Rejects_invalid_urls(string? input, string expectedMessageFragment)
    {
        Assert.False(TargetUrlValidator.TryNormalize(input, Options, out var normalized, out var error));
        Assert.Null(normalized);
        Assert.Contains(expectedMessageFragment, error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Rejects_urls_longer_than_the_configured_limit()
    {
        var url = "https://example.com/" + new string('a', 100);

        Assert.False(TargetUrlValidator.TryNormalize(url, Options, out _, out var error));
        Assert.Contains("100", error, StringComparison.Ordinal);
    }

    [Fact]
    public void Does_not_block_hosts_that_merely_end_with_a_blocked_name()
    {
        Assert.True(TargetUrlValidator.TryNormalize("https://notevil.example/x", Options, out _, out _));
    }
}
