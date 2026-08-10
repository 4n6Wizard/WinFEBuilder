using System.Text.Json;
using WinFEBuilder.Core.Models;
using WinFEBuilder.Core.Services;
using Xunit;

namespace WinFEBuilder.Tests;

/// <summary>
/// Covers the two things that decide whether a modern .NET tool works on WinFE: the destination is
/// inside the image, and the runtime major version matches what the tool asks for.
/// </summary>
public class ImageContentTests
{
    // ---------------------------------------------------------------- destination safety
    [Theory]
    [InlineData(@"Program Files\dotnet")]
    [InlineData(@"Program Files\AIMTools\AIM-Remote_x64")]
    [InlineData(@"Windows\System32\AIM-Driver")]
    [InlineData("tools")]
    public void SafeDestinations_AreAccepted(string dest)
    {
        Assert.True(ImageContentItem.IsSafeDestination(dest, out var why), why);
    }

    [Theory]
    [InlineData(@"C:\Windows\System32")]          // absolute — would hit the host, not the image
    [InlineData(@"\Windows\System32")]            // rooted
    [InlineData(@"..\..\Windows")]                // traversal out of the mount
    [InlineData(@"Program Files\..\..\Windows")]  // traversal buried mid-path
    [InlineData("")]
    [InlineData("   ")]
    public void UnsafeDestinations_AreRejected(string dest)
    {
        Assert.False(ImageContentItem.IsSafeDestination(dest, out var why));
        Assert.False(string.IsNullOrWhiteSpace(why));
    }

    // ---------------------------------------------------------------- runtime version matching
    [Fact]
    public void ReadRequiredFrameworkVersion_ReadsASingleFrameworkReference()
    {
        using var tmp = new TempDir();
        var dir = tmp.Dir("app");
        File.WriteAllText(Path.Combine(dir, "aim_remote.runtimeconfig.json"), """
        {
          "runtimeOptions": {
            "tfm": "net9.0",
            "framework": { "name": "Microsoft.NETCore.App", "version": "9.0.0" }
          }
        }
        """);

        Assert.Equal("9.0.0", DotNetRuntimeLocator.ReadRequiredFrameworkVersion(dir));
    }

    [Fact]
    public void ReadRequiredFrameworkVersion_PrefersNetCoreAppFromMultipleFrameworks()
    {
        using var tmp = new TempDir();
        var dir = tmp.Dir("app");
        File.WriteAllText(Path.Combine(dir, "tool.runtimeconfig.json"), """
        {
          "runtimeOptions": {
            "frameworks": [
              { "name": "Microsoft.WindowsDesktop.App", "version": "8.0.0" },
              { "name": "Microsoft.NETCore.App", "version": "10.0.0" }
            ]
          }
        }
        """);

        Assert.Equal("10.0.0", DotNetRuntimeLocator.ReadRequiredFrameworkVersion(dir));
    }

    [Fact]
    public void ReadRequiredFrameworkVersion_ReturnsNullWhenNotAModernDotNetApp()
    {
        using var tmp = new TempDir();
        var dir = tmp.Dir("native");
        File.WriteAllText(Path.Combine(dir, "tool.exe"), "not a real exe");

        // A native or .NET Framework tool has no runtimeconfig.json — it must not be treated as
        // needing a modern runtime, or we would refuse to copy a perfectly good tool.
        Assert.Null(DotNetRuntimeLocator.ReadRequiredFrameworkVersion(dir));
    }

    [Fact]
    public void ReadRequiredFrameworkVersion_SurvivesMalformedJson()
    {
        using var tmp = new TempDir();
        var dir = tmp.Dir("broken");
        File.WriteAllText(Path.Combine(dir, "tool.runtimeconfig.json"), "{ this is not json");

        Assert.Null(DotNetRuntimeLocator.ReadRequiredFrameworkVersion(dir));
    }

