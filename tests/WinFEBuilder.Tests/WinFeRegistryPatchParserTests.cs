using WinFEBuilder.Core.Validation;
using Xunit;

namespace WinFEBuilder.Tests;

public class WinFeRegistryPatchParserTests
{
    /// <summary>
    /// One architecture block, matching the shape used by IntelWinFE's MakeWinFEx64-x86.bat.
    /// </summary>
    private const string OneBlock = @"
dism /mount-wim /wimfile:x64\sources\boot.wim /index:1 /mountdir:Temp\mount
copy x64\explorerframe.dll Temp\mount\windows\system32
reg load HKLM\FE_SOFTWARE Temp\Mount\Windows\System32\Config\SOFTWARE
reg load HKLM\FE_SYSTEM Temp\Mount\Windows\System32\Config\SYSTEM
reg load HKLM\FE_USER Temp\Mount\Windows\System32\Config\DEFAULT
reg add HKLM\FE_SYSTEM\ControlSet001\Services\partmgr\Parameters /v SanPolicy /t REG_DWORD /d 3 /f
reg add HKLM\FE_SYSTEM\ControlSet001\Services\MountMgr\ /v NoAutoMount /t REG_DWORD /d 1 /f
reg add HKLM\FE_SOFTWARE\Classes\CLSID\{AE054212-3535-4430-83ED-D501AA6680E6} /ve /t REG_SZ /d ""Shell Name Space ListView"" /f
reg add ""HKLM\FE_USER\Control Panel\Desktop"" /v ""Wallpaper"" /d ""%%systemroot%%\system32\wallpaper.jpg"" /t ""REG_SZ"" /f
reg unload HKLM\FE_SOFTWARE
reg unload HKLM\FE_SYSTEM
reg unload HKLM\FE_USER
";

    [Fact]
    public void Parse_ExtractsLoadsAddsAndUnloads()
    {
        var ops = WinFeRegistryPatchParser.Parse(OneBlock);

        Assert.Equal(3, ops.Count(o => o.Verb == WinFeRegistryVerb.Load));
        Assert.Equal(4, ops.Count(o => o.Verb == WinFeRegistryVerb.Add));
        Assert.Equal(3, ops.Count(o => o.Verb == WinFeRegistryVerb.Unload));
    }

    [Fact]
    public void Parse_RebasesHivePathOntoImageRoot()
    {
        var load = WinFeRegistryPatchParser.Parse(OneBlock)
            .First(o => o.Verb == WinFeRegistryVerb.Load && o.HiveKey == @"HKLM\FE_SYSTEM");

        // The script's own mount folder must be stripped so we can rebase onto our mount dir.
        Assert.Equal(@"Windows\System32\Config\SYSTEM", load.HiveFileRelativePath);
    }

    [Fact]
    public void Parse_KeepsWriteProtectionSettings()
    {
        var adds = WinFeRegistryPatchParser.Parse(OneBlock)
            .Where(o => o.Verb == WinFeRegistryVerb.Add).ToList();

        var san = adds.First(a => a.Arguments.Contains("SanPolicy"));
        Assert.Equal(@"HKLM\FE_SYSTEM\ControlSet001\Services\partmgr\Parameters", san.Arguments[0]);
        Assert.Contains("3", san.Arguments);

        Assert.Contains(adds, a => a.Arguments.Contains("NoAutoMount"));
    }

    [Fact]
    public void Parse_TreatsQuotedValueWithSpacesAsSingleArgument()
    {
        var clsid = WinFeRegistryPatchParser.Parse(OneBlock)
            .First(o => o.Verb == WinFeRegistryVerb.Add && o.Arguments[0].Contains("AE054212"));

        // "Shell Name Space ListView" must survive as one argument, not four.
        Assert.Contains("Shell Name Space ListView", clsid.Arguments);
    }

    [Fact]
    public void Parse_UnescapesBatchPercentSigns()
    {
        var wallpaper = WinFeRegistryPatchParser.Parse(OneBlock)
            .First(o => o.Verb == WinFeRegistryVerb.Add && o.Arguments[0].Contains("Control Panel"));

        Assert.Contains(@"%systemroot%\system32\wallpaper.jpg", wallpaper.Arguments);
        Assert.DoesNotContain(wallpaper.Arguments, a => a.Contains("%%"));
    }

    [Fact]
    public void Parse_DualArchScript_ReturnsOnlyFirstBlock()
    {
        // Both architecture blocks are identical; the patches are replayed per boot.wim, so
        // returning both would apply everything twice.
        var ops = WinFeRegistryPatchParser.Parse(OneBlock + OneBlock);
        var single = WinFeRegistryPatchParser.Parse(OneBlock);

        Assert.Equal(single.Count, ops.Count);
    }

    [Fact]
    public void Parse_IgnoresCommentedOutCommands()
    {
        const string script = @"
:: reg add HKLM\FE_SYSTEM\Foo /v Bar /t REG_DWORD /d 1 /f
rem reg add HKLM\FE_SYSTEM\Baz /v Qux /t REG_DWORD /d 1 /f
reg add HKLM\FE_SYSTEM\Real /v Live /t REG_DWORD /d 1 /f
";
        var ops = WinFeRegistryPatchParser.Parse(script);

        Assert.Single(ops);
        Assert.Equal(@"HKLM\FE_SYSTEM\Real", ops[0].Arguments[0]);
    }

    [Fact]
    public void Parse_SkipsLoadWithUnrecognisedHiveLocation()
    {
        const string script = "reg load HKLM\\FE_ODD C:\\somewhere\\else\\HIVE\n";
        Assert.Empty(WinFeRegistryPatchParser.Parse(script));
    }

    [Fact]
    public void Parse_EmptyOrNullInput_ReturnsEmpty()
    {
        Assert.Empty(WinFeRegistryPatchParser.Parse(""));
        Assert.Empty(WinFeRegistryPatchParser.Parse("   "));
    }

    [Fact]
    public void Parse_ScriptWithNoRegistryCommands_ReturnsEmpty()
    {
        const string script = "copy x64\\explorer.exe Temp\\mount\\windows\\system32\ndism /unmount-wim /commit\n";
        Assert.Empty(WinFeRegistryPatchParser.Parse(script));
    }
}
