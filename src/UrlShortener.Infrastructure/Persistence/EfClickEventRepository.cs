using Microsoft.EntityFrameworkCore;
using UrlShortener.Core.Abstractions;
using UrlShortener.Core.Domain;

namespace UrlShortener.Infrastructure.Persistence;

internal sealed class EfClickEventRepository(UrlShortenerDbContext db) : IClickEventRepository
{
    public async Task RecordBatchAsync(IReadOnlyCollection<ClickEvent> events, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(events);
        if (events.Count == 0)
        {
            return;
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        db.ClickEvents.AddRange(events);
        await db.SaveChangesAsync(cancellationToken);

        foreach (var group in events.GroupBy(e => e.Code, StringComparer.Ordinal))
        {
            var count = group.LongCount();
            DateTimeOffset? lastAccessed = group.Max(e => e.OccurredAt);
            await db.ShortLinks
                .Where(l => l.Code == group.Key)
                .ExecuteUpdateAsync(
                    s => s.SetProperty(l => l.ClickCount, l => l.ClickCount + count)
                          .SetProperty(l => l.LastAccessedAt, lastAccessed),
                    cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        db.ChangeTracker.Clear();
    }

    public async Task<IReadOnlyList<ClickEvent>> GetSinceAsync(
        string code,
        DateTimeOffset since,
        CancellationToken cancellationToken = default) =>
        await db.ClickEvents.AsNoTracking()
            .Where(c => c.Code == code && c.OccurredAt >= since)
            .ToListAsync(cancellationToken);
}
