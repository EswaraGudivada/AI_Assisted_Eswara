using UrlShortener.Core.Services;

namespace UrlShortener.UnitTests.Services;

public class RandomShortCodeGeneratorTests
{
    private readonly RandomShortCodeGenerator _generator = new();

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(16)]
    public void Generates_codes_of_the_requested_length_from_the_base62_alphabet(int length)
    {
        var code = _generator.Generate(length);

        Assert.Equal(length, code.Length);
        Assert.All(code, c => Assert.Contains(c, RandomShortCodeGenerator.Alphabet));
    }

    [Fact]
    public void Generated_codes_are_effectively_unique()
    {
        var codes = Enumerable.Range(0, 10_000).Select(_ => _generator.Generate(7)).ToHashSet(StringComparer.Ordinal);

        Assert.Equal(10_000, codes.Count);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Rejects_non_positive_lengths(int length)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => _generator.Generate(length));
    }
}