    // ---------------------------------------------------------------- desktop runtime decision
    [Fact]
    public void RequiresDesktopRuntime_FalseForAConsoleTool()
    {
        using var tmp = new TempDir();
        var dir = tmp.Dir("console");
        File.WriteAllText(Path.Combine(dir, "aim_remote.runtimeconfig.json"), """
        {
          "runtimeOptions": {
            "framework": { "name": "Microsoft.NETCore.App", "version": "9.0.0" }
          }
        }
        """);

        // aim_remote/aim_cli are console tools. Including the Desktop Runtime anyway would add ~75 MB
        // to boot.wim, which WinPE loads into RAM at every boot.
        Assert.False(DotNetRuntimeLocator.RequiresDesktopRuntime(dir));
    }

    [Fact]
    public void RequiresDesktopRuntime_TrueWhenTheToolDeclaresIt()
    {
        using var tmp = new TempDir();
        var dir = tmp.Dir("gui");
        File.WriteAllText(Path.Combine(dir, "tool.runtimeconfig.json"), """
        {
          "runtimeOptions": {
            "framework": { "name": "Microsoft.WindowsDesktop.App", "version": "9.0.0" }
          }
        }
        """);

        Assert.True(DotNetRuntimeLocator.RequiresDesktopRuntime(dir));
        // The base runtime version still governs which shared framework gets installed.
        Assert.Equal("9.0.0", DotNetRuntimeLocator.ReadRequiredFrameworkVersion(dir));
    }

    [Fact]
    public void ReadRequiredFrameworks_ReturnsEveryDeclaredFramework()
    {
        using var tmp = new TempDir();
        var dir = tmp.Dir("both");
        File.WriteAllText(Path.Combine(dir, "tool.runtimeconfig.json"), """
        {
          "runtimeOptions": {
            "frameworks": [
              { "name": "Microsoft.NETCore.App", "version": "10.0.0" },
              { "name": "Microsoft.WindowsDesktop.App", "version": "10.0.0" }
            ]
          }
        }
        """);

        var frameworks = DotNetRuntimeLocator.ReadRequiredFrameworks(dir);

        Assert.Equal(2, frameworks.Count);
        Assert.True(DotNetRuntimeLocator.RequiresDesktopRuntime(dir));
        Assert.Equal("10.0.0", DotNetRuntimeLocator.ReadRequiredFrameworkVersion(dir));
    }

    [Fact]
    public void RequiresDesktopRuntime_FalseForANonDotNetFolder()
    {
        using var tmp = new TempDir();
        var dir = tmp.Dir("native");
        File.WriteAllText(Path.Combine(dir, "tool.exe"), "stub");

        Assert.False(DotNetRuntimeLocator.RequiresDesktopRuntime(dir));
    }

    [Fact]
    public void SelectMatching_PicksNewestPatchOfTheSameMajor()
    {
        using var tmp = new TempDir();
        var root = FakeDotnetRoot(tmp, "9.0.5", "9.0.18", "10.0.10");

        var picked = DotNetRuntimeLocator.SelectMatching("9.0.0", dotnetRoot: root);

        Assert.NotNull(picked);
        Assert.Equal("9.0.18", picked!.Version);
    }

    [Fact]
    public void SelectMatching_RefusesADifferentMajor()
    {
        using var tmp = new TempDir();
        var root = FakeDotnetRoot(tmp, "9.0.18");

        // AIM 3.12.344 asks for net10.0. Substituting .NET 9 is exactly the mistake that produces
        // "You must install or update .NET to run this application" at boot, so it must return null
        // rather than the closest thing available.
        Assert.Null(DotNetRuntimeLocator.SelectMatching("10.0.0", dotnetRoot: root));
    }

