using System.Text.Json;
using WinFEBuilder.Core.Configuration;
using Xunit;

namespace WinFEBuilder.Tests;

/// <summary>
/// The released executable ships without a settings.json, so <see cref="AppSettings"/>'s own defaults
/// are what an operator gets on first run. These pin the values that reach a first-time user.
/// </summary>
public class ReleaseDefaultsTests
{
    [Fact]
    public void SimulationMode_DefaultsToRealWrites()
    {
        // Regression guard: this shipped as `true` in 1.0.0, so a user who downloaded the exe to
        // write media got a green "simulating" banner and a fake disk #99 instead — the tool looked
        // broken. Simulation is opt-in; the disk gates are the safety mechanism, not this flag.
        Assert.False(new AppSettings().SimulationMode);
    }

    [Fact]
    public void SimulationMode_IsNotWrittenToSettingsJson()
    {
        // It is a developer guard, not an operator option: it must never show up in an operator's
        // config file as a knob to flip.
        var json = JsonSerializer.Serialize(new AppSettings());

        Assert.DoesNotContain("SimulationMode", json);
        // Sanity check that serialization is actually producing the real settings.
        Assert.Contains("WorkspaceRoot", json);
    }

    [Fact]
    public void SimulationMode_InAnExistingConfigFileIsIgnored()
    {
        // Operators upgrading from 1.0.0 may still have "SimulationMode": true in their settings.json.
        // It must not be honored, or the tool would silently refuse to write media for them.
        var legacy = """
        {
          "WorkspaceRoot": "workspace",
          "SimulationMode": true
        }
        """;

        var loaded = JsonSerializer.Deserialize<AppSettings>(
            legacy, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        Assert.NotNull(loaded);
        Assert.Equal("workspace", loaded!.WorkspaceRoot);
        Assert.False(loaded.SimulationMode);
    }

    [Fact]
    public void FirstRunDefaults_AreUsableWithoutEditingAnyFile()
    {
        var s = new AppSettings();

        Assert.Equal("workspace", s.WorkspaceRoot);
        Assert.Equal("output", s.OutputRoot);
        Assert.Equal("logs", s.LogRoot);
        Assert.Equal("reports", s.ReportRoot);
        Assert.Equal("powershell", s.PreferredPowerShell);

        // No operator/organization is invented, and no framework path is assumed.
        Assert.Null(s.LastFrameworkPath);
        Assert.Null(s.OperatorName);
        Assert.Null(s.OrganizationName);

        // Guard rails that should never default to zero/disabled.
        Assert.True(s.MinimumFreeSpaceGb > 0);
        Assert.True(s.DiskPartTimeoutSeconds > 0);
    }
}
