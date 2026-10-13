using System.Text.RegularExpressions;

namespace UrlShortener.Core.Validation;

public static partial class ShortCodeFormat
{
    public const int MinLength = 4;
    public const int MaxLength = 32;

    public static bool IsValid(string? code) =>
        code is { Length: >= MinLength and <= MaxLength } && AllowedCharacters().IsMatch(code);

    [GeneratedRegex("^[A-Za-z0-9_-]+$", RegexOptions.CultureInvariant)]
    private static partial Regex AllowedCharacters();
}
