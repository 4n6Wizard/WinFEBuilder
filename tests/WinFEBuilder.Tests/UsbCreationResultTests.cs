using WinFEBuilder.Core.Models;
using Xunit;

namespace WinFEBuilder.Tests;

public class UsbCreationResultTests
{
    private static UsbCreationResult AllStages(string status)
    {
        var r = new UsbCreationResult { Executed = true };
        foreach (var s in UsbStage.Mandatory) r.SetStage(s, status);
        return r;
    }

    [Fact]
    public void Success_RequiresEveryMandatoryStageToPass()
    {
        var r = AllStages(UsbStage.Pass);
        Assert.True(r.AllMandatoryStagesPassed());
        Assert.True(r.Success);
    }

    [Fact]
    public void Success_False_WhenAnyStageMissing()
    {
        var r = new UsbCreationResult { Executed = true };
        // All but the last stage pass.
        foreach (var s in UsbStage.Mandatory.Take(UsbStage.Mandatory.Count - 1))
            r.SetStage(s, UsbStage.Pass);
        Assert.False(r.AllMandatoryStagesPassed());
        Assert.False(r.Success);
    }

    [Fact]
    public void Success_False_WhenOneStageFailed()
    {
        var r = AllStages(UsbStage.Pass);
        r.SetStage(UsbStage.BootConfig, UsbStage.Fail, "bootsect exit 1");
        Assert.False(r.Success);
    }

    [Fact]
    public void Success_False_WhenErrorsPresent_EvenIfStagesPass()
    {
        var r = AllStages(UsbStage.Pass);
        r.Errors.Add("something went wrong");
        Assert.False(r.Success);
    }

    [Fact]
    public void Success_False_WhenNotExecuted()
    {
        var r = AllStages(UsbStage.Pass);
        r.Executed = false;
        Assert.False(r.Success);
    }
}
