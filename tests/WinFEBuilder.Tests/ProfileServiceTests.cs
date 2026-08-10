using WinFEBuilder.Core.Configuration;
using Xunit;

namespace WinFEBuilder.Tests;

public class ProfileServiceTests
{
    [Fact]
    public void SeedsDefaultProfiles_WhenFileMissing()
    {
        using var tmp = new TempDir();
        var svc = new ProfileService(Path.Combine(tmp.Path, "build-profiles.json"));
        var names = svc.List().Select(p => p.Name).ToList();
        Assert.Contains("Agency Standard", names);
        Assert.Contains("UEFI Only", names);
        Assert.Contains("Legacy BIOS", names);
    }

    [Fact]
    public void Save_Get_Delete_RoundTrips()
    {
        using var tmp = new TempDir();
        var file = Path.Combine(tmp.Path, "build-profiles.json");
        var svc = new ProfileService(file);

        svc.Save(new BuildProfile { Name = "Custom", OrganizationName = "Org", UsbLayout = "UEFI" });
        var got = svc.Get("Custom");
        Assert.NotNull(got);
        Assert.Equal("Org", got!.OrganizationName);

        // Overwrite by same name
        svc.Save(new BuildProfile { Name = "Custom", OrganizationName = "Org2" });
        Assert.Equal("Org2", svc.Get("Custom")!.OrganizationName);
        Assert.Single(svc.List(), p => p.Name == "Custom");

        svc.Delete("Custom");
        Assert.Null(svc.Get("Custom"));
    }

    [Fact]
    public void Save_RequiresName()
    {
        using var tmp = new TempDir();
        var svc = new ProfileService(Path.Combine(tmp.Path, "p.json"));
        Assert.Throws<ArgumentException>(() => svc.Save(new BuildProfile { Name = "" }));
    }

    [Fact]
    public void StaleAbsoluteProfilePaths_AreClearedOnLoad()
    {
        using var tmp = new TempDir();
        var file = Path.Combine(tmp.Path, "build-profiles.json");
        var svc = new ProfileService(file);
        // Absolute paths from "another machine" that do not exist here (under an uncreated temp subdir).
        var ghost = Path.Combine(tmp.Path, "ghost-from-other-pc");
        svc.Save(new BuildProfile
        {
            Name = "Imported",
            WorkspaceRoot = Path.Combine(ghost, "workspace"),
            OutputRoot = Path.Combine(ghost, "output"),
            FrameworkPath = Path.Combine(ghost, "IntelWinFE"),
            Wallpaper = Path.Combine(ghost, "wall.jpg")
        });

        var got = svc.Get("Imported")!;
        Assert.Null(got.WorkspaceRoot);
        Assert.Null(got.OutputRoot);
        Assert.Null(got.FrameworkPath);
        Assert.Null(got.Wallpaper);
    }

    [Fact]
    public void BuildProfile_HasNoDiskNumberField()
    {
        // Guard against accidentally persisting disk numbers (unstable/unsafe).
        var props = typeof(BuildProfile).GetProperties().Select(p => p.Name.ToLowerInvariant());
        Assert.DoesNotContain(props, n => n.Contains("disknumber") || n == "disk");
    }
}
