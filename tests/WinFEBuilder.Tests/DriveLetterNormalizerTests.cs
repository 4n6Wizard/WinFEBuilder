using WinFEBuilder.Core.Validation;
using Xunit;

namespace WinFEBuilder.Tests;

public class DriveLetterNormalizerTests
{
    [Theory]
    [InlineData("K", "K:")]
    [InlineData("K:", "K:")]
    [InlineData("K:\\", "K:")]
    [InlineData("k", "K:")]
    [InlineData("k:", "K:")]
    [InlineData("  K:  ", "K:")]
    [InlineData("F", "F:")]
    [InlineData("F:\\", "F:")]
    [InlineData("C:\\some\\dir", "C:")] // rooted path collapses to its drive letter
    public void Normalize_ReturnsCanonicalForm(string input, string expected)
        => Assert.Equal(expected, DriveLetterNormalizer.Normalize(input));

    [Fact]
    public void Root_AppendsSingleBackslash()
    {
        Assert.Equal("K:\\", DriveLetterNormalizer.Root("K"));
        Assert.Equal("K:\\", DriveLetterNormalizer.Root("K:"));
        Assert.Equal("K:\\", DriveLetterNormalizer.Root("K:\\"));
    }

    [Fact]
    public void Normalize_NeverProducesDoubleColon()
    {
        foreach (var input in new[] { "K", "K:", "K:\\", "F", "F:", "F:\\" })
        {
            var norm = DriveLetterNormalizer.Normalize(input);
            Assert.DoesNotContain("::", norm);
            Assert.Equal(2, norm.Length); // exactly "X:"
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("1")]
    [InlineData("::")]
    [InlineData("Kx")]
    public void Normalize_RejectsInvalid(string input)
        => Assert.Throws<System.ArgumentException>(() => DriveLetterNormalizer.Normalize(input));

    [Fact]
    public void TryNormalize_ReturnsNullOnInvalid()
    {
        Assert.Null(DriveLetterNormalizer.TryNormalize(""));
        Assert.Equal("K:", DriveLetterNormalizer.TryNormalize("k:\\"));
    }
}
