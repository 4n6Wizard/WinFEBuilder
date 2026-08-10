using WinFEBuilder.Core.Validation;
using Xunit;

namespace WinFEBuilder.Tests;

/// <summary>
/// A driver whose device entries live only in a section decorated for a newer Windows installs into a
/// WinPE 1809 image perfectly — DISM reports success, the package is signed — and then never binds.
/// The operator just sees "no network adapter". These tests pin the detection of that case, modelled
/// on the two real Realtek INFs that produced it.
/// </summary>
public class InfOsApplicabilityTests
{
    // Shape of the inbox Realtek driver: most devices usable everywhere, but the newer silicon
    // revision only listed for Windows 11 (build 22000+). This is the one that silently failed.
    private const string Windows11OnlyForNewDevice = """
        [Version]
        Signature = "$Windows NT$"
        Class     = Net
        Provider  = %Provider%

        [Manufacturer]
        %Provider% = Realtek, NTx86, NTamd64, NTamd64.10.0...22000

        [Realtek.NTamd64]
        %RTL8168.DeviceDesc% = RTL8168.ndi, PCI\VEN_10EC&DEV_8168&REV_01
        %RTL8125.DeviceDesc% = RTL8125.ndi, PCI\VEN_10EC&DEV_8125&REV_01

        [Realtek.NTamd64.10.0...22000]
        %RTL8125AG.DeviceDesc% = RTL8125BG.ndi, PCI\VEN_10EC&DEV_8125&REV_05
        %RTL8126.DeviceDesc%   = RTL8126.ndi,   PCI\VEN_10EC&DEV_8126&REV_01
        """;

    // Shape of the Windows 10 package that worked: decorated only with the OS major/minor, no build
    // floor, so every device is available on any Windows 10 build including WinPE 1809.
    private const string Windows10AnyBuild = """
        [Version]
        Signature = "$Windows NT$"
        Class     = Net
        Provider  = %Realtek%

        [Manufacturer]
        %Realtek% = Realtek, NTx86.10.0, NTamd64.10.0

        [Realtek.NTamd64.10.0]
        %RTL8125AG.DeviceDesc% = RTL8125BG.ndi, PCI\VEN_10EC&DEV_8125&REV_05
        %RTL8125.DeviceDesc%   = RTL8125.ndi,   PCI\VEN_10EC&DEV_8125&REV_01
        """;

    [Fact]
    public void DetectsDevicesRestrictedToANewerWindows()
    {
        var s = InfOsApplicability.Analyze(Windows11OnlyForNewDevice);

        Assert.Equal(2, s.UsableDeviceCount);        // the two REV_01 entries
        Assert.Equal(2, s.RestrictedDeviceCount);    // REV_05 and the 8126
        Assert.Equal(22000, s.LowestRestrictedBuild);
        Assert.Contains("22000", s.Summary);
    }

    [Fact]
    public void TreatsOsMajorMinorDecorationAsUnrestricted()
    {
        // "NTamd64.10.0" names Windows 10 with no build floor — usable on 17763.
        var s = InfOsApplicability.Analyze(Windows10AnyBuild);

        Assert.Equal(2, s.UsableDeviceCount);
        Assert.False(s.HasRestrictedDevices);
        Assert.Null(s.LowestRestrictedBuild);
        Assert.Contains("no build restriction", s.Summary);
    }

    [Fact]
    public void SupportsHardwareId_DistinguishesTheTwoRealDrivers()
    {
        const string myAdapter = @"PCI\VEN_10EC&DEV_8125&REV_05";

        // The exact pairing observed: the inbox driver refuses this device on WinPE 1809, the
        // Windows 10 package accepts it.
        Assert.False(InfOsApplicability.SupportsHardwareId(Windows11OnlyForNewDevice, myAdapter));
        Assert.True(InfOsApplicability.SupportsHardwareId(Windows10AnyBuild, myAdapter));
    }

    [Fact]
    public void SupportsHardwareId_MatchesOnAPrefix()
    {
        // Device Manager reports SUBSYS/REV detail the INF may not carry, and vice versa.
        Assert.True(InfOsApplicability.SupportsHardwareId(Windows10AnyBuild, @"PCI\VEN_10EC&DEV_8125"));
        Assert.False(InfOsApplicability.SupportsHardwareId(Windows10AnyBuild, @"PCI\VEN_8086&DEV_15F3"));
    }

    [Fact]
    public void RestrictedDeviceBecomesUsableOnANewerTargetImage()
    {
        // Same INF, Windows 11 target: nothing is restricted any more. Confirms the verdict is about
        // the target image, not the driver in isolation.
        var onWinPe = InfOsApplicability.Analyze(Windows11OnlyForNewDevice, targetBuild: 17763);
        var onWin11 = InfOsApplicability.Analyze(Windows11OnlyForNewDevice, targetBuild: 22631);

        Assert.True(onWinPe.HasRestrictedDevices);
        Assert.False(onWin11.HasRestrictedDevices);
        Assert.Equal(4, onWin11.UsableDeviceCount);
    }

    [Fact]
    public void UndecoratedManufacturerEntryAppliesEverywhere()
    {
        const string inf = """
            [Manufacturer]
            %Vendor% = VendorDevices

            [VendorDevices]
            %Dev% = Install, PCI\VEN_1234&DEV_5678
            """;

        var s = InfOsApplicability.Analyze(inf);

        Assert.Equal(1, s.UsableDeviceCount);
        Assert.False(s.HasRestrictedDevices);
    }

    [Fact]
    public void IgnoresOtherArchitectures()
    {
        const string inf = """
            [Manufacturer]
            %V% = V, NTx86, NTamd64

            [V.NTx86]
            %A% = Install, PCI\VEN_1&DEV_1
            %B% = Install, PCI\VEN_1&DEV_2

            [V.NTamd64]
            %A% = Install, PCI\VEN_1&DEV_1
            """;

        var amd64 = InfOsApplicability.Analyze(inf, targetArch: "amd64");
        var x86 = InfOsApplicability.Analyze(inf, targetArch: "x86");

        Assert.Equal(1, amd64.UsableDeviceCount);
        Assert.Equal(2, x86.UsableDeviceCount);
    }

    [Fact]
    public void CommentsAndBlankLinesDoNotConfuseIt()
    {
        const string inf = """
            [Manufacturer]
            ; a comment
            %V% = V, NTamd64.10.0...22000

            [V.NTamd64.10.0...22000]

            %A% = Install, PCI\VEN_1&DEV_1   ; trailing comment
            """;

        var s = InfOsApplicability.Analyze(inf);

        Assert.Equal(0, s.UsableDeviceCount);
        Assert.Equal(1, s.RestrictedDeviceCount);
        Assert.Equal(22000, s.LowestRestrictedBuild);
    }

    [Fact]
    public void EmptyOrGarbageInputIsHarmless()
    {
        Assert.Empty(InfOsApplicability.Analyze("").Sections);
        Assert.Empty(InfOsApplicability.Analyze("not an inf at all").Sections);
        Assert.Equal("OS targets not declared", InfOsApplicability.Analyze("").Summary);
    }
}
