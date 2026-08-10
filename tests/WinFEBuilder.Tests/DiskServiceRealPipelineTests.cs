using System.Text.Json;
using WinFEBuilder.Core.Configuration;
using WinFEBuilder.Core.Hashing;
using WinFEBuilder.Core.Logging;
using WinFEBuilder.Core.Models;
using WinFEBuilder.Core.Services;
using Xunit;

namespace WinFEBuilder.Tests;

/// <summary>
/// Exercises the REAL (non-simulated) per-disk pipeline without touching hardware. Device-touching
/// steps (disk refresh, drive-letter detection, file copy, verification) are overridden by a test
/// subclass, and all external processes go through a scripted runner whose exit codes/timeouts are
/// controlled per test. This lets us prove bootsect-failure propagation, DiskPart timeout handling,
/// drive-letter normalization (no "K::"), and honest JSON records.
/// </summary>
public class DiskServiceRealPipelineTests
{
    // ---- Scripted process runner: records every call, decides diskpart/bootsect outcomes ----
    private sealed class ScriptedRunner : IProcessRunner
    {
        public readonly List<(string File, string[] Args)> Calls = new();
        public Func<string[], ProcessRunResult>? DiskPart;
        public Func<string[], ProcessRunResult>? BootSect;

        public Task<ProcessRunResult> RunAsync(string fileName, IEnumerable<string> arguments, string? workingDirectory = null,
            int? timeoutMs = null, Action<string>? onOutputLine = null, Action<string>? onErrorLine = null,
            bool closeStandardInput = false, CancellationToken ct = default)
        {
            var args = arguments.ToArray();
            Calls.Add((fileName, args));
            var lower = fileName.ToLowerInvariant();
            ProcessRunResult r =
                lower.Contains("diskpart") ? (DiskPart?.Invoke(args) ?? Ok(fileName, args)) :
                lower.Contains("bootsect") ? (BootSect?.Invoke(args) ?? Ok(fileName, args)) :
                Ok(fileName, args);
            return Task.FromResult(r);
        }

        public Task<ProcessRunResult> RunPowerShellScriptAsync(string powerShellExe, string scriptPath,
            IDictionary<string, string>? parameters = null, string? workingDirectory = null, int? timeoutMs = null,
            Action<string>? onOutputLine = null, Action<string>? onErrorLine = null, CancellationToken ct = default)
            => throw new InvalidOperationException("unused");
        public Task<ProcessRunResult> RunBatchFileAsync(string batchFilePath, string? workingDirectory = null,
            int? timeoutMs = null, Action<string>? onOutputLine = null, Action<string>? onErrorLine = null,
            CancellationToken ct = default)
            => throw new InvalidOperationException("unused");
    }

    private static ProcessRunResult Ok(string file, string[] a)
        => Res(file, a, 0);
    private static ProcessRunResult Res(string file, string[] a, int exit, bool timedOut = false, string err = "")
        => new()
        {
            FileName = file, Arguments = string.Join(' ', a), ExitCode = exit, TimedOut = timedOut,
            StandardError = err, StartTime = DateTimeOffset.Now, FinishTime = DateTimeOffset.Now
        };

    // ---- Test subclass: overrides device-touching seams, keeps the real orchestration ----
    private sealed class PipelineDiskService : DiskService
    {
        private readonly Dictionary<int, DiskInfo> _present;
        public string DriveLetter = "K:";
        public bool VerifyPasses = true;

        public PipelineDiskService(ILogService log, ISettingsService settings, IProcessRunner runner,
            IHashService hash, AppPaths paths, IEnumerable<DiskInfo> present)
            : base(log, settings, runner, hash, paths)
            => _present = present.ToDictionary(d => d.Number);

        public override Task<DiskInfo?> RefreshDiskAsync(int number, CancellationToken ct = default)
            => Task.FromResult(_present.TryGetValue(number, out var d) ? d : null);

        protected override Task<string?> DetectDriveLetterAsync(int diskNumber, string label, CancellationToken ct)
            => Task.FromResult<string?>(DriveLetter);

