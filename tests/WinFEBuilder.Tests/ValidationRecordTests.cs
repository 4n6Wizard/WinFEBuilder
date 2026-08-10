using WinFEBuilder.Core.Models;
using Xunit;

namespace WinFEBuilder.Tests;

public class ValidationRecordTests
{
    [Fact]
    public void WriteProtectionVerified_OnlyWhenBothChecksPass()
    {
        var r = new ValidationRecord();
        Assert.False(r.WriteProtectionVerified); // default NotTested

        r.InternalSourceOfflineOrReadOnly = ManualCheck.Pass;
        Assert.False(r.WriteProtectionVerified); // still missing the hash check

        r.TestSourceHashMatchedBeforeAfter = ManualCheck.Pass;
        Assert.True(r.WriteProtectionVerified);
    }

    [Fact]
    public void BootVerified_WhenEitherBootCheckPasses()
    {
        var r = new ValidationRecord();
        Assert.False(r.BootVerified);

        r.BootedLegacyBios = ManualCheck.Pass;
        Assert.True(r.BootVerified);
    }
}
