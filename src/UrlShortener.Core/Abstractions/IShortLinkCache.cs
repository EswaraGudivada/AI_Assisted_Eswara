using System.Diagnostics.CodeAnalysis;
using UrlShortener.Core.Domain;

namespace UrlShortener.Core.Abstractions;

public interface IShortLinkCache
{
    bool TryGet(string code, [NotNullWhen(true)] out ShortLink? link);

    void Set(ShortLink link);

    void Remove(string code);
}
