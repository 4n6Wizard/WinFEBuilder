using WinFEBuilder.Core.Validation;
using Xunit;

namespace WinFEBuilder.Tests;

public class InfParserTests
{
    private const string Amd64Inf = @"
[Version]
Signature=""$WINDOWS NT$""
Class=SCSIAdapter
ClassGuid={4d36e97b-e325-11ce-bfc1-08002be10318}
Provider=%ProviderName%

[Manufacturer]
%Mfg%=Standard,NTamd64

[Standard.NTamd64]
%Dev% = Install, PCI\VEN_1234
";

    private const string X86Inf = @"
[Version]
Class=Net
Provider=%P%
[Manufacturer]
%M%=Std,NTx86
[Std.NTx86]
";

    [Fact]
    public void DetectArchitectures_Amd64()
    {
        var a = InfParser.DetectArchitectures(Amd64Inf);
        Assert.Contains("amd64", a);
        Assert.DoesNotContain("x86", a);
    }

    [Fact]
    public void DetectArchitectures_X86()
        => Assert.Contains("x86", InfParser.DetectArchitectures(X86Inf));

    [Fact]
    public void DetectArchitectures_BareNt_MeansAll()
    {
        var a = InfParser.DetectArchitectures("[Manufacturer]\n%M%=Std,NT\n[Std.NT]");
        Assert.Contains("all", a);
    }

    [Fact]
    public void GetClassAndProvider()
    {
        Assert.Equal("SCSIAdapter", InfParser.GetClass(Amd64Inf));
        Assert.Equal("%ProviderName%", InfParser.GetProvider(Amd64Inf));
    }

    [Theory]
    [InlineData(new[] { "amd64" }, "amd64", true)]
    [InlineData(new[] { "x86" }, "amd64", false)]
    [InlineData(new[] { "all" }, "amd64", true)]
    [InlineData(new string[0], "amd64", true)] // undetermined → not blocked
    public void IsCompatibleWith(string[] declared, string target, bool expected)
        => Assert.Equal(expected, InfParser.IsCompatibleWith(declared, target));
}
