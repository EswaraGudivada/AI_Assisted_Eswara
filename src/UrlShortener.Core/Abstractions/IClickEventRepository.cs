using UrlShortener.Core.Domain;

namespace UrlShortener.Core.Abstractions;

public interface IClickEventRepository
{
    /// <summary>Stores the events and updates the click counters of the affected links atomically.</summary>
    Task RecordBatchAsync(IReadOnlyCollection<ClickEvent> events, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ClickEvent>> GetSinceAsync(string code, DateTimeOffset since, CancellationToken cancellationToken = default);
}
