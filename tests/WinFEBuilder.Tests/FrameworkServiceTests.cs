using WinFEBuilder.Core.Configuration;
using WinFEBuilder.Core.Hashing;
using WinFEBuilder.Core.Logging;
using WinFEBuilder.Core.Models;
using WinFEBuilder.Core.Services;
using Xunit;

namespace WinFEBuilder.Tests;

public class FrameworkServiceTests
{
    private static FrameworkService Build(TempDir tmp, out SettingsService settings)
    {
        settings = new SettingsService(Path.Combine(tmp.Path, "settings.json"));
        settings.Settings.WorkspaceRoot = tmp.Dir("workspace");
        var log = new LogService(tmp.Dir("logs"));
        var hash = new HashService();
        var ws = new WorkspaceService(settings, log);
        return new FrameworkService(log, hash, ws, settings);
    }

    [Fact]
    public async Task Validate_ValidFramework_Passes()
    {
        using var tmp = new TempDir();
        var fwDir = tmp.Dir("framework");
        tmp.File(@"framework\MakeWinFEx64-x86.bat", "@echo off\r\necho building");
        tmp.File(@"framework\Makex64-x86-CD.bat", "@echo off\r\necho iso");
        tmp.Dir(@"framework\Drivers");
        tmp.Dir(@"framework\Programs");
        tmp.Dir(@"framework\Wallpaper");
        tmp.File(@"framework\winfe.exe", "MZfake");

        var svc = Build(tmp, out _);
        var result = await svc.ValidateAsync(fwDir);

        Assert.True(result.IsValid);
        Assert.Equal(CheckStatus.Pass, result.Status);
        Assert.Contains(result.BuildScripts, s => s.Name == "MakeWinFEx64-x86.bat");
        Assert.True(result.SupportsX64);
        // hashes computed
        Assert.All(result.BuildScripts, s => Assert.False(string.IsNullOrEmpty(s.Sha256)));
    }

    [Fact]
    public async Task Validate_MissingDirectory_Fails()
    {
        using var tmp = new TempDir();
        var svc = Build(tmp, out _);
        var result = await svc.ValidateAsync(Path.Combine(tmp.Path, "does-not-exist"));
        Assert.False(result.IsValid);
        Assert.Equal(CheckStatus.Fail, result.Status);
    }

    [Fact]
    public async Task Validate_ZeroByteScript_Fails()
    {
        using var tmp = new TempDir();
        var fwDir = tmp.Dir("framework");
        tmp.File(@"framework\MakeWinFEx64-x86.bat", ""); // zero bytes

        var svc = Build(tmp, out _);
        var result = await svc.ValidateAsync(fwDir);

        Assert.False(result.IsValid);
        Assert.Equal(CheckStatus.Fail, result.Status);
        Assert.Contains(result.Warnings, w => w.Contains("zero bytes"));
    }

    [Fact]
    public async Task Validate_DetectsDoubleNesting()
    {
        using var tmp = new TempDir();
        var outer = tmp.Dir("outer");
        tmp.File(@"outer\inner\MakeWinFEx64-x86.bat", "@echo off");
        tmp.File(@"outer\readme.txt", "hello");

        var svc = Build(tmp, out _);
        var result = await svc.ValidateAsync(outer);

        Assert.True(result.PossibleDoubleNesting);
    }

    [Fact]
    public async Task CopyToWorkspace_PreservesEmptyPlaceholderDirectories()
    {
        // WinFE frameworks ship empty sources\ folders the build batch copies boot.wim into.
        using var tmp = new TempDir();
        var fwDir = tmp.Dir("framework");
        tmp.File(@"framework\MakeWinFEx64-x86.bat", "@echo off\r\necho x64");
        tmp.Dir(@"framework\USB\x86-x64\x64\sources");   // empty placeholder
        tmp.Dir(@"framework\USB\x86-x64\x86\sources");   // empty placeholder

        var svc = Build(tmp, out _);
        var validation = await svc.ValidateAsync(fwDir);
        var copy = await svc.CopyToWorkspaceAsync(validation);

        Assert.True(copy.Success);
        var workspace = copy.OutputPaths[0];
        Assert.True(Directory.Exists(Path.Combine(workspace, "framework", "USB", "x86-x64", "x64", "sources")),
            "Empty x64\\sources placeholder must be recreated in the workspace.");
        Assert.True(Directory.Exists(Path.Combine(workspace, "framework", "USB", "x86-x64", "x86", "sources")),
            "Empty x86\\sources placeholder must be recreated in the workspace.");
    }

    [Fact]
    public async Task CopyToWorkspace_CreatesCopyAndManifest_OriginalUntouched()
    {
        using var tmp = new TempDir();
        var fwDir = tmp.Dir("framework");
        var scriptPath = tmp.File(@"framework\MakeWinFEx64-x86.bat", "@echo off\r\necho x64");
        tmp.File(@"framework\Programs\tool.exe", "MZ");

        var svc = Build(tmp, out _);
        var validation = await svc.ValidateAsync(fwDir);
        Assert.True(validation.IsValid);

        var originalContentBefore = File.ReadAllText(scriptPath);
        var copy = await svc.CopyToWorkspaceAsync(validation);

        Assert.True(copy.Success);
        Assert.NotEmpty(copy.OutputPaths);

        // Original untouched.
        Assert.Equal(originalContentBefore, File.ReadAllText(scriptPath));

        // Manifest exists and references files.
        var manifestPath = copy.OutputPaths.First(p => p.EndsWith("workspace-manifest.json"));
        Assert.True(File.Exists(manifestPath));

        // Copied framework contains the script.
        var workspace = copy.OutputPaths[0];
        Assert.True(File.Exists(Path.Combine(workspace, "framework", "MakeWinFEx64-x86.bat")));
    }
}
