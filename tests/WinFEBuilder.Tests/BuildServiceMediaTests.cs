using WinFEBuilder.Core.Configuration;
using WinFEBuilder.Core.Hashing;
using WinFEBuilder.Core.Logging;
using WinFEBuilder.Core.Models;
using WinFEBuilder.Core.Services;
using Xunit;

namespace WinFEBuilder.Tests;

/// <summary>
/// Exercises the media-structure verification logic via a fake DISM service, without needing the
/// ADK or running any batch file. The end-to-end build (real batch execution) requires the ADK and
/// is not run automatically.
/// </summary>
public class BuildServiceMediaTests
{
    private sealed class FakeDism : IDismService
    {
        public string? ResolveDismPath() => "dism.exe";
        public Task<WimInfo> GetWimInfoAsync(string wimPath, CancellationToken ct = default)
            => Task.FromResult(new WimInfo
            {
                WimPath = wimPath,
                DismSucceeded = true,
                Architecture = "x64",
                Sha256 = "abc",
                Images = { }
            });
    }

    private sealed class StubEnv : IEnvironmentService
    {
        public bool IsElevated() => true;
        public Task<EnvironmentAuditResult> RunAuditAsync(CancellationToken ct = default)
            => Task.FromResult(new EnvironmentAuditResult());
    }

    private sealed class StubRunner : IProcessRunner
    {
        public Task<ProcessRunResult> RunAsync(string fileName, IEnumerable<string> arguments,
            string? workingDirectory = null, int? timeoutMs = null, Action<string>? onOutputLine = null,
            Action<string>? onErrorLine = null, bool closeStandardInput = false, CancellationToken ct = default)
            => Task.FromResult(new ProcessRunResult { FileName = fileName, Arguments = "", StartTime = DateTimeOffset.Now, FinishTime = DateTimeOffset.Now });

        public Task<ProcessRunResult> RunPowerShellScriptAsync(string powerShellExe, string scriptPath,
            IDictionary<string, string>? parameters = null, string? workingDirectory = null, int? timeoutMs = null,
            Action<string>? onOutputLine = null, Action<string>? onErrorLine = null, CancellationToken ct = default)
            => Task.FromResult(new ProcessRunResult { FileName = powerShellExe, Arguments = "", StartTime = DateTimeOffset.Now, FinishTime = DateTimeOffset.Now });

        public Task<ProcessRunResult> RunBatchFileAsync(string batchFilePath, string? workingDirectory = null,
            int? timeoutMs = null, Action<string>? onOutputLine = null, Action<string>? onErrorLine = null,
            CancellationToken ct = default)
            => Task.FromResult(new ProcessRunResult { FileName = batchFilePath, Arguments = "", StartTime = DateTimeOffset.Now, FinishTime = DateTimeOffset.Now });
    }

    private static BuildService Build(TempDir tmp)
    {
        var settings = new SettingsService(System.IO.Path.Combine(tmp.Path, "settings.json"));
        settings.Settings.WorkspaceRoot = tmp.Dir("workspace");
        settings.Settings.OutputRoot = tmp.Dir("output");
        var log = new LogService(tmp.Dir("logs"));
        var hash = new HashService();
        var ws = new WorkspaceService(settings, log);
        var framework = new FrameworkService(log, hash, ws, settings);
        var runner = new StubRunner();
        var dism = new FakeDism();
        var drivers = new DriverService(log, runner, dism, hash, new AppPaths(tmp.Path));
        return new BuildService(log, settings, new StubEnv(), framework, dism, runner, hash, drivers);
    }

    // VerifyMediaAsync is private; we validate the same rules through MediaLocator + a constructed tree.
    [Fact]
    public void MediaTree_WithAllComponents_IsStructurallyComplete()
    {
        using var tmp = new TempDir();
        var root = tmp.Dir(@"workspace\Build_x\framework\USB");
        tmp.Dir(@"workspace\Build_x\framework\USB\Boot");
        tmp.Dir(@"workspace\Build_x\framework\USB\EFI");
        tmp.Dir(@"workspace\Build_x\framework\USB\Sources");
        var wim = tmp.File(@"workspace\Build_x\framework\USB\Sources\boot.wim", "wimdata");

        var media = new MediaValidationResult { MediaRoot = root, BootWimPath = wim };
        foreach (var comp in Core.Validation.MediaLocator.ExpectedBootComponents)
        {
            var full = System.IO.Path.Combine(root, comp);
            media.Expected[comp] = comp.EndsWith("boot.wim", StringComparison.OrdinalIgnoreCase)
                ? File.Exists(full)
                : Directory.Exists(full);
        }

        Assert.True(media.StructureValid);
    }

    [Fact]
    public void MediaTree_MissingBootWim_IsIncomplete()
    {
        using var tmp = new TempDir();
        var root = tmp.Dir(@"ws\USB");
        tmp.Dir(@"ws\USB\Boot");
        tmp.Dir(@"ws\USB\EFI");
        tmp.Dir(@"ws\USB\Sources");

        var media = new MediaValidationResult { MediaRoot = root };
        foreach (var comp in Core.Validation.MediaLocator.ExpectedBootComponents)
        {
            var full = System.IO.Path.Combine(root, comp);
            media.Expected[comp] = comp.EndsWith("boot.wim", StringComparison.OrdinalIgnoreCase)
                ? File.Exists(full)
                : Directory.Exists(full);
        }

        Assert.False(media.StructureValid);
    }

    [Fact]
    public async Task Build_BlocksAtPreflight_WhenAdkMissing()
    {
        // StubEnv reports no ADK, so the build must stop at the environment-audit gate and never
        // attempt to run a batch file.
        using var tmp = new TempDir();
        var svc = Build(tmp);
        var result = await svc.RunBuildAsync(new BuildRequest(), null, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains(result.Stages, s => s.Name == "Environment audit" && s.Status == CheckStatus.Fail);
        Assert.DoesNotContain(result.Stages, s => s.Name == "Run WinFE media build");
        Assert.NotNull(result.RecommendedAction);
    }
}
