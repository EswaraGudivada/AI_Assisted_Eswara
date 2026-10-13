using UrlShortener.Core.Abstractions;
using UrlShortener.Core.Common;
using UrlShortener.Core.Domain;

namespace UrlShortener.UnitTests.Fakes.InMemory;

internal sealed class InMemoryShortLinkRepository(InMemoryLinkStore store) : IShortLinkRepository
{
    public Task<ShortLink?> GetByCodeAsync(string code, CancellationToken cancellationToken = default) =>
        Task.FromResult(store.Links.GetValueOrDefault(code));

    public Task<ShortLink?> FindReusableByUrlAsync(string originalUrl, CancellationToken cancellationToken = default) =>
        Task.FromResult(store.Links.Values
            .Where(l => l.OriginalUrl == originalUrl && l.IsActive && !l.IsCustomAlias && l.ExpiresAt is null)
            .OrderBy(l => l.CreatedAt)
            .FirstOrDefault());

    public Task<bool> TryAddAsync(ShortLink link, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(link);
        return Task.FromResult(store.Links.TryAdd(link.Code, link));
    }

    public Task<bool> DeactivateAsync(string code, CancellationToken cancellationToken = default)
    {
        while (store.Links.TryGetValue(code, out var current))
        {
            if (store.Links.TryUpdate(code, current with { IsActive = false }, current))
            {
                return Task.FromResult(true);
            }
        }

        return Task.FromResult(false);
    }

    public Task<PagedResult<ShortLink>> ListAsync(int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var snapshot = store.Links.Values.ToList();
        var items = snapshot
            .OrderByDescending(l => l.CreatedAt)
            .ThenBy(l => l.Code, StringComparer.Ordinal)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToList();
        return Task.FromResult(new PagedResult<ShortLink>(items, page, pageSize, snapshot.Count));
    }
}
