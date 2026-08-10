using Microsoft.Extensions.DependencyInjection;
using WinFEBuilder.Core;
using WinFEBuilder.Core.Configuration;
using WinFEBuilder.Core.Services;
using Xunit;

namespace WinFEBuilder.Tests;

public class PortablePathsTests
{
    [Fact]
    public void RelativeRoots_ResolveBesideTheApp()
    {
        using var tmp = new TempDir();
        var paths = new AppPaths(tmp.Path); // temp is outside the solution, so RootDir == tmp.Path

        var provider = new ServiceCollection().AddWinFeBuilderCore(paths).BuildServiceProvider();
        var settings = provider.GetRequiredService<ISettingsService>().Settings;

        // No settings.json exists in temp, so the relative defaults resolve under the app root.
        Assert.Equal(Path.Combine(tmp.Path, "workspace"), settings.WorkspaceRoot);
        Assert.Equal(Path.Combine(tmp.Path, "output"), settings.OutputRoot);
        Assert.Equal(Path.Combine(tmp.Path, "reports"), settings.ReportRoot);
        Assert.Equal(Path.Combine(tmp.Path, "logs"), settings.LogRoot);
    }

    [Fact]
    public void AbsoluteRoot_IsKeptAsAnOverride()
    {
        using var tmp = new TempDir();
        var custom = tmp.Dir("elsewhere");
        // Write a settings.json (in the config dir the app reads) with an absolute workspace override.
        var paths = new AppPaths(tmp.Path);
        Directory.CreateDirectory(paths.ConfigDir);
        File.WriteAllText(paths.SettingsFile,
            "{ \"WorkspaceRoot\": " + System.Text.Json.JsonSerializer.Serialize(custom) + " }");

        var provider = new ServiceCollection().AddWinFeBuilderCore(paths).BuildServiceProvider();
        var settings = provider.GetRequiredService<ISettingsService>().Settings;

        Assert.Equal(custom, settings.WorkspaceRoot);                       // absolute kept
        Assert.Equal(Path.Combine(tmp.Path, "output"), settings.OutputRoot); // others still portable
    }
}
