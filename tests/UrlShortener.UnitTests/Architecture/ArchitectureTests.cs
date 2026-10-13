using System.Reflection;
using UrlShortener.Core.Services;
using UrlShortener.Infrastructure;

namespace UrlShortener.UnitTests.Architecture;

/// <summary>Keeps the dependency direction honest: Api -> Infrastructure -> Core, and Core stays framework-free.</summary>
public class ArchitectureTests
{
    private static readonly Assembly Core = typeof(ShortLinkService).Assembly;
    private static readonly Assembly Infrastructure = typeof(DependencyInjection).Assembly;

    [Theory]
    [InlineData("Microsoft.EntityFrameworkCore")]
    [InlineData("Microsoft.AspNetCore")]
    [InlineData("Microsoft.Extensions")]
    [InlineData("Swashbuckle")]
    [InlineData("UrlShortener.Infrastructure")]
    [InlineData("UrlShortener.Api")]
    public void Core_does_not_depend_on_frameworks_or_outer_layers(string forbiddenPrefix)
    {
        Assert.DoesNotContain(References(Core), name => name.StartsWith(forbiddenPrefix, StringComparison.Ordinal));
    }

    [Fact]
    public void Infrastructure_depends_on_core_but_not_on_the_api()
    {
        var references = References(Infrastructure);

        Assert.Contains("UrlShortener.Core", references);
        Assert.DoesNotContain("UrlShortener.Api", references);
    }

    private static List<string> References(Assembly assembly) =>
        [.. assembly.GetReferencedAssemblies().Select(a => a.Name ?? string.Empty)];
}
