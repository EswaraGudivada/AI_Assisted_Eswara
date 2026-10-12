using UrlShortener.Core;

namespace UrlShortener.Api.Infrastructure;

/// <summary>
/// Builds public short URLs. Configure <c>Shortener:BaseUrl</c> in production so URLs are not derived
/// from the client-controlled Host header.
/// </summary>
public sealed class ShortUrlBuilder(ShortenerOptions options)
{
    public string Build(HttpRequest request, string code)
    {
        ArgumentNullException.ThrowIfNull(request);
        var baseUrl = string.IsNullOrWhiteSpace(options.BaseUrl)
            ? $"{request.Scheme}://{request.Host}{request.PathBase}"
            : options.BaseUrl;
        return $"{baseUrl.TrimEnd('/')}/{Uri.EscapeDataString(code)}";
    }
}
