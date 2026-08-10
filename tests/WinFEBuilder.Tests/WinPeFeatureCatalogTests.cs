using WinFEBuilder.Core.Validation;
using Xunit;

namespace WinFEBuilder.Tests;

public class WinPeFeatureCatalogTests
{
    [Fact]
    public void Catalog_ContainsNetFx()
    {
        var f = WinPeFeatureCatalog.ByName("WinPE-NetFx");
        Assert.NotNull(f);
        Assert.Equal("WinPE-NetFx.cab", f!.Cab);
    }

    [Fact]
    public void CabPaths_ProducesBaseAndLangCab_InOrder()
    {
        var paths = WinPeFeatureCatalog.CabPaths(@"C:\OCs", "en-us", new[] { "WinPE-NetFx" });
        Assert.Equal(2, paths.Count);
        Assert.Equal(@"C:\OCs\WinPE-NetFx.cab", paths[0]);
        Assert.Equal(@"C:\OCs\en-us\WinPE-NetFx_en-us.cab", paths[1]);
    }

    [Fact]
    public void CabPaths_OrdersByDependencyOrder()
    {
        // PowerShell (order 4) must come after NetFx (order 2) regardless of input order.
        var paths = WinPeFeatureCatalog.CabPaths(@"C:\OCs", "en-us", new[] { "WinPE-PowerShell", "WinPE-NetFx" });
        var netfxIndex = paths.FindIndex(p => p.EndsWith("WinPE-NetFx.cab"));
        var psIndex = paths.FindIndex(p => p.EndsWith("WinPE-PowerShell.cab"));
        Assert.True(netfxIndex >= 0 && psIndex >= 0 && netfxIndex < psIndex);
    }

    [Fact]
    public void CabPaths_IgnoresUnknownFeatures()
        => Assert.Empty(WinPeFeatureCatalog.CabPaths(@"C:\OCs", "en-us", new[] { "Nope" }));
}
