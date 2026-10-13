using System.Diagnostics.Metrics;
using Microsoft.Extensions.DependencyInjection;
using UrlShortener.Infrastructure.Diagnostics;

namespace UrlShortener.UnitTests.Fakes;

internal static class TestMetrics
{
    public static ShortenerMetrics Create() =>
        new(new ServiceCollection().AddMetrics().BuildServiceProvider().GetRequiredService<IMeterFactory>());
}
