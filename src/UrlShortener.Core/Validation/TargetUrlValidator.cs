using System.Diagnostics.CodeAnalysis;

namespace UrlShortener.Core.Validation;

public static class TargetUrlValidator
{
    /// <summary>
    /// Validates a destination URL and returns its canonical absolute form.
    /// </summary>
    public static bool TryNormalize(
        string? input,
        ShortenerOptions options,
        [NotNullWhen(true)] out string? normalized,
        [NotNullWhen(false)] out string? error)
    {
        ArgumentNullException.ThrowIfNull(options);
        normalized = null;

        if (string.IsNullOrWhiteSpace(input))
        {
            error = "A URL is required.";
            return false;
        }

        var trimmed = input.Trim();
        if (trimmed.Length > options.MaxUrlLength)
        {
            error = $"The URL must not exceed {options.MaxUrlLength} characters.";
            return false;
        }

        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
        {
            error = "The URL must be absolute (for example https://example.com/page).";
            return false;
        }

        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
        {
            error = "Only http and https URLs are supported.";
            return false;
        }

        if (string.IsNullOrEmpty(uri.Host))
        {
            error = "The URL must contain a host.";
            return false;
        }

        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            error = "URLs containing credentials are not allowed.";
            return false;
        }

        if (IsBlockedHost(uri.Host, options))
        {
            error = $"Links to host '{uri.Host}' are not allowed.";
            return false;
        }

        normalized = uri.AbsoluteUri;
        error = null;
        return true;
    }

    private static bool IsBlockedHost(string host, ShortenerOptions options)
    {
        var blocked = options.BlockedHosts.AsEnumerable();
        if (Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out var baseUri))
        {
            blocked = blocked.Append(baseUri.Host);
        }

        return blocked
            .Where(b => !string.IsNullOrWhiteSpace(b))
            .Any(b => host.Equals(b, StringComparison.OrdinalIgnoreCase)
                      || host.EndsWith("." + b, StringComparison.OrdinalIgnoreCase));
    }
}
