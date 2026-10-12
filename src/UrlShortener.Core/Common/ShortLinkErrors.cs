namespace UrlShortener.Core.Common;

public static class ShortLinkErrors
{
    public static Error InvalidUrl(string reason) => new("url.invalid", reason, ErrorType.Validation);

    public static Error InvalidAlias(string reason) => new("alias.invalid", reason, ErrorType.Validation);

    public static Error InvalidExpiry(string reason) => new("expiry.invalid", reason, ErrorType.Validation);

    public static Error InvalidPaging(string reason) => new("paging.invalid", reason, ErrorType.Validation);

    public static Error InvalidStatsWindow(int maxDays) =>
        new("stats.window_invalid", $"The statistics window must be between 1 and {maxDays} days.", ErrorType.Validation);

    public static Error AliasTaken(string alias) =>
        new("alias.conflict", $"The alias '{alias}' is already in use.", ErrorType.Conflict);

    public static Error NotFound(string code) =>
        new("link.not_found", $"No short link exists for code '{code}'.", ErrorType.NotFound);

    public static Error Expired(string code) =>
        new("link.expired", $"The short link '{code}' has expired.", ErrorType.Gone);

    public static Error Deactivated(string code) =>
        new("link.deactivated", $"The short link '{code}' has been deactivated.", ErrorType.Gone);

    public static readonly Error CodeGenerationFailed =
        new("code.generation_failed", "A unique short code could not be generated. Please retry.", ErrorType.Unavailable);
}
