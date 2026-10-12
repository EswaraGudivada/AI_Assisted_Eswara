using UrlShortener.Core.Common;

namespace UrlShortener.UnitTests.Infrastructure;

public class ResultTests
{
    [Fact]
    public void Accessing_the_value_of_a_failure_throws()
    {
        Result<int> result = ShortLinkErrors.NotFound("abc");

        Assert.False(result.IsSuccess);
        Assert.Throws<InvalidOperationException>(() => result.Value);
    }

    [Fact]
    public void Success_exposes_the_value()
    {
        Result<int> result = 42;

        Assert.True(result.IsSuccess);
        Assert.Null(result.Error);
        Assert.Equal(42, result.Value);
    }
}
