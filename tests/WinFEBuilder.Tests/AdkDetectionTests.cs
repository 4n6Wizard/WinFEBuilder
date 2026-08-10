using WinFEBuilder.Core.Logging;
using WinFEBuilder.Core.Services;
using Xunit;

namespace WinFEBuilder.Tests;

/// <summary>
/// ADK detection is environment-dependent (reads the local registry/filesystem). These tests
/// assert the detector runs safely and returns a coherent result object rather than asserting a
/// particular ADK is installed on the build agent.
/// </summary>
public class AdkDetectionTests
{
    [Fact]
    public void Detect_DoesNotThrow_AndReturnsResult()
    {
        using var tmp = new TempDir();
        var log = new LogService(tmp.Dir("logs"));
        var svc = new AdkDetectionService(log);

        var result = svc.Detect();

        Assert.NotNull(result);
        // If found, the essentials must be populated (internal consistency).
        if (result.Found)
        {
            Assert.False(string.IsNullOrEmpty(result.DismPath));
            Assert.False(string.IsNullOrEmpty(result.OscdimgPath));
            Assert.False(string.IsNullOrEmpty(result.WinPeRoot));
        }
        else
        {
            Assert.NotEmpty(result.Warnings);
        }
    }
}