        protected override (int files, long bytes) CopyTree(string source, string destRoot, CancellationToken ct)
            => (5, 1000);

        protected override void VerifyMedia(string driveRoot, UsbCreationResult result, CancellationToken ct)
        {
            if (VerifyPasses)
            {
                result.BootStructureStatus = "PASS";
                result.OfflineStructuralValidationStatus = "PASS";
                result.SetStage(UsbStage.Verification, UsbStage.Pass);
            }
            else
            {
                base.VerifyMedia(driveRoot, result, ct);
            }
        }
    }

    // Subclass that does NOT override VerifyMedia so we can test the real verification logic.
    private sealed class VerifyExposedDiskService : DiskService
    {
        public VerifyExposedDiskService(ILogService log, ISettingsService settings, IProcessRunner runner,
            IHashService hash, AppPaths paths) : base(log, settings, runner, hash, paths) { }

        public void RunVerify(string driveRoot, UsbCreationResult result) => VerifyMedia(driveRoot, result, default);
    }

    private static DiskInfo RealDisk(int n) => new()
    {
        Number = n, Model = "RealStick", SerialNumber = $"R-{n}", UniqueId = $"RU-{n}",
        BusType = "USB", SizeBytes = 32L * 1024 * 1024 * 1024, IsRemovable = true, IsSimulated = false
        // DriveLetters intentionally empty so the disk is eligible (hosts no protected volume).
    };

    // A non-removable, non-system, non-protected internal disk (e.g. a data drive).
    private static DiskInfo FixedDisk(int n) => new()
    {
        Number = n, Model = "InternalData", SerialNumber = $"F-{n}", UniqueId = $"FU-{n}",
        BusType = "SATA", SizeBytes = 512L * 1024 * 1024 * 1024, IsRemovable = false, IsSimulated = false
    };

    private static (SettingsService settings, LogService log, AppPaths paths, string media) Env(TempDir tmp)
    {
        var settings = new SettingsService(Path.Combine(tmp.Path, "settings.json"));
        settings.Settings.SimulationMode = false; // REAL path
        settings.Settings.OutputRoot = tmp.Dir("output");
        settings.Settings.WorkspaceRoot = tmp.Dir("workspace");
        var log = new LogService(tmp.Dir("logs"));
        var paths = new AppPaths(tmp.Path);

        var media = tmp.Dir("media");
        tmp.Dir(@"media\Boot"); tmp.Dir(@"media\EFI"); tmp.Dir(@"media\Sources");
        tmp.File(@"media\Sources\boot.wim", "wim");
        tmp.File(@"media\Boot\bootsect.exe", "MZ"); // so ConfigureBootSectorAsync "finds" bootsect
        return (settings, log, paths, media);
    }

    // ============================ 1. No double colon =============================================
    [Theory]
    [InlineData("K:")]
    [InlineData("K")]
    [InlineData("K:\\")]
    [InlineData("F:\\")]
    public async Task BootSect_TargetIsNormalized_NoDoubleColon(string detected)
    {
        using var tmp = new TempDir();
        var (settings, log, paths, media) = Env(tmp);
        var runner = new ScriptedRunner();
        var disk = RealDisk(8);
        var svc = new PipelineDiskService(log, settings, runner, new HashService(), paths, new[] { disk })
        { DriveLetter = detected };

        var result = await svc.CreateSingleUsbAsync(disk, media, "WINFE", null, CancellationToken.None);

        Assert.True(result.Success, string.Join(";", result.Errors));
        var boot = runner.Calls.Single(c => c.File.ToLowerInvariant().Contains("bootsect"));
        Assert.DoesNotContain("::", string.Join(' ', boot.Args));
        Assert.Contains(boot.Args, a => a is "K:" or "F:");
        Assert.DoesNotContain(boot.Args, a => a.Contains("::"));
        // The whole recorded command line is clean too.
        Assert.DoesNotContain("::", result.BootSectArguments ?? "");
    }

