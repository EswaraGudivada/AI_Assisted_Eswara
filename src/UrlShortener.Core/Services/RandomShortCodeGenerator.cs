using System.Security.Cryptography;
using UrlShortener.Core.Abstractions;

namespace UrlShortener.Core.Services;

/// <summary>
/// Generates unbiased, non-sequential Base62 codes using a CSPRNG so codes cannot be enumerated.
/// 7 characters give 62^7 (about 3.5 trillion) combinations.
/// </summary>
public sealed class RandomShortCodeGenerator : IShortCodeGenerator
{
    public const string Alphabet = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";

    public string Generate(int length)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(length, 1);
        return RandomNumberGenerator.GetString(Alphabet, length);
    }
}
