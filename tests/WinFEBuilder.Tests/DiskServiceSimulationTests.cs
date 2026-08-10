using WinFEBuilder.Core.Configuration;
using WinFEBuilder.Core.Hashing;
using WinFEBuilder.Core.Logging;
using WinFEBuilder.Core.Models;
using WinFEBuilder.Core.Services;
using Xunit;

namespace WinFEBuilder.Tests;

/// <summary>
/// Verifies the USB creation guards WITHOUT ever touching a real disk. Simulation mode is on, and
/// a simulated disk is used, so no destructive command can run.
/// </summary>
public class DiskServiceSimulationTests
{
    private sealed class NoopRunner : IProcessRunner
    {
        public Task<ProcessRunResult> RunAsync(string fileName, IEnumerable<string> arguments,
            string? workingDirectory = null, int? timeoutMs = null, Action<string>? onOutputLine = null,
            Action<string>? onErrorLine = null, bool closeStandardInput = false, CancellationToken ct = default)
            => throw new InvalidOperationException("A process must NOT be started in simulation mode.");

        public Task<ProcessRunResult> RunPowerShellScriptAsync(string powerShellExe, string scriptPath,
            IDictionary<string, string>? parameters = null, string? workingDirectory = null, int? timeoutMs = null,
            Action<string>? onOutputLine = null, Action<string>? onErrorLine = null, CancellationToken ct = default)
            => throw new InvalidOperationException("No PS in simulation.");

        public Task<ProcessRunResult> RunBatchFileAsync(string batchFilePath, string? workingDirectory = null,
            int? timeoutMs = null, Action<string>? onOutputLine = null, Action<string>? onErrorLine = null,
            CancellationToken ct = default)
            => throw new InvalidOperationException("No batch in simulation.");
    }

    private static DiskService Build(TempDir tmp, out DiskInfo simDisk, out string mediaRoot)
    {
        var settings = new SettingsService(System.IO.Path.Combine(tmp.Path, "settings.json"));
        settings.Settings.SimulationMode = true;
        settings.Settings.WorkspaceRoot = tmp.Dir("workspace");
        settings.Settings.OutputRoot = tmp.Dir("output");
        var log = new LogService(tmp.Dir("logs"));
        var paths = new AppPaths(tmp.Path);
        var svc = new DiskService(log, settings, new NoopRunner(), new HashService(), paths);

        // Valid media root with sources\boot.wim.
        mediaRoot = tmp.Dir(@"media");
        tmp.Dir(@"media\Boot");
        tmp.Dir(@"media\EFI");
        tmp.Dir(@"media\Sources");
        tmp.File(@"media\Sources\boot.wim", "wim");

        simDisk = new DiskInfo
        {
            Number = 99, Model = "DemoStick", SerialNumber = "SIM-1", UniqueId = "SIM-U",
            BusType = "USB", SizeBytes = 32L * 1024 * 1024 * 1024, IsRemovable = true, IsSimulated = true
        };
        return svc;
    }

    [Fact]
    public async Task Create_Simulation_GeneratesScript_DoesNotExecute()
    {
        using var tmp = new TempDir();
        var svc = Build(tmp, out var disk, out var media);

        var req = new UsbBuildRequest
        {
            SelectedDisk = disk,
            MediaSourcePath = media,
            ConfirmationPhrase = "ERASE DISK 99",
            AcknowledgedDataLoss = true
        };

        var result = await svc.CreateUsbAsync(req, null, CancellationToken.None);

        Assert.True(result.Simulated);
        Assert.False(result.Executed);
        Assert.Contains("select disk 99", result.DiskPartScript);
        Assert.Contains("format fs=fat32", result.DiskPartScript);
        Assert.Equal("SIMULATED", result.UsbCreationStatus);
    }

    [Fact]
    public async Task Create_WrongPhrase_IsRejected()
    {
        using var tmp = new TempDir();
        var svc = Build(tmp, out var disk, out var media);

        var req = new UsbBuildRequest
        {
            SelectedDisk = disk,
            MediaSourcePath = media,
            ConfirmationPhrase = "ERASE DISK 3", // wrong number
            AcknowledgedDataLoss = true
        };

        var result = await svc.CreateUsbAsync(req, null, CancellationToken.None);
        Assert.False(result.Executed);
        Assert.Equal("FAIL", result.UsbCreationStatus);
        Assert.Contains(result.Errors, e => e.Contains("ERASE DISK 99"));
    }

    [Fact]
    public async Task Create_MissingAcknowledgement_IsRejected()
    {
        using var tmp = new TempDir();
        var svc = Build(tmp, out var disk, out var media);

        var req = new UsbBuildRequest
        {
            SelectedDisk = disk,
            MediaSourcePath = media,
            ConfirmationPhrase = "ERASE DISK 99",
            AcknowledgedDataLoss = false
        };

        var result = await svc.CreateUsbAsync(req, null, CancellationToken.None);
        Assert.False(result.Executed);
        Assert.Contains(result.Errors, e => e.Contains("acknowledgement", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Create_InvalidMedia_IsRejected()
    {
        using var tmp = new TempDir();
        var svc = Build(tmp, out var disk, out _);

        var req = new UsbBuildRequest
        {
            SelectedDisk = disk,
            MediaSourcePath = tmp.Dir("empty"), // no sources\boot.wim
            ConfirmationPhrase = "ERASE DISK 99",
            AcknowledgedDataLoss = true
        };

        var result = await svc.CreateUsbAsync(req, null, CancellationToken.None);
        Assert.False(result.Executed);
        Assert.Contains(result.Errors, e => e.Contains("boot.wim", StringComparison.OrdinalIgnoreCase));
    }
}