    // ============ 1b. Fixed (non-removable) disk refused unless explicitly allowed ==============
    [Fact]
    public async Task FixedDisk_RefusedByDefault_AllowedOnlyWithExplicitOptIn()
    {
        using var tmp = new TempDir();
        var (settings, log, paths, media) = Env(tmp);
        var disk = FixedDisk(8);
        var svc = new PipelineDiskService(log, settings, new ScriptedRunner(), new HashService(), paths, new[] { disk })
        { DriveLetter = "K:" };

        // Default: the Core refuses a fixed disk (does not infer intent from the disk's own removability).
        var refused = await svc.CreateSingleUsbAsync(disk, media, "WINFE", null, CancellationToken.None);
        Assert.False(refused.Success);
        Assert.Equal(UsbStage.Revalidation, refused.FailedStage);

        // Explicit operator opt-in: allowed to proceed and succeeds on the scripted real path.
        var allowed = await svc.CreateSingleUsbAsync(disk, media, "WINFE", null, CancellationToken.None, allowFixedDisk: true);
        Assert.True(allowed.Success, string.Join(";", allowed.Errors));
    }

    // ============================ 2. Bootsect failure propagates ================================
    [Fact]
    public async Task BootSectExit1_MarksTargetFailed_NotSuccess()
    {
        using var tmp = new TempDir();
        var (settings, log, paths, media) = Env(tmp);
        var runner = new ScriptedRunner { BootSect = a => Res("bootsect.exe", a, 1, err: "boot config failed") };
        var disk = RealDisk(8);
        var svc = new PipelineDiskService(log, settings, runner, new HashService(), paths, new[] { disk })
        { VerifyPasses = true }; // even if verification WOULD pass, the disk must still fail

        var req = new UsbBatchRequest { Targets = new[] { disk }, MediaSourcePath = media, ConfirmationPhrase = "ERASE DISK 8", AcknowledgedDataLoss = true };
        var batch = await svc.RunUsbBatchAsync(req, null, null, CancellationToken.None);

        var t = Assert.Single(batch.Targets);
        Assert.Equal(UsbTargetStatus.Failed, t.Status);
        Assert.Equal(UsbStage.BootConfig, t.FailedStage);
        Assert.Equal(1, t.Result!.BootSectExitCode);
        Assert.False(t.Result.Success);
        Assert.Equal("FAIL", t.Result.UsbCreationStatus);
    }

    // ============================ 3. Verification cannot override a prior failure =================
    [Fact]
    public void Verification_CannotConvertPriorFailureToSuccess()
    {
        using var tmp = new TempDir();
        var (settings, log, paths, _) = Env(tmp);
        var svc = new VerifyExposedDiskService(log, settings, new ScriptedRunner(), new HashService(), paths);

        // A perfectly valid finished volume on disk...
        var vol = tmp.Dir("vol");
        tmp.Dir(@"vol\Boot"); tmp.Dir(@"vol\EFI\Boot"); tmp.Dir(@"vol\Sources");
        tmp.File(@"vol\Sources\boot.wim", "wim");
        tmp.File(@"vol\EFI\Boot\bootx64.efi", "efi");
        tmp.File(@"vol\bootmgr", "bm");

        // ...but an EARLIER stage already failed.
        var result = new UsbCreationResult { Executed = true };
        result.SetStage(UsbStage.Revalidation, UsbStage.Pass);
        result.SetStage(UsbStage.DiskPart, UsbStage.Pass);
        result.SetStage(UsbStage.DriveLetter, UsbStage.Pass);
        result.SetStage(UsbStage.MediaCopy, UsbStage.Pass);
        result.SetStage(UsbStage.BootConfig, UsbStage.Fail, "bootsect exit 1");
        result.Errors.Add("bootsect exit 1");

        svc.RunVerify(vol + "\\", result);

        Assert.NotEqual(UsbStage.Pass, result.StageStatus(UsbStage.Verification));
        Assert.False(result.Success);
    }

