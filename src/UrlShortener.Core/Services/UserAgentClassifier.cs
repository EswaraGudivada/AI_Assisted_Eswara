namespace UrlShortener.Core.Services;

/// <summary>Reduces a user-agent string to a closed set of families, so the stored value is not a fingerprint.</summary>
public static class UserAgentClassifier
{
    public static string? Classify(string? userAgent)
    {
        if (string.IsNullOrWhiteSpace(userAgent))
        {
            return null;
        }

        var agent = userAgent.ToLowerInvariant();

        // Automated traffic first; Edge/Opera before Chrome because their strings contain "chrome".
        return agent switch
        {
            _ when ContainsAny(agent, "bot", "crawler", "spider", "slurp") => "bot",
            _ when ContainsAny(agent, "curl", "wget", "httpie", "postman", "insomnia") => "tool",
            _ when ContainsAny(agent, "edg/", "edge") => "edge",
            _ when ContainsAny(agent, "opr/", "opera") => "opera",
            _ when ContainsAny(agent, "chrome", "crios") => "chrome",
            _ when ContainsAny(agent, "firefox", "fxios") => "firefox",
            _ when ContainsAny(agent, "safari") => "safari",
            _ => "other",
        };
    }

    private static bool ContainsAny(string agent, params ReadOnlySpan<string> needles)
    {
        foreach (var needle in needles)
        {
            if (agent.Contains(needle, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
