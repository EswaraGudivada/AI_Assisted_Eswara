using UrlShortener.Core.Common;

namespace UrlShortener.Core.Safety;

/// <summary>
/// Refuses configured hosts (and their subdomains) plus the service's own host, so a short link can never
/// point back at the shortener.
/// </summary>
public sealed class DeniedHostRule : IUrlSafetyRule
{
    private readonly string[] _denied;

    public DeniedHostRule(SecurityOptions security, ShortenerOptions shortener)
    {
        ArgumentNullException.ThrowIfNull(security);
        ArgumentNullException.ThrowIfNull(shortener);

        var hosts = security.DeniedHosts.AsEnumerable();
        if (Uri.TryCreate(shortener.BaseUrl, UriKind.Absolute, out var baseUri))
        {
            hosts = hosts.Append(baseUri.Host);
        }

        _denied = [.. hosts
            .Where(h => !string.IsNullOrWhiteSpace(h))
            .Select(h => h.Trim().TrimStart('.').ToLowerInvariant())
            .Distinct(StringComparer.Ordinal)];
    }

    public Error? Evaluate(Uri url)
    {
        ArgumentNullException.ThrowIfNull(url);
        var host = url.Host.ToLowerInvariant();
        return _denied.Any(d => host == d || host.EndsWith("." + d, StringComparison.Ordinal))
            ? ShortLinkErrors.DeniedHost
            : null;
    }
}
