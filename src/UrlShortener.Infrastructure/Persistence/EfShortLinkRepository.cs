using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using UrlShortener.Core.Abstractions;
using UrlShortener.Core.Common;
using UrlShortener.Core.Domain;

namespace UrlShortener.Infrastructure.Persistence;

internal sealed class EfShortLinkRepository(UrlShortenerDbContext db) : IShortLinkRepository
{
    private const int SqliteConstraintViolation = 19;

    public Task<ShortLink?> GetByCodeAsync(string code, CancellationToken cancellationToken = default) =>
        db.ShortLinks.AsNoTracking().FirstOrDefaultAsync(l => l.Code == code, cancellationToken);

    public Task<ShortLink?> FindReusableByUrlAsync(string originalUrl, CancellationToken cancellationToken = default) =>
        db.ShortLinks.AsNoTracking()
            .Where(l => l.OriginalUrl == originalUrl && l.IsActive && !l.IsCustomAlias && l.ExpiresAt == null)
            .OrderBy(l => l.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<bool> TryAddAsync(ShortLink link, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(link);

        if (await db.ShortLinks.AnyAsync(l => l.Code == link.Code, cancellationToken))
        {
            return false;
        }

        db.ShortLinks.Add(link);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqliteException { SqliteErrorCode: SqliteConstraintViolation })
        {
            // Lost a race with a concurrent insert of the same code; the unique index is the source of truth.
            return false;
        }
        finally
        {
            db.Entry(link).State = EntityState.Detached;
        }
    }

    public async Task<bool> DeactivateAsync(string code, CancellationToken cancellationToken = default)
    {
        var affected = await db.ShortLinks
            .Where(l => l.Code == code)
            .ExecuteUpdateAsync(s => s.SetProperty(l => l.IsActive, false), cancellationToken);
        return affected > 0;
    }

    public async Task<PagedResult<ShortLink>> ListAsync(int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var total = await db.ShortLinks.LongCountAsync(cancellationToken);
        var items = await db.ShortLinks.AsNoTracking()
            .OrderByDescending(l => l.CreatedAt)
            .ThenBy(l => l.Code)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
        return new PagedResult<ShortLink>(items, page, pageSize, total);
    }
}