    [Fact]
    public void SelectMatching_ReturnsNullForUnparseableRequirement()
    {
        using var tmp = new TempDir();
        var root = FakeDotnetRoot(tmp, "9.0.18");

        Assert.Null(DotNetRuntimeLocator.SelectMatching(null, dotnetRoot: root));
        Assert.Null(DotNetRuntimeLocator.SelectMatching("unknown", dotnetRoot: root));
    }

    // ---------------------------------------------------------------- runtime layout
    [Fact]
    public void BuildRuntimeLayout_ProducesTheHostAndSharedFrameworkPaths()
    {
        using var tmp = new TempDir();
        var root = FakeDotnetRoot(tmp, "9.0.18");
        var runtime = DotNetRuntimeLocator.SelectMatching("9.0.0", dotnetRoot: root)!;

        var layout = DotNetRuntimeLocator.BuildRuntimeLayout(runtime, dotnetRoot: root);
        var destinations = layout.Select(l => l.DestinationRelative).ToList();

        // hostfxr makes the folder act as a .NET root; the shared framework is the runtime itself.
        Assert.Contains(@"Program Files\dotnet\host\fxr\9.0.18", destinations);
        Assert.Contains(@"Program Files\dotnet\shared\Microsoft.NETCore.App\9.0.18", destinations);
        Assert.All(destinations, d => Assert.True(ImageContentItem.IsSafeDestination(d, out _)));
    }

    [Fact]
    public void BuildRuntimeLayout_OmitsDesktopRuntimeUnlessAsked()
    {
        using var tmp = new TempDir();
        var root = FakeDotnetRoot(tmp, "9.0.18", desktopVersions: new[] { "9.0.18" });
        var runtime = DotNetRuntimeLocator.SelectMatching("9.0.0", dotnetRoot: root)!;

        var without = DotNetRuntimeLocator.BuildRuntimeLayout(runtime, dotnetRoot: root, includeDesktopRuntime: false);
        var with = DotNetRuntimeLocator.BuildRuntimeLayout(runtime, dotnetRoot: root, includeDesktopRuntime: true);

        Assert.DoesNotContain(without, l => l.DestinationRelative.Contains("WindowsDesktop"));
        Assert.Contains(with, l => l.DestinationRelative.Contains("WindowsDesktop"));
    }

    [Fact]
    public void Enumerate_IgnoresNonVersionFolders()
    {
        using var tmp = new TempDir();
        var root = FakeDotnetRoot(tmp, "9.0.18");
        Directory.CreateDirectory(Path.Combine(root, "shared", "Microsoft.NETCore.App", "notaversion"));

        var all = DotNetRuntimeLocator.Enumerate(root);

        Assert.Single(all);
        Assert.Equal("9.0.18", all[0].Version);
    }

    /// <summary>Builds a minimal dotnet installation layout for the locator to walk.</summary>
    private static string FakeDotnetRoot(TempDir tmp, params string[] netCoreVersions)
        => FakeDotnetRoot(tmp, netCoreVersions, Array.Empty<string>());

    private static string FakeDotnetRoot(TempDir tmp, string[] netCoreVersions, string[] desktopVersions)
    {
        var root = tmp.Dir("dotnet");
        File.WriteAllText(Path.Combine(root, "dotnet.exe"), "stub");

        foreach (var v in netCoreVersions)
        {
            Directory.CreateDirectory(Path.Combine(root, "shared", DotNetRuntimeLocator.NetCoreApp, v));
            Directory.CreateDirectory(Path.Combine(root, "host", "fxr", v));
            File.WriteAllText(Path.Combine(root, "host", "fxr", v, "hostfxr.dll"), "stub");
        }
        foreach (var v in desktopVersions)
            Directory.CreateDirectory(Path.Combine(root, "shared", DotNetRuntimeLocator.WindowsDesktopApp, v));

        return root;
    }

    private static string FakeDotnetRoot(TempDir tmp, string netCoreVersion, string[] desktopVersions)
        => FakeDotnetRoot(tmp, new[] { netCoreVersion }, desktopVersions);
}
