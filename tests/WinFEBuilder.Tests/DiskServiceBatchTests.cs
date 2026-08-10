using WinFEBuilder.Core.Configuration;
using WinFEBuilder.Core.Hashing;
using WinFEBuilder.Core.Logging;
using WinFEBuilder.Core.Models;
using WinFEBuilder.Core.Services;
using Xunit;

namespace WinFEBuilder.Tests;

/// <summary>
/// Sequential multi-USB batch behavior, all in simulation mode with a process runner that throws if
/// any external process is started — proving no destructive command runs. No physical disks required.
/// </summary>
public class DiskServiceBatchTests
{
    private sealed class NoProcessRunner : IProcessRunner
    {
        public Task<ProcessRunResult> RunAsync(string fileName, IEnumerable<string> arguments, string? workingDirectory = null,
            int? timeoutMs = null, Action<string>? onOutputLine = null, Action<string>? onErrorLine = null,
            bool closeStandardInput = false, CancellationToken ct = default)
            => throw new InvalidOperationException("No external process may run in simulation/batch tests.");
        public Task<ProcessRunResult> RunPowerShellScriptAsync(string powerShellExe, string scriptPath,
            IDictionary<string, string>? parameters = null, string? workingDirectory = null, int? timeoutMs = null,
            Action<string>? onOutputLine = null, Action<string>? onErrorLine = null, CancellationToken ct = default)
            => throw new InvalidOperationException("No PS in tests.");
        public Task<ProcessRunResult> RunBatchFileAsync(string batchFilePath, string? workingDirectory = null,
            int? timeoutMs = null, Action<string>? onOutputLine = null, Action<string>? onErrorLine = null,
            CancellationToken ct = default)
            => throw new InvalidOperationException("No batch in tests.");
    }

    private static DiskService Build(TempDir tmp, out string media)
    {
        var settings = new SettingsService(Path.Combine(tmp.Path, "settings.json"));
        settings.Settings.SimulationMode = true;
        settings.Settings.OutputRoot = tmp.Dir("output");
        var log = new LogService(tmp.Dir("logs"));
        var paths = new AppPaths(tmp.Path);
        media = tmp.Dir("media");
        tmp.Dir(@"media\Boot"); tmp.Dir(@"media\EFI"); tmp.Dir(@"media\Sources");
        tmp.File(@"media\Sources\boot.wim", "wim");
        return new DiskService(log, settings, new NoProcessRunner(), new HashService(), paths);
    }

    private static DiskInfo Sim(int n) => new()
    {
        Number = n, Model = "SimStick", SerialNumber = $"SIM-{n}", UniqueId = $"U-{n}",
        BusType = "USB", SizeBytes = 32L * 1024 * 1024 * 1024, IsRemovable = true, IsSimulated = true
    };

    private static DiskInfo MissingReal(int n) => new()
    {
        Number = n, Model = "Ghost", SerialNumber = $"G-{n}", UniqueId = $"GU-{n}",
        BusType = "USB", SizeBytes = 16L * 1024 * 1024 * 1024, IsRemovable = true, IsSimulated = false
    };

    private static UsbBatchRequest Req(IReadOnlyList<DiskInfo> targets, string media, string phrase, bool ack = true)
        => new() { Targets = targets, MediaSourcePath = media, ConfirmationPhrase = phrase, AcknowledgedDataLoss = ack };

    [Fact]
    public async Task GlobalAbort_WhenMediaInvalid()
    {
        using var tmp = new TempDir();
        var svc = Build(tmp, out _);
        var r = await svc.RunUsbBatchAsync(Req(new[] { Sim(99) }, @"C:\nope", "ERASE DISK 99"), null, null, CancellationToken.None);
        Assert.True(r.GlobalAbort);
        Assert.Contains("media", r.GlobalError!, StringComparison.OrdinalIgnoreCase);
        Assert.All(r.Targets, t => Assert.Equal(UsbTargetStatus.NotStarted, t.Status));
    }

