using UrlShortener.Core.Abstractions;
using UrlShortener.Core.Common;
using UrlShortener.Core.Domain;
using UrlShortener.Core.Models;
using UrlShortener.Core.Validation;

namespace UrlShortener.Core.Services;

public sealed class ShortLinkService(
    IShortLinkRepository repository,
    IClickEventRepository clickRepository,
    IShortCodeGenerator codeGenerator,
    IShortLinkCache cache,
    IClickTracker clickTracker,
    ShortenerOptions options,
    TimeProvider timeProvider) : IShortLinkService
{
    public const int MaxPageSize = 100;
    public const int TopReferrerCount = 5;
    public const string DirectReferrer = "(direct)";
    internal const int MaxReferrerLength = 255;
    internal const int MaxUserAgentLength = 512;

    public async Task<Result<CreateShortLinkResult>> CreateAsync(
        CreateShortLinkCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (!TargetUrlValidator.TryNormalize(command.Url, options, out var url, out var urlError))
        {
            return ShortLinkErrors.InvalidUrl(urlError);
        }

        var now = timeProvider.GetUtcNow();
        var expiresAt = command.ExpiresAt?.ToUniversalTime();
        if (expiresAt is { } expiry && expiry <= now)
        {
            return ShortLinkErrors.InvalidExpiry("The expiry must be in the future.");
        }

        if (!string.IsNullOrWhiteSpace(command.CustomAlias))
        {
            return await CreateWithAliasAsync(command.CustomAlias.Trim(), url, now, expiresAt, cancellationToken);
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
                return new CreateShortLinkResult(link, Created: true);
            }
        }

        return ShortLinkErrors.CodeGenerationFailed;
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

        if (!cache.TryGet(code, out var link))
        {
            link = await repository.GetByCodeAsync(code, cancellationToken);
            if (link is null)
            {
                return ShortLinkErrors.NotFound(code);
            }

            cache.Set(link);
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

        clickTracker.TryTrack(new ClickEvent
        {
            Code = link.Code,
            OccurredAt = now,
            Referrer = NormalizeReferrer(context.Referrer),
            UserAgent = Truncate(context.UserAgent, MaxUserAgentLength),
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

        cache.Remove(code);
        return true;
    }

    public async Task<Result<PagedResult<ShortLink>>> ListAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        if (page < 1)
        {
            return ShortLinkErrors.InvalidPaging("The page must be 1 or greater.");
        }

        if (pageSize is < 1 or > MaxPageSize)
        {
            return ShortLinkErrors.InvalidPaging($"The page size must be between 1 and {MaxPageSize}.");
        }

        return await repository.ListAsync(page, pageSize, cancellationToken);
    }

    public async Task<Result<LinkStatistics>> GetStatisticsAsync(
        string code,
        int days,
        CancellationToken cancellationToken = default)
    {
        if (days < 1 || days > options.MaxStatsWindowDays)
        {
            return ShortLinkErrors.InvalidStatsWindow(options.MaxStatsWindowDays);
        }

        var linkResult = await GetAsync(code, cancellationToken);
        if (!linkResult.IsSuccess)
        {
            return linkResult.Error!;
        }

        var link = linkResult.Value;
        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
        var firstDay = today.AddDays(-(days - 1));
        var since = new DateTimeOffset(firstDay.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

        var events = await clickRepository.GetSinceAsync(link.Code, since, cancellationToken);

        var clicksByDay = events
            .GroupBy(e => DateOnly.FromDateTime(e.OccurredAt.UtcDateTime))
            .ToDictionary(g => g.Key, g => g.Count());

        var daily = Enumerable.Range(0, days)
            .Select(offset => firstDay.AddDays(offset))
            .Select(day => new DailyClickCount(day, clicksByDay.GetValueOrDefault(day)))
            .ToList();

        var topReferrers = events
            .GroupBy(e => e.Referrer ?? DirectReferrer, StringComparer.Ordinal)
            .Select(g => new ReferrerCount(g.Key, g.Count()))
            .OrderByDescending(r => r.Clicks)
            .ThenBy(r => r.Referrer, StringComparer.Ordinal)
            .Take(TopReferrerCount)
            .ToList();

        return new LinkStatistics(link.Code, link.ClickCount, link.LastAccessedAt, days, daily, topReferrers);
    }

    private async Task<Result<CreateShortLinkResult>> CreateWithAliasAsync(
        string alias,
        string url,
        DateTimeOffset now,
        DateTimeOffset? expiresAt,
        CancellationToken cancellationToken)
    {
        if (!AliasValidator.TryValidate(alias, options, out var aliasError))
        {
            return ShortLinkErrors.InvalidAlias(aliasError);
        }

        var link = NewLink(alias, url, now, expiresAt, isCustomAlias: true);
        return await repository.TryAddAsync(link, cancellationToken)
            ? new CreateShortLinkResult(link, Created: true)
            : ShortLinkErrors.AliasTaken(alias);
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

        return Truncate(uri.Host.ToLowerInvariant(), MaxReferrerLength);
    }

    private static string? Truncate(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }
}
