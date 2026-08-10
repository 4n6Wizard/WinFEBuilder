using System.Text.Json;
using WinFEBuilder.Core.Configuration;
using WinFEBuilder.Core.Logging;
using WinFEBuilder.Core.Models;
using WinFEBuilder.Core.Services;
using Xunit;

namespace WinFEBuilder.Tests;

public class WorkspaceAndManifestTests
{
    private static (WorkspaceService ws, SettingsService settings) Build(TempDir tmp)
    {
        var settingsFile = Path.Combine(tmp.Path, "settings.json");
        var settings = new SettingsService(settingsFile);
        settings.Settings.WorkspaceRoot = tmp.Dir("workspace");
        var log = new LogService(tmp.Dir("logs"));
        return (new WorkspaceService(settings, log), settings);
    }

    [Fact]
    public void BuildWorkspaceName_UsesTimestampFormat()
    {
        using var tmp = new TempDir();
        var (ws, _) = Build(tmp);
        var name = ws.BuildWorkspaceName(new DateTimeOffset(2026, 7, 20, 14, 30, 0, TimeSpan.Zero));
        Assert.Equal("Build_2026-07-20_143000", name);
    }

    [Fact]
    public void CreateTimestampedWorkspace_CreatesDirectory()
    {
        using var tmp = new TempDir();
        var (ws, _) = Build(tmp);
        var dir = ws.CreateTimestampedWorkspace(new DateTimeOffset(2026, 7, 20, 9, 5, 1, TimeSpan.Zero));
        Assert.True(Directory.Exists(dir));
        Assert.EndsWith("Build_2026-07-20_090501", dir);
    }

    [Fact]
    public void WriteManifest_RoundTripsJson()
    {
        using var tmp = new TempDir();
        var (ws, _) = Build(tmp);
        var dir = ws.CreateTimestampedWorkspace();

        var manifest = new WorkspaceManifest
        {
            OriginalSourcePath = @"C:\src\framework",
            WorkspacePath = dir,
            FileCount = 2,
            TotalBytes = 1234,
            Hashes =
            {
                new FileHashEntry { RelativePath = "a.bat", FullPath = @"C:\x\a.bat", SizeBytes = 10, Sha256 = "deadbeef" }
            }
        };

        var path = ws.WriteManifest(dir, manifest);
        Assert.True(File.Exists(path));

        var reloaded = JsonSerializer.Deserialize<WorkspaceManifest>(File.ReadAllText(path));
        Assert.NotNull(reloaded);
        Assert.Equal(2, reloaded!.FileCount);
        Assert.Equal(1234, reloaded.TotalBytes);
        Assert.Single(reloaded.Hashes);
        Assert.Equal("deadbeef", reloaded.Hashes[0].Sha256);
    }
}
