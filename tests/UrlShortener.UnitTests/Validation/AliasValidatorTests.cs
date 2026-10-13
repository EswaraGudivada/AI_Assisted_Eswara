using UrlShortener.Core;
using UrlShortener.Core.Validation;

namespace UrlShortener.UnitTests.Validation;

public class AliasValidatorTests
{
    private static readonly ShortenerOptions Options = new();

    [Theory]
    [InlineData("abcd")]
    [InlineData("my-link_2026")]
    [InlineData("ABCDEFGHIJKLMNOPQRSTUVWXYZ012345")]
    public void Accepts_valid_aliases(string alias)
    {
        Assert.True(AliasValidator.TryValidate(alias, Options, out var error));
        Assert.Null(error);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456")]
    [InlineData("has space")]
    [InlineData("dot.ted")]
    [InlineData("slash/ed")]
    [InlineData("ünïcode")]
    public void Rejects_badly_formatted_aliases(string alias)
    {
        Assert.False(AliasValidator.TryValidate(alias, Options, out var error));
        Assert.Contains("4-32", error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("ADMIN")]
    [InlineData("HEALTH")]
    [InlineData("OpenApi")]
    public void Rejects_reserved_aliases_case_insensitively(string alias)
    {
        Assert.False(AliasValidator.TryValidate(alias, Options, out var error));
        Assert.Contains("reserved", error, StringComparison.Ordinal);
    }
}
