namespace UrlShortener.Core.Common;

/// <summary>Every expected failure, with a stable snake_case code that clients can branch on.</summary>
public static class ShortLinkErrors
{
    public static Error InvalidUrl(string reason) => new("url_invalid", reason, ErrorType.Validation);

    public static readonly Error PrivateTarget = new(
        "url_private_target",
        "The target URL must point at a public host. Private, loopback, and link-local addresses are not allowed.",
        ErrorType.Validation);

    public static readonly Error DeniedHost = new("url_host_denied", "The target host is not permitted.", ErrorType.Validation);

    public static Error InvalidAlias(string reason) => new("alias_invalid", reason, ErrorType.Validation);

    public static Error ReservedAlias(string alias) => new("alias_reserved", $"The alias '{alias}' is reserved.", ErrorType.Validation);

    public static Error InvalidExpiry(string reason) => new("expiry_invalid", reason, ErrorType.Validation);

    public static Error InvalidPaging(string reason) => new("paging_invalid", reason, ErrorType.Validation);

    public static readonly Error RangeInverted = new("range_inverted", "'from' must not be later than 'to'.", ErrorType.Validation);

    public static Error RangeTooWide(int maxDays) =>
        new("range_too_wide", $"The requested range must not exceed {maxDays} days.", ErrorType.Validation);

    public static Error AliasTaken(string alias) =>
        new("alias_taken", $"The alias '{alias}' is already in use.", ErrorType.Conflict);

    public static Error NotFound(string code) =>
        new("link_not_found", $"No short link exists for code '{code}'.", ErrorType.NotFound);

    public static Error Expired(string code) =>
        new("link_expired", $"The short link '{code}' has expired.", ErrorType.Gone);

    public static Error Deactivated(string code) =>
        new("link_deactivated", $"The short link '{code}' has been deactivated.", ErrorType.Gone);

    public static readonly Error CodeGenerationExhausted = new(
        "code_generation_exhausted",
        "A unique short code could not be generated. Please retry.",
        ErrorType.Unavailable);
}