    // ============================ 3b. Real verification detects missing boot files ================
    [Fact]
    public void Verification_FailsWhenBootFilesMissing()
    {
        using var tmp = new TempDir();
        var (settings, log, paths, _) = Env(tmp);
        var svc = new VerifyExposedDiskService(log, settings, new ScriptedRunner(), new HashService(), paths);

        var vol = tmp.Dir("vol2");
        tmp.Dir(@"vol2\Boot"); tmp.Dir(@"vol2\EFI\Boot"); tmp.Dir(@"vol2\Sources");
        tmp.File(@"vol2\Sources\boot.wim", "wim");
        // Missing EFI loader and bootmgr.

        var result = new UsbCreationResult { Executed = true };
        foreach (var s in new[] { UsbStage.Revalidation, UsbStage.DiskPart, UsbStage.DriveLetter, UsbStage.MediaCopy, UsbStage.BootConfig })
            result.SetStage(s, UsbStage.Pass);

        svc.RunVerify(vol + "\\", result);

        Assert.Equal(UsbStage.Fail, result.StageStatus(UsbStage.Verification));
        Assert.Contains(result.MissingFiles, m => m.Contains("bootmgr"));
        Assert.Contains(result.MissingFiles, m => m.Contains("UEFI"));
    }

    // ============================ 3c. Missing volume => controlled failure ========================
    [Fact]
    public void Verification_MissingVolume_ProducesControlledFailure()
    {
        using var tmp = new TempDir();
        var (settings, log, paths, _) = Env(tmp);
        var svc = new VerifyExposedDiskService(log, settings, new ScriptedRunner(), new HashService(), paths);

        var result = new UsbCreationResult { Executed = true };
        foreach (var s in new[] { UsbStage.Revalidation, UsbStage.DiskPart, UsbStage.DriveLetter, UsbStage.MediaCopy, UsbStage.BootConfig })
            result.SetStage(s, UsbStage.Pass);

        // No exception — a controlled failure.
        svc.RunVerify(@"Q:\does-not-exist-" + System.Guid.NewGuid().ToString("N") + "\\", result);
        Assert.Equal(UsbStage.Fail, result.StageStatus(UsbStage.Verification));
        Assert.False(result.Success);
    }

    // ============================ 4. DiskPart timeout fails only current target ==================
    [Fact]
    public async Task DiskPartTimeout_FailsOnlyCurrentTarget_BatchContinues()
    {
        using var tmp = new TempDir();
        var (settings, log, paths, media) = Env(tmp);

        int diskpartCall = 0;
        var runner = new ScriptedRunner
        {
            // Time out on the SECOND diskpart invocation (the middle disk) only.
            DiskPart = a => { diskpartCall++; return diskpartCall == 2 ? Res("diskpart.exe", a, -1, timedOut: true) : Ok("diskpart.exe", a); }
        };

        var disks = new[] { RealDisk(1), RealDisk(2), RealDisk(3) };
        var svc = new PipelineDiskService(log, settings, runner, new HashService(), paths, disks);
        var req = new UsbBatchRequest { Targets = disks, MediaSourcePath = media, ConfirmationPhrase = "ERASE 3 DISKS", AcknowledgedDataLoss = true };

        var batch = await svc.RunUsbBatchAsync(req, null, null, CancellationToken.None);

        Assert.Equal(UsbTargetStatus.Success, batch.Targets[0].Status);
        Assert.Equal(UsbTargetStatus.Failed, batch.Targets[1].Status);   // timed out
        Assert.Equal(UsbTargetStatus.Success, batch.Targets[2].Status);  // batch continued
        Assert.Equal(UsbStage.DiskPart, batch.Targets[1].FailedStage);
        Assert.True(batch.Targets[1].Result!.DiskPartTimedOut);
        Assert.Equal(2, batch.Successful);
        Assert.Equal(1, batch.Failed);
    }

