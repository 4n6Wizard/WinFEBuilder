using WinFEBuilder.Core.Logging;
using WinFEBuilder.Core.Services;
using Xunit;

namespace WinFEBuilder.Tests;

public class WallpaperServiceTests
{
    private static WallpaperService Build(TempDir tmp) => new(new LogService(tmp.Dir("logs")));

    [Fact]
    public void SetWallpaper_CopiesToX64AndX86AsWallpaperJpg()
    {
        using var tmp = new TempDir();
        var fw = tmp.Dir("framework");
        tmp.Dir(@"framework\x64");
        tmp.Dir(@"framework\x86");
        var img = tmp.File("myimage.jpg", "fake-jpeg-bytes");

        var svc = Build(tmp);
        var r = svc.SetWallpaper(img, fw);

        Assert.True(r.Success);
        Assert.True(File.Exists(Path.Combine(fw, "x64", "wallpaper.jpg")));
        Assert.True(File.Exists(Path.Combine(fw, "x86", "wallpaper.jpg")));
        Assert.Equal(Path.Combine(fw, "x64", "wallpaper.jpg"), svc.CurrentWallpaper(fw));
    }

    [Fact]
    public void SetWallpaper_CreatesMissingStagingFolders()
    {
        using var tmp = new TempDir();
        var fw = tmp.Dir("framework"); // no x64/x86 yet
        var img = tmp.File("pic.jpg", "bytes");

        var svc = Build(tmp);
        var r = svc.SetWallpaper(img, fw);

        Assert.True(r.Success);
        Assert.True(File.Exists(Path.Combine(fw, "x64", "wallpaper.jpg")));
    }

    [Fact]
    public void SetWallpaper_FailsWhenImageMissing()
    {
        using var tmp = new TempDir();
        var fw = tmp.Dir("framework");
        var svc = Build(tmp);
        var r = svc.SetWallpaper(Path.Combine(tmp.Path, "nope.jpg"), fw);
        Assert.False(r.Success);
    }

    [Fact]
    public void TargetPaths_ArePredictable()
    {
        using var tmp = new TempDir();
        var svc = Build(tmp);
        var targets = svc.TargetPaths(@"C:\fw");
        Assert.Contains(@"C:\fw\x64\wallpaper.jpg", targets);
        Assert.Contains(@"C:\fw\x86\wallpaper.jpg", targets);
    }
}
