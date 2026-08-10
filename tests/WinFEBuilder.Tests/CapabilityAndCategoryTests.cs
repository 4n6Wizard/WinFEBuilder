using WinFEBuilder.Core.Models;
using WinFEBuilder.Core.Validation;
using Xunit;

namespace WinFEBuilder.Tests;

public class DriverCategorizerTests
{
    private static DriverInfo D(string cls, string name = "d.inf", string provider = "P") =>
        new() { InfPath = @"C:\d\" + name, InfName = name, DriverClass = cls, Provider = provider };

    [Theory]
    [InlineData("Net", DriverCategorizer.Network)]
    [InlineData("Display", DriverCategorizer.Display)]
    [InlineData("USB", DriverCategorizer.Usb)]
    [InlineData("SCSIAdapter", DriverCategorizer.Storage)]
    [InlineData("HDC", DriverCategorizer.Storage)]
    [InlineData("Keyboard", DriverCategorizer.Other)]
    public void Categorize_ByClass(string cls, string expected)
        => Assert.Equal(expected, DriverCategorizer.Categorize(D(cls)));

    [Fact]
    public void Categorize_NvmeByName_WinsOverClass()
        => Assert.Equal(DriverCategorizer.Nvme, DriverCategorizer.Categorize(D("SCSIAdapter", "stornvme.inf")));
}

public class WindowsCapabilityCatalogTests
{
    [Fact]
    public void DotNet_ResolvesToNetFxWithWmiDependency()
    {
        var features = WindowsCapabilityCatalog.ResolveFeatures(new[] { "DotNet" });
        Assert.Contains("WinPE-WMI", features);   // dependency auto-included
        Assert.Contains("WinPE-NetFx", features);
        // WMI must come before NetFx (install order).
        Assert.True(features.ToList().IndexOf("WinPE-WMI") < features.ToList().IndexOf("WinPE-NetFx"));
    }

    [Fact]
    public void MultipleCapabilities_AreDedupedAndOrdered()
    {
        var features = WindowsCapabilityCatalog.ResolveFeatures(new[] { "PowerShell", "DotNet" });
        Assert.Equal(features.Distinct().Count(), features.Count);       // no dupes
        var list = features.ToList();
        Assert.True(list.IndexOf("WinPE-NetFx") < list.IndexOf("WinPE-PowerShell"));
    }

    [Fact]
    public void DisplayNames_NeverExposePackageNames()
    {
        var names = WindowsCapabilityCatalog.DisplayNames(new[] { "DotNet", "PowerShell", "StorageManagement" });
        Assert.All(names, n => Assert.DoesNotContain("WinPE-", n));
        Assert.Contains(".NET Framework", names);
    }

    [Fact]
    public void UnknownKeys_Ignored()
        => Assert.Empty(WindowsCapabilityCatalog.ResolveFeatures(new[] { "Nope" }));
}

public class ToolComponentResolverTests
{
    [Fact]
    public void DotNet_IsAlwaysBaseline_EvenWithNoTools()
        => Assert.Contains("DotNet", ToolComponentResolver.Resolve(Array.Empty<string>()));

    [Fact]
    public void Ftk_ResolvesToDotNet_ViaBaseline()
    {
        var caps = ToolComponentResolver.Resolve(new[] { "FTK Imager" });
        Assert.Contains("DotNet", caps);
        // And .NET resolves to the correct WinPE features internally (no package names shown to users).
        var features = WindowsCapabilityCatalog.ResolveFeatures(caps);
        Assert.Contains("WinPE-NetFx", features);
        Assert.Contains("WinPE-WMI", features);
    }

    [Fact]
    public void PowerShellTool_AddsPowerShellCapability()
    {
        var caps = ToolComponentResolver.Resolve(new[] { "MyPowerShellKit" });
        Assert.Contains("PowerShell", caps);
        Assert.Contains("DotNet", caps);
    }
}