    // ============================ 4b. "No volume selected" style diskpart failure ================
    [Fact]
    public async Task DiskPartNonZeroExit_FailsTarget_BatchContinues()
    {
        using var tmp = new TempDir();
        var (settings, log, paths, media) = Env(tmp);

        int call = 0;
        var runner = new ScriptedRunner
        {
            DiskPart = a => { call++; return call == 1 ? Res("diskpart.exe", a, 1, err: "There is no volume selected.") : Ok("diskpart.exe", a); }
        };
        var disks = new[] { RealDisk(11), RealDisk(12) };
        var svc = new PipelineDiskService(log, settings, runner, new HashService(), paths, disks);
        var req = new UsbBatchRequest { Targets = disks, MediaSourcePath = media, ConfirmationPhrase = "ERASE 2 DISKS", AcknowledgedDataLoss = true };

        var batch = await svc.RunUsbBatchAsync(req, null, null, CancellationToken.None);

        Assert.Equal(UsbTargetStatus.Failed, batch.Targets[0].Status);
        Assert.Equal(UsbStage.DiskPart, batch.Targets[0].FailedStage);
        Assert.False(batch.Targets[0].Result!.DiskPartTimedOut); // a plain failure, not a timeout
        Assert.Equal(1, batch.Targets[0].Result!.DiskPartExitCode);
        Assert.Equal(UsbTargetStatus.Success, batch.Targets[1].Status);
    }

    // ============================ 5. Timeout uses the configured setting =========================
    [Fact]
    public async Task DiskPart_UsesConfiguredTimeout_AtLeast15Minutes()
    {
        using var tmp = new TempDir();
        var (settings, log, paths, media) = Env(tmp);
        Assert.True(settings.Settings.DiskPartTimeoutSeconds >= 900); // default is 15 min

        var runner = new ScriptedRunner();
        var disk = RealDisk(8);
        var svc = new PipelineDiskService(log, settings, runner, new HashService(), paths, new[] { disk });
        var result = await svc.CreateSingleUsbAsync(disk, media, "WINFE", null, CancellationToken.None);

        Assert.Equal(settings.Settings.DiskPartTimeoutSeconds, result.DiskPartTimeoutSeconds);
    }

    // ============================ 6. JSON record reflects the real final result ==================
    [Fact]
    public async Task JsonRecord_ReflectsFailure_OnBootSectFailure()
    {
        using var tmp = new TempDir();
        var (settings, log, paths, media) = Env(tmp);
        var runner = new ScriptedRunner { BootSect = a => Res("bootsect.exe", a, 1, err: "boom") };
        var disk = RealDisk(8);
        var svc = new PipelineDiskService(log, settings, runner, new HashService(), paths, new[] { disk });

        await svc.CreateSingleUsbAsync(disk, media, "WINFE", null, CancellationToken.None);

        var recordFile = Directory.EnumerateFiles(paths.ReportDir, "usb-record_*.json").OrderBy(f => f).LastOrDefault();
        Assert.NotNull(recordFile);
        var rec = JsonSerializer.Deserialize<UsbRecord>(File.ReadAllText(recordFile!))!;

        Assert.Equal("FAILED", rec.FinalStatus);
        Assert.Equal(UsbStage.BootConfig, rec.FailedStage);
        Assert.Equal(1, rec.BootSectExitCode);
        Assert.Equal(8, rec.DiskNumber);
        Assert.Equal("R-8", rec.SerialNumber);
        Assert.Equal("K:", rec.AssignedDriveLetter);
        Assert.NotEmpty(rec.Stages);
    }

    [Fact]
    public async Task JsonRecord_ReflectsSuccess_WhenAllStagesPass()
    {
        using var tmp = new TempDir();
        var (settings, log, paths, media) = Env(tmp);
        var svc = new PipelineDiskService(log, settings, new ScriptedRunner(), new HashService(), paths, new[] { RealDisk(8) });

        var result = await svc.CreateSingleUsbAsync(RealDisk(8), media, "WINFE", null, CancellationToken.None);
        Assert.True(result.Success);

        var recordFile = Directory.EnumerateFiles(paths.ReportDir, "usb-record_*.json").OrderBy(f => f).Last();
        var rec = JsonSerializer.Deserialize<UsbRecord>(File.ReadAllText(recordFile))!;
        Assert.Equal("SUCCESS", rec.FinalStatus);
        Assert.Null(rec.FailedStage);
    }
}
