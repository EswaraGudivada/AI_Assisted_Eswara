using UrlShortener.Core;

namespace UrlShortener.Api.Infrastructure;

/// <summary>
/// Builds public short URLs from <c>Shortener:BaseUrl</c>, never from the client-controlled Host header.
/// </summary>
public sealed class ShortUrlBuilder(ShortenerOptions options)
{
    public string Build(string code) => $"{options.BaseUrl.TrimEnd('/')}/{Uri.EscapeDataString(code)}";
}
