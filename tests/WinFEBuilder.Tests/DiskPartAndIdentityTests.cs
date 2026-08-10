using WinFEBuilder.Core.Models;
using WinFEBuilder.Core.Validation;
using Xunit;

namespace WinFEBuilder.Tests;

public class DiskPartScriptBuilderTests
{
    [Fact]
    public void Build_ProducesStandardLayout()
    {
        var script = DiskPartScriptBuilder.Build(3, "WINFE");
        var expected = string.Join(Environment.NewLine, new[]
        {
            "rescan",
            "select disk 3",
            "clean",
            "convert mbr",
            "create partition primary",
            "select partition 1",
            "format fs=fat32 quick label=WINFE",
            "active",
            "assign",
            "exit"
        }) + Environment.NewLine;
        Assert.Equal(expected, script);
    }

    [Fact]
    public void Build_UsesVerifiedDiskNumber()
        => Assert.Contains("select disk 7", DiskPartScriptBuilder.Build(7));

    [Theory]
    [InlineData("My Label!", "MYLABEL")]
    [InlineData("", "WINFE")]
    [InlineData("ThisLabelIsWayTooLong", "THISLABELIS")] // 11 char cap
    public void SanitizeLabel_CleansAndCaps(string input, string expected)
        => Assert.Equal(expected, DiskPartScriptBuilder.SanitizeLabel(input));

    [Fact]
    public void Build_NegativeDisk_Throws()
        => Assert.Throws<ArgumentOutOfRangeException>(() => DiskPartScriptBuilder.Build(-1));

    // --- Disk 11 regression: "There is no volume selected." -------------------------------------
    [Fact]
    public void Build_ExplicitlySelectsPartitionBeforeFormatAndActive()
    {
        var lines = DiskPartScriptBuilder.Build(11, "WINFE")
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

        int createIdx = Array.IndexOf(lines, "create partition primary");
        int selectPartIdx = Array.IndexOf(lines, "select partition 1");
        int formatIdx = Array.FindIndex(lines, l => l.StartsWith("format "));
        int activeIdx = Array.IndexOf(lines, "active");

        Assert.True(selectPartIdx >= 0, "script must explicitly select the partition");
        Assert.True(createIdx < selectPartIdx, "select must come after create");
        Assert.True(selectPartIdx < formatIdx, "partition must be selected before format");
        Assert.True(selectPartIdx < activeIdx, "partition must be selected before active");
    }

    [Fact]
    public void Build_IncludesRescan()
        => Assert.Contains("rescan", DiskPartScriptBuilder.Build(11));
}

public class DiskIdentityTests
{
    private static DiskInfo Disk(int n = 3, string serial = "SN1", long size = 1000) => new()
    {
        Number = n, Model = "M", SerialNumber = serial, UniqueId = "U", BusType = "USB", SizeBytes = size
    };

    [Fact]
    public void Matches_SameIdentity_True()
        => Assert.True(DiskIdentity.Matches(Disk(), Disk()));

    [Fact]
    public void Matches_DifferentSerial_False()
    {
        Assert.False(DiskIdentity.Matches(Disk(serial: "SN1"), Disk(serial: "SN2")));
        Assert.Contains(DiskIdentity.Differences(Disk(serial: "SN1"), Disk(serial: "SN2")),
            d => d.StartsWith("Serial"));
    }

    [Fact]
    public void Matches_DifferentSize_False()
        => Assert.False(DiskIdentity.Matches(Disk(size: 1000), Disk(size: 2000)));

    [Fact]
    public void Matches_NumberReassigned_False()
        => Assert.False(DiskIdentity.Matches(Disk(n: 3), Disk(n: 4)));
}
