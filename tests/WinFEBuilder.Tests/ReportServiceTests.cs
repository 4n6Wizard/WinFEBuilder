using System.Text.Json;
using WinFEBuilder.Core.Configuration;
using WinFEBuilder.Core.Logging;
using WinFEBuilder.Core.Models;
using WinFEBuilder.Core.Reports;
using WinFEBuilder.Core.Services;
using Xunit;

namespace WinFEBuilder.Tests;

public class ReportServiceTests
{
    private static ReportService Build(TempDir tmp, out AppPaths paths)
    {
        paths = new AppPaths(tmp.Path);
        var log = new LogService(tmp.Dir("logs"));
        var settings = new SettingsService(Path.Combine(tmp.Path, "settings.json"));
        settings.Settings.WorkspaceRoot = tmp.Dir("workspace");
        var adk = new AdkDetectionService(log);
        return new ReportService(paths, log, settings, adk);
    }

    private static string WriteManifest(TempDir tmp)
    {
        var manifest = new BuildManifest
        {
            FrameworkSource = @"C:\fw",
            WorkspacePath = @"C:\ws\Build_x",
            MediaScript = "MakeWinFEx64-x86.bat",
            IsoScript = "Makex64-x86-CD.bat",
            MediaBuildExitCode = 0,
            BootStructureValidated = true,
            BootWimSha256 = "abc123",
            BootWimArchitecture = "x64",
            IsoSha256 = "def456",
            BuildStatus = "Build Successful"
        };
        var path = tmp.File("build-manifest.json", JsonSerializer.Serialize(manifest));
        return path;
    }

    [Fact]
    public void BuildModel_WithoutValidation_DoesNotFabricateForensicStatus()
    {
        using var tmp = new TempDir();
        var svc = Build(tmp, out _);
        var manifest = WriteManifest(tmp);

        var model = svc.BuildModel(manifest);

        Assert.Equal("Build Successful", model.BuildStatus);
        Assert.Equal("Validated", model.BootStructureStatus);
        // No validation record → these MUST remain NOT TESTED / NOT APPROVED.
        Assert.Equal("NOT TESTED", model.BootTestStatus);
        Assert.Equal("NOT TESTED", model.WriteProtectionTestStatus);
    }

    [Fact]
    public void BuildModel_WithPassingValidation_ReflectsRecordedStatus()
    {
        using var tmp = new TempDir();
        var svc = Build(tmp, out _);
        var manifest = WriteManifest(tmp);

        // The validation record is passed in-memory; nothing is read from a file.
        var record = new ValidationRecord
        {
            BootedUefi = ManualCheck.Pass,
            InternalSourceOfflineOrReadOnly = ManualCheck.Pass,
            TestSourceHashMatchedBeforeAfter = ManualCheck.Pass
        };

        var model = svc.BuildModel(manifest, record);
        Assert.Contains("Passed", model.BootTestStatus);
        Assert.Contains("Passed", model.WriteProtectionTestStatus);
    }

    [Fact]
    public void Generate_WritesHtmlOnly_NoJson()
    {
        using var tmp = new TempDir();
        var svc = Build(tmp, out var paths);
        var manifest = WriteManifest(tmp);

        var html = svc.Generate(manifest);
        Assert.True(File.Exists(html));
        Assert.EndsWith(".html", html);

        // No JSON report file is written alongside it.
        Assert.Empty(Directory.GetFiles(paths.ReportDir, "report_*.json"));

        var htmlText = File.ReadAllText(html);
        Assert.Contains("WinFE Builder", htmlText);
        Assert.Contains("NOT TESTED", htmlText); // honest defaults present
    }
}
