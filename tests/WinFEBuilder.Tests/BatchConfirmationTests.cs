using WinFEBuilder.Core.Validation;
using Xunit;

namespace WinFEBuilder.Tests;

public class BatchConfirmationTests
{
    [Fact]
    public void SingleDisk_UsesEraseDiskN()
        => Assert.Equal("ERASE DISK 8", BatchConfirmationValidator.Expected(new[] { 8 }));

    [Fact]
    public void MultipleDisks_UsesEraseCountDisks()
        => Assert.Equal("ERASE 4 DISKS", BatchConfirmationValidator.Expected(new[] { 8, 9, 10, 11 }));

    [Theory]
    [InlineData(new[] { 8 }, "ERASE DISK 8", true)]
    [InlineData(new[] { 8 }, "ERASE 1 DISKS", false)]         // single must use disk-number form
    [InlineData(new[] { 8, 9, 10 }, "ERASE 3 DISKS", true)]
    [InlineData(new[] { 8, 9, 10 }, "ERASE 4 DISKS", false)]  // count must match
    [InlineData(new[] { 8, 9 }, "erase 2 disks", false)]      // case-sensitive
    [InlineData(new[] { 8, 9 }, "  ERASE 2 DISKS  ", true)]   // surrounding whitespace ok
    public void IsValid_MatchesExactPhrase(int[] disks, string typed, bool expected)
        => Assert.Equal(expected, BatchConfirmationValidator.IsValid(typed, disks));

    [Fact]
    public void EmptySelection_IsNeverValid()
        => Assert.False(BatchConfirmationValidator.IsValid("ERASE 0 DISKS", System.Array.Empty<int>()));

    [Fact]
    public void PhraseChangesWithSelectionCount()
    {
        // Adding a disk changes the required phrase — so a previously-correct phrase becomes invalid.
        var two = new[] { 8, 9 };
        var three = new[] { 8, 9, 10 };
        var typedForTwo = BatchConfirmationValidator.Expected(two);
        Assert.True(BatchConfirmationValidator.IsValid(typedForTwo, two));
        Assert.False(BatchConfirmationValidator.IsValid(typedForTwo, three));
    }
}
