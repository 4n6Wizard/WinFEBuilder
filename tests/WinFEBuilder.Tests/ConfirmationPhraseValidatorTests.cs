using WinFEBuilder.Core.Validation;
using Xunit;

namespace WinFEBuilder.Tests;

public class ConfirmationPhraseValidatorTests
{
    [Fact]
    public void BuildExpectedPhrase_FormatsCorrectly()
        => Assert.Equal("ERASE DISK 3", ConfirmationPhraseValidator.BuildExpectedPhrase(3));

    [Theory]
    [InlineData("ERASE DISK 3", 3, true)]
    [InlineData("  ERASE DISK 3  ", 3, true)] // surrounding whitespace allowed
    [InlineData("ERASE DISK 3", 4, false)]    // wrong disk number
    [InlineData("erase disk 3", 3, false)]    // case-sensitive
    [InlineData("ERASE  DISK 3", 3, false)]   // extra internal space
    [InlineData("ERASE DISK 03", 3, false)]   // leading zero
    [InlineData("", 3, false)]
    [InlineData(null, 3, false)]
    [InlineData("DELETE DISK 3", 3, false)]
    public void IsValid_MatchesExactPhraseOnly(string? typed, int disk, bool expected)
        => Assert.Equal(expected, ConfirmationPhraseValidator.IsValid(typed, disk));
}
