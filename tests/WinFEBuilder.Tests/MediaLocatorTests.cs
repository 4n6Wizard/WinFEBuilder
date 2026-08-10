using WinFEBuilder.Core.Validation;
using Xunit;

namespace WinFEBuilder.Tests;

public class MediaLocatorTests
{
    [Fact]
    public void FindBootWim_LocatesSourcesBootWim()
    {
        var files = new[]
        {
            @"C:\ws\framework\readme.txt",
            @"C:\ws\framework\USB\Boot\bootmgr",
            @"C:\ws\framework\USB\sources\boot.wim",
        };
        var found = MediaLocator.FindBootWim(files);
        Assert.Equal(@"C:\ws\framework\USB\sources\boot.wim", found);
    }

    [Fact]
    public void FindBootWim_ReturnsNullWhenAbsent()
        => Assert.Null(MediaLocator.FindBootWim(new[] { @"C:\ws\a.txt", @"C:\ws\b.wim" }));

    [Fact]
    public void MediaRootFromBootWim_ReturnsRootAboveSources()
    {
        var root = MediaLocator.MediaRootFromBootWim(@"C:\ws\framework\USB\sources\boot.wim");
        Assert.Equal(@"C:\ws\framework\USB", root);
    }

    [Fact]
    public void SelectNewestIso_PicksNewestNonEmpty()
    {
        var older = new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero);
        var newer = new DateTimeOffset(2026, 7, 20, 0, 0, 0, TimeSpan.Zero);
        var candidates = new[]
        {
            (@"C:\out\old.iso", 1000L, older),
            (@"C:\out\empty.iso", 0L, newer),
            (@"C:\out\new.iso", 2000L, newer),
        };
        Assert.Equal(@"C:\out\new.iso", MediaLocator.SelectNewestIso(candidates));
    }

    [Fact]
    public void FindMediaRoot_FindsCombinedMultiArchRoot()
    {
        // IntelWinFE-style: the deployable root has Boot/EFI/Sources; boot.wim is nested per-arch.
        var dirs = new[]
        {
            @"C:\ws\framework\USB\x86-x64",
            @"C:\ws\framework\USB\x86-x64\Boot",
            @"C:\ws\framework\USB\x86-x64\EFI",
            @"C:\ws\framework\USB\x86-x64\Sources",
            @"C:\ws\framework\USB\x86-x64\x64\sources",
            @"C:\ws\framework\USB\x86-x64\x86\sources",
        };
        Assert.Equal(@"C:\ws\framework\USB\x86-x64", MediaLocator.FindMediaRoot(dirs));
    }

    [Fact]
    public void FindMediaRoot_ReturnsNullWhenNoSkeleton()
        => Assert.Null(MediaLocator.FindMediaRoot(new[] { @"C:\ws\a", @"C:\ws\a\Boot" }));

    [Theory]
    [InlineData(new[] { "Boot", "EFI", "Sources" }, true)]
    [InlineData(new[] { "Boot", "Sources" }, false)]
    public void HasBootableSkeleton_ChecksTriad(string[] children, bool expected)
        => Assert.Equal(expected, MediaLocator.HasBootableSkeleton(children));

    [Fact]
    public void SelectNewestIso_IgnoresZeroByteFiles()
    {
        var t = new DateTimeOffset(2026, 7, 20, 0, 0, 0, TimeSpan.Zero);
        var candidates = new[] { (@"C:\out\empty.iso", 0L, t) };
        Assert.Null(MediaLocator.SelectNewestIso(candidates));
    }
}
