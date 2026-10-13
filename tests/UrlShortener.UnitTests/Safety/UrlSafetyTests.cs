using UrlShortener.Core;
using UrlShortener.Core.Safety;

namespace UrlShortener.UnitTests.Safety;

public class UrlSafetyTests
{
    private static readonly PrivateNetworkRule Strict = new(new SecurityOptions());

    [Theory]
    [InlineData("http://localhost/")]
    [InlineData("http://LOCALHOST:5080/")]
    [InlineData("http://app.localhost/")]
    [InlineData("http://printer.local/")]
    [InlineData("http://db.internal/")]
    [InlineData("http://metadata.google.internal/")]
    [InlineData("http://127.0.0.1/")]
    [InlineData("http://127.10.0.1/")]
    [InlineData("http://0.0.0.0/")]
    [InlineData("http://10.0.0.1/")]
    [InlineData("http://172.16.0.1/")]
    [InlineData("http://172.31.255.255/")]
    [InlineData("http://192.168.1.1/")]
    [InlineData("http://169.254.169.254/latest/meta-data/")]
    [InlineData("http://100.64.0.1/")]
    [InlineData("http://[::1]/")]
    [InlineData("http://[fe80::1]/")]
    [InlineData("http://[fd00::1]/")]
    [InlineData("http://[::ffff:10.0.0.1]/")]
    public void Private_network_rule_rejects_internal_targets(string url)
    {
        Assert.Equal("url_private_target", Strict.Evaluate(new Uri(url))?.Code);
    }

    [Theory]
    [InlineData("https://example.com/")]
    [InlineData("http://8.8.8.8/")]
    [InlineData("http://172.32.0.1/")]
    [InlineData("http://100.128.0.1/")]
    [InlineData("http://[2001:4860:4860::8888]/")]
    [InlineData("https://localhost.example.com/")]
    public void Private_network_rule_allows_public_targets(string url)
    {
        Assert.Null(Strict.Evaluate(new Uri(url)));
    }

    [Fact]
    public void Private_network_rule_can_be_relaxed_for_development()
    {
        var relaxed = new PrivateNetworkRule(new SecurityOptions { AllowPrivateNetworkTargets = true });

        Assert.Null(relaxed.Evaluate(new Uri("http://localhost:3000/")));
    }

    [Theory]
    [InlineData("https://evil.example/", true)]
    [InlineData("https://a.b.evil.example/", true)]
    [InlineData("https://notevil.example/", false)]
    [InlineData("https://sho.rt/abc", true)]
    [InlineData("https://example.com/", false)]
    public void Denied_host_rule_matches_hosts_and_subdomains_and_the_service_itself(string url, bool denied)
    {
        var rule = new DeniedHostRule(
            new SecurityOptions { DeniedHosts = [" .Evil.Example "] },
            new ShortenerOptions { BaseUrl = "https://sho.rt" });

        Assert.Equal(denied, rule.Evaluate(new Uri(url)) is { Code: "url_host_denied" });
    }

    [Fact]
    public void Validator_returns_the_first_rejection_and_null_when_all_rules_pass()
    {
        var validator = new UrlSafetyValidator(
        [
            Strict,
            new DeniedHostRule(new SecurityOptions { DeniedHosts = ["evil.example"] }, new ShortenerOptions()),
        ]);

        Assert.Equal("url_private_target", validator.Validate(new Uri("http://127.0.0.1/"))?.Code);
        Assert.Equal("url_host_denied", validator.Validate(new Uri("https://evil.example/"))?.Code);
        Assert.Null(validator.Validate(new Uri("https://example.com/")));
    }
}
