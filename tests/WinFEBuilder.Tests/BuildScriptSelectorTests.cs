using WinFEBuilder.Core.Validation;
using Xunit;

namespace WinFEBuilder.Tests;

public class BuildScriptSelectorTests
{
    private static readonly string[] Intel = { "MakeWinFEx64-x86.bat", "Makex64-x86-CD.bat" };

    [Fact]
    public void SelectIsoScript_PicksCdScript()
        => Assert.Equal("Makex64-x86-CD.bat", BuildScriptSelector.SelectIsoScript(Intel));

    [Fact]
    public void SelectMediaScript_PrefersMakeWinFE_NotCd()
        => Assert.Equal("MakeWinFEx64-x86.bat", BuildScriptSelector.SelectMediaScript(Intel));

    [Theory]
    [InlineData("Makex64-x86-CD.bat", true)]
    [InlineData("Build-ISO.bat", true)]
    [InlineData("MakeDVD.bat", true)]
    [InlineData("MakeWinFEx64-x86.bat", false)]
    public void IsIsoScript_Classifies(string name, bool expected)
        => Assert.Equal(expected, BuildScriptSelector.IsIsoScript(name));

    [Fact]
    public void SelectMediaScript_FallsBackToFirstNonIso()
    {
        var scripts = new[] { "Makex64-x86-CD.bat", "custom-build.bat" };
        Assert.Equal("custom-build.bat", BuildScriptSelector.SelectMediaScript(scripts));
    }

    [Fact]
    public void Selectors_EmptyInput_ReturnNull()
    {
        Assert.Null(BuildScriptSelector.SelectIsoScript(Array.Empty<string>()));
        Assert.Null(BuildScriptSelector.SelectMediaScript(Array.Empty<string>()));
    }
}
