using UrlShortener.Core.Common;

namespace UrlShortener.Core.Safety;

/// <summary>One policy about which destinations may be shortened. Returns <c>null</c> when the URL is allowed.</summary>
public interface IUrlSafetyRule
{
    Error? Evaluate(Uri url);
}

public interface IUrlSafetyValidator
{
    Error? Validate(Uri url);
}

/// <summary>Runs every registered rule and reports the first specific rejection. New policy = one new rule class.</summary>
public sealed class UrlSafetyValidator(IEnumerable<IUrlSafetyRule> rules) : IUrlSafetyValidator
{
    private readonly IUrlSafetyRule[] _rules = [.. rules];

    public Error? Validate(Uri url)
    {
        ArgumentNullException.ThrowIfNull(url);
        foreach (var rule in _rules)
        {
            if (rule.Evaluate(url) is { } error)
            {
                return error;
            }
        }

        return null;
    }
}
