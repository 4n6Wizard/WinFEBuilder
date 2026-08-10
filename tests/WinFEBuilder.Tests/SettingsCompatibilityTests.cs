using WinFEBuilder.Core.Configuration;
using Xunit;

namespace WinFEBuilder.Tests;

/// <summary>
/// Guards backward compatibility of the settings file: an older file missing newer keys, or a
/// corrupt file, must load without crashing and fall back to sensible defaults.
/// </summary>
public class SettingsCompatibilityTests
{
    [Fact]
    public void OlderSettings_MissingNewKeys_LoadWithDefaults()
    {
        using var tmp = new TempDir();
        var path = System.IO.Path.Combine(tmp.Path, "settings.json");
        // Simulate an old file that only knew about WorkspaceRoot.
        System.IO.File.WriteAllText(path, "{ \"WorkspaceRoot\": \"C:\\\\LegacyWorkspace\" }");

        var svc = new SettingsService(path);

        Assert.Equal(@"C:\LegacyWorkspace", svc.Settings.WorkspaceRoot);
        Assert.False(string.IsNullOrWhiteSpace(svc.Settings.OutputRoot));   // default applied
        Assert.True(svc.Settings.MinimumFreeSpaceGb > 0);                    // default applied
        Assert.False(string.IsNullOrWhiteSpace(svc.Settings.PreferredPowerShell));
    }

    [Fact]
    public void CorruptSettings_FallBackToDefaults_NoThrow()
    {
        using var tmp = new TempDir();
        var path = System.IO.Path.Combine(tmp.Path, "settings.json");
        System.IO.File.WriteAllText(path, "{ this is not valid json ");

        var svc = new SettingsService(path);          // must not throw
        Assert.NotNull(svc.Settings);
        Assert.False(string.IsNullOrWhiteSpace(svc.Settings.WorkspaceRoot));
    }

    [Fact]
    public void MissingSettingsFile_UsesDefaults()
    {
        using var tmp = new TempDir();
        var path = System.IO.Path.Combine(tmp.Path, "does-not-exist.json");
        var svc = new SettingsService(path);
        Assert.NotNull(svc.Settings);
        Assert.False(string.IsNullOrWhiteSpace(svc.Settings.OutputRoot));
    }

    [Fact]
    public void StaleLastFrameworkPath_IsClearedOnLoad()
    {
        using var tmp = new TempDir();
        var path = System.IO.Path.Combine(tmp.Path, "settings.json");
        // A framework path from another machine that does not exist here.
        System.IO.File.WriteAllText(path, "{ \"LastFrameworkPath\": \"C:\\\\Users\\\\other\\\\IntelWinFE\" }");

        var svc = new SettingsService(path);

        Assert.Null(svc.Settings.LastFrameworkPath);
    }

    [Fact]
    public void ExistingLastFrameworkPath_IsKept()
    {
        using var tmp = new TempDir();
        var fw = tmp.Dir("framework");
        var path = System.IO.Path.Combine(tmp.Path, "settings.json");
        System.IO.File.WriteAllText(path,
            "{ \"LastFrameworkPath\": " + System.Text.Json.JsonSerializer.Serialize(fw) + " }");

        var svc = new SettingsService(path);

        Assert.Equal(fw, svc.Settings.LastFrameworkPath);
    }

    [Fact]
    public void SaveThenLoad_RoundTripsOperatorAndOrg()
    {
        using var tmp = new TempDir();
        var path = System.IO.Path.Combine(tmp.Path, "settings.json");
        var a = new SettingsService(path);
        a.Settings.OperatorName = "Examiner A";
        a.Settings.OrganizationName = "Agency";
        a.Save();

        var b = new SettingsService(path);
        Assert.Equal("Examiner A", b.Settings.OperatorName);
        Assert.Equal("Agency", b.Settings.OrganizationName);
    }
}
