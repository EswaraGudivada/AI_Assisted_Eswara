using System.Diagnostics.CodeAnalysis;

namespace UrlShortener.Core.Validation;

public static class AliasValidator
{
    public static bool TryValidate(string alias, ShortenerOptions options, [NotNullWhen(false)] out string? error)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (!ShortCodeFormat.IsValid(alias))
        {
            error = $"An alias must be {ShortCodeFormat.MinLength}-{ShortCodeFormat.MaxLength} characters " +
                    "and contain only letters, digits, '-' or '_'.";
            return false;
        }

        if (IsReserved(alias, options))
        {
            error = $"The alias '{alias}' is reserved.";
            return false;
        }

        error = null;
        return true;
    }

    public static bool IsReserved(string code, ShortenerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return options.ReservedAliases.Contains(code, StringComparer.OrdinalIgnoreCase);
    }
}
