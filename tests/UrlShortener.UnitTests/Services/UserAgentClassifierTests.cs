using UrlShortener.Core.Services;

namespace UrlShortener.UnitTests.Services;

public class UserAgentClassifierTests
{
    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData("Mozilla/5.0 (compatible; Googlebot/2.1; +http://www.google.com/bot.html)", "bot")]
    [InlineData("Mozilla/5.0 AppleWebKit/537.36 (KHTML, like Gecko; compatible; bingbot/2.0)", "bot")]
    [InlineData("curl/8.5.0", "tool")]
    [InlineData("PostmanRuntime/7.39.0", "tool")]
    [InlineData("Mozilla/5.0 (Windows NT 10.0) AppleWebKit/537.36 Chrome/126.0 Safari/537.36 Edg/126.0", "edge")]
    [InlineData("Mozilla/5.0 (Windows NT 10.0) AppleWebKit/537.36 Chrome/126.0 Safari/537.36 OPR/111.0", "opera")]
    [InlineData("Mozilla/5.0 (Windows NT 10.0) AppleWebKit/537.36 Chrome/126.0 Safari/537.36", "chrome")]
    [InlineData("Mozilla/5.0 (iPhone; CPU iPhone OS 17_5) AppleWebKit/605.1.15 CriOS/126.0 Mobile Safari/604.1", "chrome")]
    [InlineData("Mozilla/5.0 (X11; Linux x86_64; rv:127.0) Gecko/20100101 Firefox/127.0", "firefox")]
    [InlineData("Mozilla/5.0 (Macintosh; Intel Mac OS X 14_5) AppleWebKit/605.1.15 Version/17.5 Safari/605.1.15", "safari")]
    [InlineData("SomethingElse/1.0", "other")]
    public void Classifies_user_agents_into_a_closed_set_of_families(string? userAgent, string? expected)
    {
        Assert.Equal(expected, UserAgentClassifier.Classify(userAgent));
    }
}
