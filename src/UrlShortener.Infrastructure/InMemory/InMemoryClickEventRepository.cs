using System.Collections.Concurrent;
using UrlShortener.Core.Abstractions;
using UrlShortener.Core.Domain;

namespace UrlShortener.Infrastructure.InMemory;

internal sealed class InMemoryClickEventRepository(InMemoryLinkStore store) : IClickEventRepository
{
    public Task RecordBatchAsync(IReadOnlyCollection<ClickEvent> events, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(events);

        foreach (var group in events.GroupBy(e => e.Code, StringComparer.Ordinal))
        {
            var queue = store.Clicks.GetOrAdd(group.Key, _ => new ConcurrentQueue<ClickEvent>());
            foreach (var clickEvent in group)
            {
                queue.Enqueue(clickEvent);
            }

            var count = group.LongCount();
            var lastAccessed = group.Max(e => e.OccurredAt);
            while (store.Links.TryGetValue(group.Key, out var current))
            {
                var updated = current with
                {
                    ClickCount = current.ClickCount + count,
                    LastAccessedAt = current.LastAccessedAt is { } previous && previous > lastAccessed ? previous : lastAccessed,
                };

                if (store.Links.TryUpdate(group.Key, updated, current))
                {
                    break;
                }
            }
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<ClickEvent>> GetSinceAsync(
        string code,
        DateTimeOffset since,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<ClickEvent> result = store.Clicks.TryGetValue(code, out var queue)
            ? queue.Where(c => c.OccurredAt >= since).ToList()
            : [];
        return Task.FromResult(result);
    }
}
