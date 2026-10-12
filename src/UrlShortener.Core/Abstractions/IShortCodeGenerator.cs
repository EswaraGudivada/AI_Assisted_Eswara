namespace UrlShortener.Core.Abstractions;

public interface IShortCodeGenerator
{
    string Generate(int length);
}
