using UrlShortener.Core.Common;
using UrlShortener.Core.Domain;
using UrlShortener.Core.Models;

namespace UrlShortener.Core.Services;

public interface IShortLinkService
{
    Task<Result<CreateShortLinkResult>> CreateAsync(CreateShortLinkCommand command, CancellationToken cancellationToken = default);

    /// <summary>Resolves a code to its link and queues the click without waiting for storage.</summary>
    Task<Result<ShortLink>> ResolveAsync(string code, ClickContext context, CancellationToken cancellationToken = default);

    Task<Result<ShortLink>> GetAsync(string code, CancellationToken cancellationToken = default);

    /// <summary>Turns a link off. Idempotent: deactivating an inactive link succeeds.</summary>
    Task<Result<bool>> DeactivateAsync(string code, CancellationToken cancellationToken = default);

    Task<Result<PagedResult<ShortLink>>> ListAsync(int? page, int? pageSize, CancellationToken cancellationToken = default);

    Task<Result<LinkStatistics>> GetStatisticsAsync(
        string code,
        DateTimeOffset? from,
        DateTimeOffset? to,
        CancellationToken cancellationToken = default);
}
