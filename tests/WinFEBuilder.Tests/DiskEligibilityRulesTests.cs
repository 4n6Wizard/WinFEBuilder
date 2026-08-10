using WinFEBuilder.Core.Models;
using WinFEBuilder.Core.Validation;
using Xunit;

namespace WinFEBuilder.Tests;

public class DiskEligibilityRulesTests
{
    private static DiskInfo Usb(int n = 3) => new()
    {
        Number = n,
        Model = "Generic USB",
        SerialNumber = "SN123",
        UniqueId = "UID-123",
        BusType = "USB",
        SizeBytes = 32L * 1024 * 1024 * 1024,
        IsRemovable = true,
        DriveLetters = { "E:" }
    };

    private static ProtectedContext Ctx()
    {
        var c = new ProtectedContext();
        c.Protect("C:", "Windows system volume");
        return c;
    }

    [Fact]
    public void EligibleUsb_IsAllowed()
    {
        var e = DiskEligibilityRules.Evaluate(Usb(), Ctx());
        Assert.True(e.CanTarget);
        Assert.Empty(e.BlockReasons);
    }

    [Fact]
    public void UnverifiableDisk_IsBlocked_FailClosed()
    {
        // A real disk whose partitions could not be enumerated must be refused (we can't prove it's safe).
        var d = new DiskInfo
        {
            Number = 4, Model = "Generic USB", SerialNumber = "SN9", UniqueId = "UID-9",
            BusType = "USB", SizeBytes = 16L * 1024 * 1024 * 1024, IsRemovable = true,
            PartitionInfoReliable = false
        };
        var e = DiskEligibilityRules.Evaluate(d, Ctx());
        Assert.False(e.CanTarget);
        Assert.Contains(e.BlockReasons, r => r.Contains("could not be verified", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void SimulatedDisk_UnverifiableStillEligible()
    {
        // Simulated disks never touch hardware, so the reliability gate does not apply.
        var d = new DiskInfo { Number = 99, IsSimulated = true, PartitionInfoReliable = false };
        Assert.True(DiskEligibilityRules.Evaluate(d, Ctx()).CanTarget);
    }

    [Fact]
    public void SystemDisk_IsBlocked()
    {
        var d = new DiskInfo { Number = 0, Model = "SSD", SerialNumber = "x", UniqueId = "u", BusType = "NVMe", SizeBytes = 500_000_000_000, IsRemovable = false, IsSystemDisk = true };
        var e = DiskEligibilityRules.Evaluate(d, Ctx(), allowNonRemovable: true);
        Assert.False(e.CanTarget);
        Assert.Contains(e.BlockReasons, r => r.Contains("system disk", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void BootDisk_IsBlocked()
    {
        var d = Usb();
        var boot = new DiskInfo { Number = d.Number, Model = d.Model, SerialNumber = d.SerialNumber, UniqueId = d.UniqueId, BusType = d.BusType, SizeBytes = d.SizeBytes, IsRemovable = true, IsBootDisk = true };
        var e = DiskEligibilityRules.Evaluate(boot, Ctx());
        Assert.False(e.CanTarget);
    }

    [Fact]
    public void DiskHostingProtectedVolume_IsBlocked()
    {
        var d = new DiskInfo { Number = 1, Model = "M", SerialNumber = "s", UniqueId = "u", BusType = "USB", SizeBytes = 1000, IsRemovable = true, DriveLetters = { "C:" } };
        var e = DiskEligibilityRules.Evaluate(d, Ctx());
        Assert.False(e.CanTarget);
        Assert.Contains(e.BlockReasons, r => r.Contains("C:"));
    }

    [Fact]
    public void NoUniqueId_IsBlocked()
    {
        var d = new DiskInfo { Number = 3, Model = "M", SerialNumber = "s", UniqueId = null, BusType = "USB", SizeBytes = 1000, IsRemovable = true };
        var e = DiskEligibilityRules.Evaluate(d, Ctx());
        Assert.False(e.CanTarget);
        Assert.Contains(e.BlockReasons, r => r.Contains("uniquely identifiable", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ZeroSize_IsBlocked()
    {
        var d = new DiskInfo { Number = 3, Model = "M", SerialNumber = "s", UniqueId = "u", BusType = "USB", SizeBytes = 0, IsRemovable = true };
        Assert.False(DiskEligibilityRules.Evaluate(d, Ctx()).CanTarget);
    }

    [Fact]
    public void ReadOnly_IsBlocked()
    {
        var d = new DiskInfo { Number = 3, Model = "M", SerialNumber = "s", UniqueId = "u", BusType = "USB", SizeBytes = 1000, IsRemovable = true, IsReadOnly = true };
        Assert.False(DiskEligibilityRules.Evaluate(d, Ctx()).CanTarget);
    }

    [Fact]
    public void NonRemovable_BlockedByDefault_AllowedWithAdvanced()
    {
        var d = new DiskInfo { Number = 4, Model = "Data HDD", SerialNumber = "s", UniqueId = "u", BusType = "SATA", SizeBytes = 2_000_000_000_000, IsRemovable = false, DriveLetters = { "D:" } };
        Assert.False(DiskEligibilityRules.Evaluate(d, Ctx(), allowNonRemovable: false).CanTarget);
        Assert.True(DiskEligibilityRules.Evaluate(d, Ctx(), allowNonRemovable: true).CanTarget);
    }

    [Fact]
    public void SimulatedDisk_IsAlwaysEligible()
    {
        var d = new DiskInfo { Number = 99, IsSimulated = true };
        Assert.True(DiskEligibilityRules.Evaluate(d, Ctx()).CanTarget);
    }
}
