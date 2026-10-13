using UrlShortener.Core.Abstractions;
using UrlShortener.Core.Common;
using UrlShortener.Core.Domain;
using UrlShortener.Core.Models;
using UrlShortener.Core.Safety;
using UrlShortener.Core.Validation;

namespace UrlShortener.Core.Services;

public sealed class ShortLinkService(
    IShortLinkRepository repository,
    IClickEventRepository clickRepository,
    IShortCodeGenerator codeGenerator,
    IShortLinkCache cache,
    IClickTracker clickTracker,
    IUrlSafetyValidator safety,
    ShortenerOptions options,
    AnalyticsOptions analytics,
    TimeProvider timeProvider) : IShortLinkService
{
    public const string DirectReferrer = "(direct)";
    internal const int MaxReferrerLength = 253;

    public async Task<Result<CreateShortLinkResult>> CreateAsync(
        CreateShortLinkCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (!TargetUrlValidator.TryNormalize(command.Url, options, out var url, out var urlError))
        {
            return ShortLinkErrors.InvalidUrl(urlError);
        }

        if (safety.Validate(new Uri(url)) is { } unsafeTarget)
        {
            return unsafeTarget;
        }

        var now = timeProvider.GetUtcNow();
        var expiresAt = command.ExpiresAt?.ToUniversalTime();
        if (expiresAt is { } expiry && expiry <= now)
        {
            return ShortLinkErrors.InvalidExpiry("The expiry must be in the future.");
        }

        if (!string.IsNullOrWhiteSpace(command.Alias))
        {
            return await CreateWithAliasAsync(command.Alias.Trim(), url, now, expiresAt, cancellationToken);
        }

        if (options.DeduplicateUrls && expiresAt is null)
        {
            var existing = await repository.FindReusableByUrlAsync(url, cancellationToken);
            if (existing is not null)
            {
                return new CreateShortLinkResult(existing, Created: false);
            }
        }

        for (var attempt = 0; attempt < options.MaxGenerationAttempts; attempt++)
        {
            var code = codeGenerator.Generate(options.CodeLength);
            if (AliasValidator.IsReserved(code, options))
            {
                continue;
            }

            var link = NewLink(code, url, now, expiresAt, isCustomAlias: false);
            if (await repository.TryAddAsync(link, cancellationToken))
            {
                cache.Invalidate(code);
                return new CreateShortLinkResult(link, Created: true);
            }
        }

        return ShortLinkErrors.CodeGenerationExhausted;
    }

    public async Task<Result<ShortLink>> ResolveAsync(
        string code,
        ClickContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!ShortCodeFormat.IsValid(code))
        {
            return ShortLinkErrors.NotFound(code);
        }

        var link = await FindCachedAsync(code, cancellationToken);
        if (link is null)
        {
            return ShortLinkErrors.NotFound(code);
        }

        var now = timeProvider.GetUtcNow();
        if (!link.IsActive)
        {
            return ShortLinkErrors.Deactivated(code);
        }

        if (link.IsExpired(now))
        {
            return ShortLinkErrors.Expired(code);
        }

        // Queued after the decision so it can never change the answer; a full queue only loses the click.
        clickTracker.TryTrack(new ClickEvent
        {
            Code = link.Code,
            OccurredAt = now,
            RefererHost = NormalizeReferrer(context.Referrer),
            UserAgentFamily = UserAgentClassifier.Classify(context.UserAgent),
        });

        return link;
    }

    public async Task<Result<ShortLink>> GetAsync(string code, CancellationToken cancellationToken = default)
    {
        var link = ShortCodeFormat.IsValid(code) ? await repository.GetByCodeAsync(code, cancellationToken) : null;
        return link is null ? ShortLinkErrors.NotFound(code) : link;
    }

    public async Task<Result<bool>> DeactivateAsync(string code, CancellationToken cancellationToken = default)
    {
        if (!ShortCodeFormat.IsValid(code) || !await repository.DeactivateAsync(code, cancellationToken))
        {
            return ShortLinkErrors.NotFound(code);
        }

        // Evict after the write commits so a concurrent read cannot re-cache the old row.
        cache.Invalidate(code);
        return true;
    }

    public async Task<Result<PagedResult<ShortLink>>> ListAsync(
        int? page,
        int? pageSize,
        CancellationToken cancellationToken = default)
    {
        var pageNumber = page ?? 1;
        var size = pageSize ?? options.DefaultPageSize;

        if (pageNumber < 1)
        {
            return ShortLinkErrors.InvalidPaging("The page must be 1 or greater.");
        }

        if (size < 1 || size > options.MaxPageSize)
        {
            return ShortLinkErrors.InvalidPaging($"The page size must be between 1 and {options.MaxPageSize}.");
        }

        return await repository.ListAsync(pageNumber, size, cancellationToken);
    }

    public async Task<Result<LinkStatistics>> GetStatisticsAsync(
        string code,
        DateTimeOffset? from,
        DateTimeOffset? to,
        CancellationToken cancellationToken = default)
    {
        var linkResult = await GetAsync(code, cancellationToken);
        if (!linkResult.IsSuccess)
        {
            return linkResult.Error!;
        }

        var windowEnd = (to ?? timeProvider.GetUtcNow()).ToUniversalTime();
        var windowStart = (from ?? windowEnd.AddDays(-analytics.DefaultStatsWindowDays)).ToUniversalTime();

        if (windowStart > windowEnd)
        {
            return ShortLinkErrors.RangeInverted;
        }

        if ((windowEnd - windowStart).TotalDays > analytics.MaxStatsRangeDays)
        {
            return ShortLinkErrors.RangeTooWide(analytics.MaxStatsRangeDays);
        }

        var link = linkResult.Value;
        var events = await clickRepository.GetRangeAsync(link.Code, windowStart, windowEnd, cancellationToken);

        var clicksByDay = events
            .GroupBy(e => DateOnly.FromDateTime(e.OccurredAt.UtcDateTime))
            .ToDictionary(g => g.Key, g => g.LongCount());

        var firstDay = DateOnly.FromDateTime(windowStart.UtcDateTime);
        var lastDay = DateOnly.FromDateTime(windowEnd.UtcDateTime);
        var daily = Enumerable.Range(0, lastDay.DayNumber - firstDay.DayNumber + 1)
            .Select(offset => firstDay.AddDays(offset))
            .Select(day => new DailyClickCount(day, clicksByDay.GetValueOrDefault(day)))
            .ToList();

        var topReferrers = events
            .GroupBy(e => e.RefererHost ?? DirectReferrer, StringComparer.Ordinal)
            .Select(g => new ReferrerClickCount(g.Key, g.LongCount()))
            .OrderByDescending(r => r.Clicks)
            .ThenBy(r => r.Referrer, StringComparer.Ordinal)
            .Take(analytics.TopReferrerCount)
            .ToList();

        return new LinkStatistics(
            link.Code,
            link.ClickCount,
            link.LastAccessedAt,
            windowStart,
            windowEnd,
            events.Count,
            daily,
            topReferrers);
    }

    private async Task<ShortLink?> FindCachedAsync(string code, CancellationToken cancellationToken)
    {
        switch (cache.Lookup(code, out var cached))
        {
            case CacheLookup.Found:
                return cached;
            case CacheLookup.KnownMissing:
                return null;
        }

        var link = await repository.GetByCodeAsync(code, cancellationToken);
        if (link is null)
        {
            cache.StoreMissing(code);
        }
        else
        {
            cache.StoreFound(link);
        }

        return link;
    }

    private async Task<Result<CreateShortLinkResult>> CreateWithAliasAsync(
        string alias,
        string url,
        DateTimeOffset now,
        DateTimeOffset? expiresAt,
        CancellationToken cancellationToken)
    {
        if (!ShortCodeFormat.IsValid(alias))
        {
            return ShortLinkErrors.InvalidAlias(
                $"An alias must be {ShortCodeFormat.MinLength}-{ShortCodeFormat.MaxLength} characters " +
                "and contain only letters, digits, '-' or '_'.");
        }

        if (AliasValidator.IsReserved(alias, options))
        {
            return ShortLinkErrors.ReservedAlias(alias);
        }

        // No pre-check: the unique index decides, so two concurrent claims cannot both win.
        var link = NewLink(alias, url, now, expiresAt, isCustomAlias: true);
        if (!await repository.TryAddAsync(link, cancellationToken))
        {
            return ShortLinkErrors.AliasTaken(alias);
        }

        cache.Invalidate(alias);
        return new CreateShortLinkResult(link, Created: true);
    }

    private static ShortLink NewLink(string code, string url, DateTimeOffset now, DateTimeOffset? expiresAt, bool isCustomAlias) =>
        new()
        {
            Code = code,
            OriginalUrl = url,
            CreatedAt = now,
            ExpiresAt = expiresAt,
            IsCustomAlias = isCustomAlias,
        };

    internal static string? NormalizeReferrer(string? referrer)
    {
        if (string.IsNullOrWhiteSpace(referrer)
            || !Uri.TryCreate(referrer.Trim(), UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return null;
        }

        var host = uri.Host.ToLowerInvariant();
        return host.Length <= MaxReferrerLength ? host : host[..MaxReferrerLength];
    }
}
