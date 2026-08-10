using WinFEBuilder.Core.Validation;
using Xunit;

namespace WinFEBuilder.Tests;

public class FrameworkValidatorTests
{
    [Theory]
    [InlineData("MakeWinFEx64-x86.bat", true)]
    [InlineData("Makex64-x86-CD.bat", true)]
    [InlineData("MakePE.bat", true)]
    [InlineData("build-winfe.bat", true)]     // heuristic: contains 'winfe'
    [InlineData("readme.txt", false)]
    [InlineData("random.bat", false)]
    [InlineData("", false)]
    public void IsBuildScript_ClassifiesCorrectly(string name, bool expected)
        => Assert.Equal(expected, FrameworkValidator.IsBuildScript(name));

    [Theory]
    [InlineData("tool.exe", true)]
    [InlineData("lib.dll", true)]
    [InlineData("boot.wim", true)]
    [InlineData("notes.txt", false)]
    public void IsFrameworkComponent_ClassifiesCorrectly(string name, bool expected)
        => Assert.Equal(expected, FrameworkValidator.IsFrameworkComponent(name));

    [Fact]
    public void IsLikelyDoubleNested_TrueWhenScriptsOnlyInChild()
    {
        var topLevel = new[] { "readme.txt", "license.rtf" };
        Assert.True(FrameworkValidator.IsLikelyDoubleNested(topLevel, childDirectoriesWithScripts: 1));
    }

    [Fact]
    public void IsLikelyDoubleNested_FalseWhenTopLevelHasScripts()
    {
        var topLevel = new[] { "MakeWinFEx64-x86.bat", "readme.txt" };
        Assert.False(FrameworkValidator.IsLikelyDoubleNested(topLevel, childDirectoriesWithScripts: 1));
    }

    [Fact]
    public void IsLikelyDoubleNested_FalseWhenNoChildScripts()
    {
        var topLevel = new[] { "readme.txt" };
        Assert.False(FrameworkValidator.IsLikelyDoubleNested(topLevel, childDirectoriesWithScripts: 0));
    }

    [Theory]
    [InlineData(new[] { "MakeWinFEx64-x86.bat" }, true)]
    [InlineData(new[] { "tool.amd64.dll" }, true)]
    [InlineData(new[] { "MakeWinFEx86.bat" }, false)]
    public void AppearsToSupportX64_Detects64BitHints(string[] names, bool expected)
        => Assert.Equal(expected, FrameworkValidator.AppearsToSupportX64(names));
}
