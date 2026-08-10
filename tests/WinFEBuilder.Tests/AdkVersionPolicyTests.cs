using WinFEBuilder.Core.Models;
using WinFEBuilder.Core.Services;
using Xunit;

namespace WinFEBuilder.Tests;

/// <summary>
/// WinFE builds against ADK 1803 (10.1.17134.x) — the release Colin Ramsden's build instructions
/// specify — or 1809 (10.1.17763.x), the next release, which also works. ADK 1903 and later break the
/// framework. These tests pin that rule, including the cases that must NOT hard-block: an
/// undetectable version, and a compatible kit installed beside a newer one.
/// </summary>
public class AdkVersionPolicyTests
{
    [Theory]
    [InlineData("10.1.17763.1")]
    [InlineData("10.1.17763.132")]
    [InlineData("10.1.17763")]
    [InlineData("10.1.17763.7320")]   // the build that produced boot-tested media
    public void IsSupported_AcceptsAdk1809(string version) =>
        Assert.True(AdkVersionPolicy.IsSupported(version));

    [Theory]
    [InlineData("10.1.17134.1")]
    [InlineData("10.1.17134")]
    [InlineData("10.1.17134.12")]
    public void IsSupported_AcceptsAdk1803_TheDocumentedRelease(string version)
    {
        // Regression guard: 1.0.0 rejected 1803, so anyone following Colin's documented instructions
        // had their build refused at preflight.
        Assert.True(AdkVersionPolicy.IsSupported(version));
    }

    [Theory]
    [InlineData("10.1.18362.1")]   // 1903 — first release that breaks the framework
    [InlineData("10.1.19041.1")]   // 2004
    [InlineData("10.1.22621.1")]   // Windows 11 22H2
    [InlineData("10.1.26100.1")]   // Windows 11 24H2
    [InlineData("10.1.16299.1")]   // 1709 — older than the framework targets
    public void IsSupported_RejectsIncompatibleReleases(string version) =>
        Assert.False(AdkVersionPolicy.IsSupported(version));

    [Fact]
    public void SupportedBuilds_AreExactly1803And1809()
    {
        Assert.Equal(new[] { 17134, 17763 }, AdkVersionPolicy.SupportedBuilds);
        Assert.True(AdkVersionPolicy.FirstIncompatibleBuild > AdkVersionPolicy.NewestSupportedBuild);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("unknown")]
    [InlineData("10.1")]           // no build component
    public void IsSupported_RejectsUnparseableVersions(string? version) =>
        Assert.False(AdkVersionPolicy.IsSupported(version));

    [Fact]
    public void Evaluate_Supported_WhenRequiredVersionPresent()
    {
        Assert.Equal(AdkVersionSupport.Supported, AdkVersionPolicy.Evaluate(new[] { "10.1.17763.1" }));
        Assert.Equal(AdkVersionSupport.Supported, AdkVersionPolicy.Evaluate(new[] { "10.1.17134.1" }));
    }

    [Fact]
    public void Evaluate_Supported_WhenBothCompatibleKitsPresent()
    {
        // 1803 and 1809 side by side is not a "mixed" install worth warning about — both work.
        var versions = new[] { "10.1.17763.1", "10.1.17134.1" };

        Assert.Equal(AdkVersionSupport.Supported, AdkVersionPolicy.Evaluate(versions));
        Assert.False(AdkVersionPolicy.HasMixedInstalls(versions));
    }

    [Fact]
    public void Evaluate_Unsupported_WhenOnlyNewerKitsPresent() =>
        Assert.Equal(AdkVersionSupport.Unsupported,
            AdkVersionPolicy.Evaluate(new[] { "10.1.26100.1", "10.1.22621.1" }));

    [Fact]
    public void Evaluate_Supported_WhenRequiredKitSitsBesideNewerOnes()
    {
        // Side-by-side kits share the Windows Kits\10 root and the newest version wins the folder
        // scan. The required payload being present is what matters, so this must not be a block.
        var versions = new[] { "10.1.26100.1", "10.1.17763.1" };

        Assert.Equal(AdkVersionSupport.Supported, AdkVersionPolicy.Evaluate(versions));
        Assert.True(AdkVersionPolicy.HasMixedInstalls(versions));
    }

    [Fact]
    public void Evaluate_Unknown_WhenNothingParseable()
    {
        Assert.Equal(AdkVersionSupport.Unknown, AdkVersionPolicy.Evaluate(new[] { "unknown", "" }));
        Assert.Equal(AdkVersionSupport.Unknown, AdkVersionPolicy.Evaluate(Array.Empty<string>()));
        Assert.Equal(AdkVersionSupport.Unknown, AdkVersionPolicy.Evaluate(null));
    }

    [Fact]
    public void HasMixedInstalls_FalseForSingleGeneration()
    {
        Assert.False(AdkVersionPolicy.HasMixedInstalls(new[] { "10.1.17763.1", "10.1.17763.132" }));
        Assert.False(AdkVersionPolicy.HasMixedInstalls(new[] { "10.1.26100.1" }));
        Assert.False(AdkVersionPolicy.HasMixedInstalls(null));
    }

    [Fact]
    public void Guidance_NamesBothDownloadsAndTheIncompatibleReleases()
    {
        var guidance = AdkVersionPolicy.Guidance;

        Assert.Contains("1803", guidance);
        Assert.Contains("1809", guidance);
        Assert.Contains("WinPE", guidance);
        Assert.Contains("1903", guidance);
        Assert.Contains(AdkVersionPolicy.AdkDownloadUrl, guidance);
        Assert.Contains(AdkVersionPolicy.WinPeDownloadUrl, guidance);
    }

    [Fact]
    public void Installation_UnsupportedFlag_TracksVersionSupport()
    {
        var adk = new AdkInstallation { VersionSupport = AdkVersionSupport.Unsupported };
        Assert.True(adk.IsUnsupportedVersion);

        adk.VersionSupport = AdkVersionSupport.Supported;
        Assert.False(adk.IsUnsupportedVersion);

        // Unknown must never read as unsupported — detection is best-effort and must not block.
        adk.VersionSupport = AdkVersionSupport.Unknown;
        Assert.False(adk.IsUnsupportedVersion);
    }

    [Fact]
    public void Installation_DefaultsToUnknown_NotUnsupported() =>
        Assert.Equal(AdkVersionSupport.Unknown, new AdkInstallation().VersionSupport);
}
