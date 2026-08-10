using WinFEBuilder.Core.Validation;
using Xunit;

namespace WinFEBuilder.Tests;

public class PathValidatorTests
{
    [Theory]
    [InlineData(@"C:\Windows")]
    [InlineData(@"C:\WinFEBuilder\workspace\Build_2026-07-20_143000")]
    [InlineData(@"D:\some folder\file.bat")]
    public void IsValidAbsolutePath_AcceptsRootedPaths(string path)
        => Assert.True(PathValidator.IsValidAbsolutePath(path));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("relative\\path")]
    [InlineData("file.txt")]
    [InlineData(null)]
    public void IsValidAbsolutePath_RejectsInvalidPaths(string? path)
        => Assert.False(PathValidator.IsValidAbsolutePath(path));

    [Fact]
    public void IsValidAbsolutePath_RejectsInvalidChars()
    {
        var bad = "C:\\bad\0path";
        Assert.False(PathValidator.IsValidAbsolutePath(bad));
    }

    [Fact]
    public void Quote_WrapsAndEscapes()
    {
        Assert.Equal("\"C:\\a b\\c\"", PathValidator.Quote(@"C:\a b\c"));
    }

    [Theory]
    [InlineData(@"C:\a", @"C:\a\b", true)]
    [InlineData(@"C:\a", @"C:\a", true)]
    [InlineData(@"C:\a", @"C:\ab", false)]
    [InlineData(@"C:\a", @"D:\a\b", false)]
    public void IsSameOrUnder_Works(string parent, string child, bool expected)
        => Assert.Equal(expected, PathValidator.IsSameOrUnder(parent, child));

    [Fact]
    public void EnsureExistingFile_ThrowsForMissingFile()
        => Assert.Throws<FileNotFoundException>(() =>
            PathValidator.EnsureExistingFile(@"C:\definitely\not\here\nope.xyz"));
}