    [Fact]
    public async Task GlobalAbort_WhenPhraseWrong()
    {
        using var tmp = new TempDir();
        var svc = Build(tmp, out var media);
        var r = await svc.RunUsbBatchAsync(Req(new[] { Sim(1), Sim(2), Sim(3) }, media, "ERASE 2 DISKS"), null, null, CancellationToken.None);
        Assert.True(r.GlobalAbort);
        Assert.Contains("ERASE 3 DISKS", r.GlobalError!);
    }

    [Fact]
    public async Task GlobalAbort_WhenNoAcknowledgement()
    {
        using var tmp = new TempDir();
        var svc = Build(tmp, out var media);
        var r = await svc.RunUsbBatchAsync(Req(new[] { Sim(5) }, media, "ERASE DISK 5", ack: false), null, null, CancellationToken.None);
        Assert.True(r.GlobalAbort);
    }

    [Fact]
    public async Task Simulation_ProcessesAllSequentially()
    {
        using var tmp = new TempDir();
        var svc = Build(tmp, out var media);
        var targets = new[] { Sim(11), Sim(12), Sim(13) };
        var r = await svc.RunUsbBatchAsync(Req(targets, media, "ERASE 3 DISKS"), null, null, CancellationToken.None);

        Assert.False(r.GlobalAbort);
        Assert.Equal(3, r.Targets.Count);
        Assert.All(r.Targets, t => Assert.Equal(UsbTargetStatus.Simulated, t.Status));
        Assert.Equal(3, r.Successful);
        Assert.Equal(0, r.Failed);
        // Sequential: results in target order with non-decreasing start times.
        Assert.Equal(new[] { 11, 12, 13 }, r.Targets.Select(t => t.DiskNumber).ToArray());
        for (int i = 1; i < r.Targets.Count; i++)
            Assert.True(r.Targets[i].StartTime >= r.Targets[i - 1].StartTime);
    }

    [Fact]
    public async Task PerDiskFailure_ContinuesToNext_AndRemovedDiskFailsSafely()
    {
        using var tmp = new TempDir();
        var svc = Build(tmp, out var media);
        // Middle target is a non-simulated disk number that isn't present → must fail safely and NOT
        // stop the batch. Disk 999999 will not exist on the test machine.
        var targets = new DiskInfo[] { Sim(21), MissingReal(999999), Sim(23) };
        var r = await svc.RunUsbBatchAsync(Req(targets, media, "ERASE 3 DISKS"), null, null, CancellationToken.None);

        Assert.Equal(UsbTargetStatus.Simulated, r.Targets[0].Status);
        Assert.Equal(UsbTargetStatus.Failed, r.Targets[1].Status);
        Assert.Equal(UsbTargetStatus.Simulated, r.Targets[2].Status);   // batch continued past the failure
        Assert.Equal(2, r.Successful);
        Assert.Equal(1, r.Failed);
        Assert.False(string.IsNullOrEmpty(r.Targets[1].ErrorMessage));
    }

    [Fact]
    public async Task Cancellation_PreventsProcessing()
    {
        using var tmp = new TempDir();
        var svc = Build(tmp, out var media);
        using var cts = new CancellationTokenSource();
        cts.Cancel(); // cancelled before the loop starts
        var r = await svc.RunUsbBatchAsync(Req(new[] { Sim(31), Sim(32) }, media, "ERASE 2 DISKS"), null, null, cts.Token);

        Assert.False(r.GlobalAbort);
        Assert.All(r.Targets, t => Assert.Equal(UsbTargetStatus.Canceled, t.Status));
        Assert.Equal(0, r.Successful);
        Assert.Equal(2, r.Canceled);
    }

    [Fact]
    public async Task SingleDiskBatch_UsesSingleDiskPhrase()
    {
        using var tmp = new TempDir();
        var svc = Build(tmp, out var media);
        var r = await svc.RunUsbBatchAsync(Req(new[] { Sim(42) }, media, "ERASE DISK 42"), null, null, CancellationToken.None);
        Assert.False(r.GlobalAbort);
        Assert.Single(r.Targets);
        Assert.Equal(UsbTargetStatus.Simulated, r.Targets[0].Status);
    }
}
