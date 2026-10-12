namespace UrlShortener.Core.Common;

public enum ErrorType
{
    Validation,
    NotFound,
    Conflict,
    Gone,
    Unavailable,
}

public sealed record Error(string Code, string Message, ErrorType Type);
