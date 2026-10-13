using UrlShortener.Api.Http;
using UrlShortener.Core.Common;

namespace UrlShortener.Api.Infrastructure;

internal static class ErrorResults
{
    /// <summary>
    /// Maps a domain error to RFC 7807 Problem Details. Well-formed requests with unacceptable content get 422;
    /// 400 is left to the framework for bodies that cannot be read at all.
    /// </summary>
    public static IResult ToProblem(this Error error, HttpContext http)
    {
        var status = error.Type switch
        {
            ErrorType.Validation => StatusCodes.Status422UnprocessableEntity,
            ErrorType.NotFound => StatusCodes.Status404NotFound,
            ErrorType.Conflict => StatusCodes.Status409Conflict,
            ErrorType.Gone => StatusCodes.Status410Gone,
            ErrorType.Unavailable => StatusCodes.Status503ServiceUnavailable,
            _ => StatusCodes.Status500InternalServerError,
        };

        return Results.Problem(
            statusCode: status,
            detail: error.Message,
            extensions: new Dictionary<string, object?>
            {
                ["code"] = error.Code,
                ["correlationId"] = CorrelationIdMiddleware.Get(http),
            });
    }
}
