using UrlShortener.Core.Common;
using UrlShortener.Core.Domain;

namespace UrlShortener.Core.Abstractions;

public interface IShortLinkRepository
{
    Task<ShortLink?> GetByCodeAsync(string code, CancellationToken cancellationToken = default);

    /// <summary>Finds an active, generated (non-alias), non-expiring link for the URL, if any.</summary>
    Task<ShortLink?> FindReusableByUrlAsync(string originalUrl, CancellationToken cancellationToken = default);

    /// <summary>Persists the link. Returns <c>false</c> when the code is already taken.</summary>
    Task<bool> TryAddAsync(ShortLink link, CancellationToken cancellationToken = default);

    /// <summary>Marks the link inactive. Returns <c>false</c> when the code does not exist.</summary>
    Task<bool> DeactivateAsync(string code, CancellationToken cancellationToken = default);

    Task<PagedResult<ShortLink>> ListAsync(int page, int pageSize, CancellationToken cancellationToken = default);
}
