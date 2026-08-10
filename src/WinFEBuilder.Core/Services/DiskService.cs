using System.Management;
using System.Text.Json;
using WinFEBuilder.Core.Configuration;
using WinFEBuilder.Core.Hashing;
using WinFEBuilder.Core.Logging;
using WinFEBuilder.Core.Models;
using WinFEBuilder.Core.Validation;

namespace WinFEBuilder.Core.Services;

/// <summary>
/// Enumerates disks and performs (or, in simulation mode, only plans) USB creation. All destructive
/// paths are gated behind identity re-verification, eligibility rules, an exact confirmation phrase,
/// an acknowledgement, and simulation mode. Enumeration is strictly read-only.
/// </summary>
public class DiskService : IDiskService
{
    private const string StorageScope = @"\\.\root\Microsoft\Windows\Storage";

    /// <summary>Fallback DiskPart timeout (seconds) when the setting is missing/invalid: 15 minutes.</summary>
    public const int DefaultDiskPartTimeoutSeconds = 900;

    /// <summary>Boot-sector (bootsect.exe) timeout in milliseconds.</summary>
    private const int BootSectTimeoutMs = 120_000;

    private readonly ILogService _log;
    private readonly ISettingsService _settings;
    private readonly IProcessRunner _runner;
    private readonly IHashService _hash;
    private readonly AppPaths _paths;

    public DiskService(ILogService log, ISettingsService settings, IProcessRunner runner, IHashService hash, AppPaths paths)
    {
        _log = log;
        _settings = settings;
        _runner = runner;
        _hash = hash;
        _paths = paths;
    }

    public bool SimulationMode => _settings.Settings.SimulationMode;

    public async Task<List<DiskInfo>> EnumerateDisksAsync(bool includeNonRemovable, CancellationToken ct = default)
    {
        var disks = await Task.Run(() => QueryDisks(includeNonRemovable), ct).ConfigureAwait(false);

        if (SimulationMode)
        {
            disks.AddRange(GetSimulatedDisks());
            _log.Info("USB", "Simulation mode ON — added fake demo disk(s). No disk will be modified.");
        }

        _log.Info("USB", $"Enumerated {disks.Count} disk(s) (includeNonRemovable={includeNonRemovable}).");
        return disks;
    }

    public virtual Task<DiskInfo?> RefreshDiskAsync(int number, CancellationToken ct = default)
        => Task.Run(() =>
        {
            // Include non-removable so a swapped/changed disk is still seen for identity comparison.
            var all = QueryDisks(includeNonRemovable: true);
            return all.FirstOrDefault(d => d.Number == number);
        }, ct);

    public DiskEligibility Evaluate(DiskInfo disk, bool allowNonRemovable)
        => DiskEligibilityRules.Evaluate(disk, BuildProtectedContext(), allowNonRemovable);

    public ProtectedContext BuildProtectedContext()
    {
        var ctx = new ProtectedContext();

        // System / Windows drive.
        ctx.Protect(Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.System)), "Windows system volume");
        ctx.Protect(Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.Windows)), "Windows directory");
        ctx.Protect(Environment.GetEnvironmentVariable("SystemDrive"), "system drive");

        // Page files.
        foreach (var pf in QueryPageFileDrives())
            ctx.Protect(pf, "page file");

        // Hibernation / crash dump live on the system drive (already protected) — note explicitly.
        ctx.Protect(Environment.GetEnvironmentVariable("SystemDrive"), "hibernation / crash dump");

        // Application-managed locations.
        ctx.Protect(Path.GetPathRoot(_settings.Settings.WorkspaceRoot), "application workspace");
        ctx.Protect(Path.GetPathRoot(_settings.Settings.OutputRoot), "generated ISO output");
        ctx.Protect(Path.GetPathRoot(_paths.BaseDir), "application folder");
        if (!string.IsNullOrWhiteSpace(_settings.Settings.LastFrameworkPath))
            ctx.Protect(Path.GetPathRoot(_settings.Settings.LastFrameworkPath), "source framework");

        return ctx;
    }

    // ---------------------------------------------------------------------
    // Enumeration (read-only WMI Storage namespace)
    // ---------------------------------------------------------------------

    private List<DiskInfo> QueryDisks(bool includeNonRemovable)
    {
        var result = new List<DiskInfo>();
        try
        {
            var scope = new ManagementScope(StorageScope);
            scope.Connect();

            var volumesByLetter = QueryVolumeFileSystems(scope);
            var partitionsByDisk = QueryPartitions(scope, out var partitionsReliable);

            using var searcher = new ManagementObjectSearcher(scope, new ObjectQuery("SELECT * FROM MSFT_Disk"));
            foreach (ManagementObject mo in searcher.Get())
            {
                var number = ToInt(mo["Number"]);
                var busType = MapBusType(ToInt(mo["BusType"]));
                var removable = busType is "USB" or "SD" or "MMC";

                if (!removable && !includeNonRemovable) continue;

                partitionsByDisk.TryGetValue(number, out var letters);
                letters ??= new List<string>();
                var fileSystems = letters
                    .Select(l => volumesByLetter.TryGetValue(l, out var fs) ? fs : null)
                    .Where(fs => fs is not null)
                    .Select(fs => fs!)
                    .ToList();

                result.Add(new DiskInfo
                {
                    Number = number,
                    FriendlyName = mo["FriendlyName"] as string,
                    Manufacturer = mo["Manufacturer"] as string,
                    Model = mo["Model"] as string,
                    SerialNumber = (mo["SerialNumber"] as string)?.Trim(),
                    UniqueId = mo["UniqueId"] as string,
                    BusType = busType,
                    SizeBytes = ToLong(mo["Size"]),
                    PartitionCount = ToInt(mo["NumberOfPartitions"]),
                    DriveLetters = letters,
                    FileSystems = fileSystems,
                    IsOffline = ToBool(mo["IsOffline"]),
                    IsReadOnly = ToBool(mo["IsReadOnly"]),
                    HealthStatus = MapHealth(ToInt(mo["HealthStatus"])),
                    IsRemovable = removable,
                    IsSystemDisk = ToBool(mo["IsSystem"]),
                    IsBootDisk = ToBool(mo["IsBoot"]),
                    // If partitions couldn't be enumerated, DriveLetters are untrustworthy → mark unreliable
                    // so the eligibility gate refuses this disk rather than assuming it hosts nothing protected.
                    PartitionInfoReliable = partitionsReliable
                });
            }
        }
        catch (Exception ex)
        {
            _log.Error("USB", "Disk enumeration failed (Storage WMI).", ex);
        }

        return result.OrderBy(d => d.Number).ToList();
    }

    private Dictionary<int, List<string>> QueryPartitions(ManagementScope scope, out bool reliable)
    {
        var map = new Dictionary<int, List<string>>();
        try
        {
            using var searcher = new ManagementObjectSearcher(scope, new ObjectQuery("SELECT DiskNumber, DriveLetter FROM MSFT_Partition"));
            foreach (ManagementObject mo in searcher.Get())
            {
                var disk = ToInt(mo["DiskNumber"]);
                var letterChar = mo["DriveLetter"];
                var letter = LetterFromValue(letterChar);
                if (!map.TryGetValue(disk, out var list)) map[disk] = list = new List<string>();
                if (letter is not null) list.Add(letter);
            }
            reliable = true;
        }
        catch (Exception ex)
        {
            // Fail closed: without partition data we cannot know which disks host protected volumes.
            reliable = false;
            _log.Warning("USB", $"Partition enumeration failed ({ex.Message}) — disks will be treated as unverifiable and refused.",
                "Re-scan disks and ensure the app is running as Administrator.");
        }
        return map;
    }

    private Dictionary<string, string> QueryVolumeFileSystems(ManagementScope scope)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var searcher = new ManagementObjectSearcher(scope, new ObjectQuery("SELECT DriveLetter, FileSystem FROM MSFT_Volume"));
            foreach (ManagementObject mo in searcher.Get())
            {
                var letter = LetterFromValue(mo["DriveLetter"]);
                var fs = mo["FileSystem"] as string;
                if (letter is not null && !string.IsNullOrWhiteSpace(fs))
                    map[letter] = fs;
            }
        }
        catch (Exception ex)
        {
            _log.Debug("USB", $"Volume query failed: {ex.Message}");
        }
        return map;
    }

    private IEnumerable<string> QueryPageFileDrives()
    {
        var drives = new List<string>();
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Name FROM Win32_PageFileUsage");
            foreach (ManagementObject mo in searcher.Get())
            {
                var name = mo["Name"] as string; // e.g. "C:\\pagefile.sys"
                var root = string.IsNullOrWhiteSpace(name) ? null : Path.GetPathRoot(name);
                if (root is not null) drives.Add(root);
            }
        }
        catch (Exception ex)
        {
            _log.Debug("USB", $"Page-file query failed: {ex.Message}");
        }
        return drives;
    }

    // ---------------------------------------------------------------------
    // USB creation (guarded; simulation-first)
    // ---------------------------------------------------------------------

    /// <summary>
    /// Single-USB creation with the per-disk destructive-confirmation phrase + acknowledgement.
    /// Kept for the single-target path and existing tests; delegates to the shared core.
    /// </summary>
    public async Task<UsbCreationResult> CreateUsbAsync(UsbBuildRequest request, IProgress<string>? progress, CancellationToken ct)
    {
        var result = new UsbCreationResult();

        if (!Directory.Exists(request.MediaSourcePath) || !IsValidMediaRoot(request.MediaSourcePath))
            return FailFast(result, "Media source is not a valid, bootable WinFE media folder (need Boot, EFI, Sources and a boot.wim).",
                "Select the deployable media root produced by the Build page (e.g. the USB\\x86-x64 folder).");

        if (!ConfirmationPhraseValidator.IsValid(request.ConfirmationPhrase, request.SelectedDisk.Number))
            return FailFast(result, $"Confirmation phrase must be exactly 'ERASE DISK {request.SelectedDisk.Number}'.",
                "Type the exact phrase to confirm.");
        if (!request.AcknowledgedDataLoss)
            return FailFast(result, "Data-loss acknowledgement is required.", "Tick the acknowledgement checkbox.");

        try
        {
            return await CreateSingleUsbAsync(request.SelectedDisk, request.MediaSourcePath, request.Label, progress, ct, request.AllowFixedDisk).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return FailFast(result, "USB creation canceled.", "Re-run when ready.");
        }
    }

    /// <summary>
    /// Shared core that creates one WinFE USB on a supplied target disk. Assumes the destructive
    /// confirmation has already been satisfied by the caller (single phrase, or batch phrase). Still
    /// enforces all safety re-checks (identity, eligibility, protected disks) and simulation mode.
    /// Throws <see cref="OperationCanceledException"/> on cancellation so batch/single callers decide
    /// how to record it.
    /// </summary>
    public virtual async Task<UsbCreationResult> CreateSingleUsbAsync(DiskInfo selected, string mediaSourcePath, string label, IProgress<string>? progress, CancellationToken ct, bool allowFixedDisk = false)
    {
        var result = new UsbCreationResult { StartTime = DateTimeOffset.Now };
        void Report(string m) { progress?.Report(m); _log.Info("USB", m); }

        // Media source must be a valid, bootable WinFE media root (simple or combined multi-arch).
        if (!Directory.Exists(mediaSourcePath) || !IsValidMediaRoot(mediaSourcePath))
            return FailFast(result, "Media source is not a valid, bootable WinFE media folder (need Boot, EFI, Sources and a boot.wim).",
                "Select the deployable media root produced by the Build page (e.g. the USB\\x86-x64 folder).");

        // Revalidate the target's identity immediately before anything else. Disk numbers are unstable,
        // so we compare the full identity signature, never the number alone.
        Report($"Revalidating target — disk {selected.Number}…");
        var current = selected.IsSimulated ? selected : await RefreshDiskAsync(selected.Number, ct).ConfigureAwait(false);
        if (current is null)
        {
            result.RecommendedAction = "Re-scan disks and try again.";
            return FailStage(result, UsbStage.Revalidation, $"Disk {selected.Number} is no longer present.");
        }
        if (!DiskIdentity.Matches(selected, current))
        {
            var diffs = string.Join("; ", DiskIdentity.Differences(selected, current));
            result.RecommendedAction = "Re-scan disks and re-select the correct device. Aborted for safety.";
            return FailStage(result, UsbStage.Revalidation, $"Disk identity changed since selection ({diffs}).");
        }

        // Eligibility (protected-disk rules) re-evaluated now. The removable gate is driven by the
        // operator's EXPLICIT opt-in (allowFixedDisk), never inferred from the disk's own removability —
        // otherwise a fixed disk would silently satisfy its own gate.
        var eligibility = Evaluate(current, allowNonRemovable: allowFixedDisk);
        if (!eligibility.CanTarget)
        {
            result.RecommendedAction = "Choose a removable, non-protected disk.";
            return FailStage(result, UsbStage.Revalidation, $"Disk {current.Number} is not an eligible target: {eligibility.BlockSummary}");
        }
        result.SetStage(UsbStage.Revalidation, UsbStage.Pass);

        result.DiskPartScript = DiskPartScriptBuilder.Build(current.Number, label);

        // Simulation mode / simulated disk — plan only, never execute a destructive command.
        if (SimulationMode || current.IsSimulated)
        {
            result.Simulated = true;
            result.Executed = false;
            result.UsbCreationStatus = "SIMULATED";
            result.BootStructureStatus = "NOT TESTED";
            result.OfflineStructuralValidationStatus = "NOT TESTED";
            result.RecommendedAction = "Simulation mode is ON. Set SimulationMode=false in settings.json to enable real writes.";
            Report("SIMULATION: DiskPart script generated but NOT executed. No disk was modified.");
            return result;
        }

        // ---- REAL EXECUTION (only when all guards pass and simulation is OFF) ----

        // Stage: DiskPart preparation (clean/partition/format) with a configurable timeout.
        int timeoutSeconds = _settings.Settings.DiskPartTimeoutSeconds > 0
            ? _settings.Settings.DiskPartTimeoutSeconds
            : DefaultDiskPartTimeoutSeconds;
        result.DiskPartTimeoutSeconds = timeoutSeconds;

        Report($"Preparing disk {current.Number} with DiskPart (clean/partition/format). Configured timeout: {timeoutSeconds}s.");
        Report("DiskPart script:");
        foreach (var line in result.DiskPartScript.Split('\n'))
        {
            var cmd = line.Trim();
            if (cmd.Length > 0) Report("  diskpart> " + cmd);
        }

        var scriptFile = Path.Combine(Path.GetTempPath(), $"winfe_diskpart_{Guid.NewGuid():N}.txt");
        await File.WriteAllTextAsync(scriptFile, result.DiskPartScript, ct).ConfigureAwait(false);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var dp = await _runner.RunAsync("diskpart.exe", new[] { "/s", scriptFile },
                timeoutMs: timeoutSeconds * 1000,
                onOutputLine: ProcessOutputFilter.Wrap(l => _log.Debug("USB", l)),
                onErrorLine: l => _log.Warning("USB", l),
                ct: ct).ConfigureAwait(false);
            sw.Stop();
            result.DiskPartCommand = dp.CommandLine;
            result.DiskPartExitCode = dp.ExitCode;
            result.DiskPartOutput = dp.StandardOutput + dp.StandardError;
            result.DiskPartTimedOut = dp.TimedOut;
            result.DiskPartElapsedSeconds = sw.Elapsed.TotalSeconds;
            Report($"DiskPart finished after {sw.Elapsed.TotalSeconds:F0}s of {timeoutSeconds}s "
                   + $"(exit {dp.ExitCode}{(dp.TimedOut ? ", TIMED OUT" : "")}).");

            if (dp.TimedOut)
            {
                // A timeout is distinct from a normal nonzero exit: the process was terminated.
                result.RecommendedAction = "The DiskPart process was terminated after exceeding the configured timeout. "
                    + "Increase DiskPartTimeoutSeconds or use a faster/known-good USB device. Not retried automatically.";
                MarkStageFailed(result, UsbStage.DiskPart,
                    $"DiskPart timed out after {timeoutSeconds}s and was terminated.");
                return FinalizeReal(result, current, mediaSourcePath, label, Report);
            }
            if (dp.ExitCode != 0)
            {
                result.RecommendedAction = "Review the DiskPart output; the disk may be in use.";
                MarkStageFailed(result, UsbStage.DiskPart, $"DiskPart failed (exit {dp.ExitCode}).");
                return FinalizeReal(result, current, mediaSourcePath, label, Report);
            }
            result.SetStage(UsbStage.DiskPart, UsbStage.Pass, $"{sw.Elapsed.TotalSeconds:F0}s");
        }
        finally
        {
            try { File.Delete(scriptFile); } catch { /* ignore */ }
        }

        // Stage: drive-letter detection (validate the assigned volume actually exists).
        Report("Detecting the assigned drive letter…");
        var letter = await DetectDriveLetterAsync(current.Number, label, ct).ConfigureAwait(false);
        if (letter is null)
        {
            result.RecommendedAction = "Check Disk Management; the format may have partially completed.";
            MarkStageFailed(result, UsbStage.DriveLetter, "Could not detect the formatted volume's drive letter.");
            return FinalizeReal(result, current, mediaSourcePath, label, Report);
        }
        var driveTarget = DriveLetterNormalizer.Normalize(letter);  // canonical "K:"
        var driveRoot = DriveLetterNormalizer.Root(letter);         // canonical "K:\"
        result.AssignedDriveLetter = driveTarget;
        result.SetStage(UsbStage.DriveLetter, UsbStage.Pass, driveTarget);
        Report($"Assigned drive letter: {driveTarget}");

        // Stage: media copy.
        Report($"Copying WinFE media to {driveRoot} …");
        try
        {
            var (files, bytes) = CopyTree(mediaSourcePath, driveRoot, ct);
            result.FilesCopied = files;
            result.BytesCopied = bytes;
            result.SetStage(UsbStage.MediaCopy, UsbStage.Pass, $"{files} files, {bytes:N0} bytes");
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            result.RecommendedAction = "Check the destination volume and free space, then retry.";
            MarkStageFailed(result, UsbStage.MediaCopy, $"Media copy failed: {ex.Message}");
            return FinalizeReal(result, current, mediaSourcePath, label, Report);
        }

        // Stage: boot configuration (REQUIRED — a nonzero bootsect exit fails the disk).
        Report("Configuring boot sector (bootsect)…");
        await ConfigureBootSectorAsync(driveTarget, mediaSourcePath, result, ct).ConfigureAwait(false);
        if (result.StageStatus(UsbStage.BootConfig) != UsbStage.Pass)
            return FinalizeReal(result, current, mediaSourcePath, label, Report);

        // Stage: file verification (never converts a failed result into success).
        Report("Verifying the finished volume…");
        VerifyMedia(driveRoot, result, ct);

        return FinalizeReal(result, current, mediaSourcePath, label, Report);
    }

    /// <summary>Compute the final status, persist the per-disk record, and log honestly. Only logs a
    /// completion/success message when EVERY mandatory stage passed.</summary>
    private UsbCreationResult FinalizeReal(UsbCreationResult result, DiskInfo disk, string mediaSourcePath, string label, Action<string> report)
    {
        result.Executed = true;
        result.EndTime = DateTimeOffset.Now;
        bool ok = result.Errors.Count == 0 && result.AllMandatoryStagesPassed();
        result.UsbCreationStatus = ok ? "PASS" : "FAIL";
        PersistUsbRecord(disk, mediaSourcePath, label, result);
        report(ok
            ? "USB creation completed."
            : $"USB creation FAILED at stage: {result.FailedStage ?? "unknown"}.");
        return result;
    }

    /// <summary>
    /// Write the BIOS boot code with bootsect.exe. This is a REQUIRED stage: a missing executable, a
    /// timeout, or any nonzero exit marks the disk failed (FailedStage = Boot configuration) and stores
    /// the executable, arguments, exit code and error output.
    /// </summary>
    protected virtual async Task ConfigureBootSectorAsync(string driveLetter, string mediaSource, UsbCreationResult result, CancellationToken ct)
    {
        var target = DriveLetterNormalizer.Normalize(driveLetter);  // "K:" (never "K::")
        var candidates = new[]
        {
            Path.Combine(mediaSource, "boot", "bootsect.exe"),
            Path.Combine(DriveLetterNormalizer.Root(target), "boot", "bootsect.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "bootsect.exe")
        };
        var bootsect = candidates.FirstOrDefault(File.Exists);
        if (bootsect is null)
        {
            result.RecommendedAction = "Provide bootsect.exe (ships with the ADK/WinPE) so the BIOS boot code can be written.";
            MarkStageFailed(result, UsbStage.BootConfig, "bootsect.exe not found — cannot write the BIOS boot code.");
            return;
        }

        var args = new[] { "/nt60", target, "/force", "/mbr" };
        result.BootSectExecutable = bootsect;
        result.BootSectArguments = string.Join(' ', args);
        _log.Info("USB", $"Running bootsect: {bootsect} {result.BootSectArguments}");

        var run = await _runner.RunAsync(bootsect, args,
            timeoutMs: BootSectTimeoutMs,
            onOutputLine: ProcessOutputFilter.Wrap(l => _log.Debug("USB", l)),
            onErrorLine: l => _log.Warning("USB", l),
            ct: ct).ConfigureAwait(false);

        result.BootSectExitCode = run.ExitCode;
        result.BootSectTimedOut = run.TimedOut;

        if (run.TimedOut)
        {
            result.BootSectError = $"bootsect timed out after {BootSectTimeoutMs / 1000}s and was terminated.";
            result.RecommendedAction = "Retry with a known-good device; bootsect did not complete.";
            MarkStageFailed(result, UsbStage.BootConfig, result.BootSectError);
            return;
        }
        if (run.ExitCode != 0)
        {
            result.BootSectError = string.IsNullOrWhiteSpace(run.StandardError) ? run.StandardOutput : run.StandardError;
            result.RecommendedAction = "Review the bootsect output; the volume may not be writable or the device may be BIOS-incompatible.";
            MarkStageFailed(result, UsbStage.BootConfig,
                $"bootsect failed (exit {run.ExitCode}): {bootsect} {result.BootSectArguments}");
            return;
        }
        result.SetStage(UsbStage.BootConfig, UsbStage.Pass);
    }

    /// <summary>
    /// Create WinFE USBs on multiple targets SEQUENTIALLY (never in parallel), reusing the same
    /// validated media for each. Per-disk failures are recorded and do not stop the batch; only a
    /// global problem (bad media, wrong phrase, missing acknowledgement, cancellation) stops it.
    /// </summary>
    public async Task<UsbBatchResult> RunUsbBatchAsync(UsbBatchRequest request, IProgress<string>? log, IProgress<UsbBatchProgress>? batch, CancellationToken ct)
    {
        var result = new UsbBatchResult { Simulated = SimulationMode };
        void Log(string m) { log?.Report(m); _log.Info("USB", m); }

        // ---- Global validation (any failure aborts the whole batch before touching a disk) ----
        if (request.Targets is null || request.Targets.Count == 0)
            return GlobalAbort(result, "No disks selected.");
        if (!Directory.Exists(request.MediaSourcePath) || !IsValidMediaRoot(request.MediaSourcePath))
            return GlobalAbort(result, "The WinFE media source is missing or invalid. Run a Build first.");

        var diskNumbers = request.Targets.Select(t => t.Number).ToList();
        if (!BatchConfirmationValidator.IsValid(request.ConfirmationPhrase, diskNumbers))
            return GlobalAbort(result, $"Confirmation phrase must be exactly '{BatchConfirmationValidator.Expected(diskNumbers)}'.");
        if (!request.AcknowledgedDataLoss)
            return GlobalAbort(result, "Data-loss acknowledgement is required.");

        foreach (var t in request.Targets)
            result.Targets.Add(new UsbTargetResult { DiskNumber = t.Number, Describe = t.Describe(), SerialNumber = t.SerialNumber });

        int total = request.Targets.Count, index = 0, success = 0, failed = 0;
        foreach (var target in request.Targets)
        {
            index++;
            var tr = result.Targets[index - 1];

            // Do not start the next disk after a cancellation request.
            if (ct.IsCancellationRequested)
            {
                tr.Status = UsbTargetStatus.Canceled;
                continue;
            }

            Log("");
            Log("========================================");
            Log($"USB {index} of {total} — Disk {target.Number} — {target.FriendlyName ?? target.Model ?? "Unknown"}");
            Log("========================================");
            batch?.Report(new UsbBatchProgress { Current = index, Total = total, Disk = target, Stage = "Starting", Successful = success, Failed = failed });

            tr.Status = UsbTargetStatus.Running;
            tr.StartTime = DateTimeOffset.Now;
            try
            {
                var single = await CreateSingleUsbAsync(target, request.MediaSourcePath, request.Label, log, ct, request.AllowFixedDisk).ConfigureAwait(false);
                tr.Result = single;
                tr.EndTime = DateTimeOffset.Now;

                if (single.Simulated)
                {
                    tr.Status = UsbTargetStatus.Simulated; success++;
                    Log("SIMULATED");
                }
                else if (single.Success)
                {
                    tr.Status = UsbTargetStatus.Success; success++;
                    Log("SUCCESS");
                }
                else
                {
                    tr.Status = UsbTargetStatus.Failed;
                    tr.ErrorMessage = single.Errors.FirstOrDefault() ?? "USB creation failed.";
                    tr.FailedStage = single.FailedStage;
                    failed++;
                    Log("FAILED: " + tr.ErrorMessage);
                }
            }
            catch (OperationCanceledException)
            {
                // Cancellation while this disk was in progress: record it and stop (the loop's next
                // iteration sees the cancellation and marks any remaining disks Canceled).
                tr.Status = UsbTargetStatus.Canceled;
                tr.EndTime = DateTimeOffset.Now;
                Log("CANCELED");
            }
            catch (Exception ex)
            {
                // Per-disk failure: record and CONTINUE to the next disk.
                tr.Status = UsbTargetStatus.Failed;
                tr.ErrorMessage = ex.Message;
                tr.EndTime = DateTimeOffset.Now;
                failed++;
                _log.Error("USB", $"Disk {target.Number} failed.", ex);
                Log("FAILED: " + ex.Message);
            }

            batch?.Report(new UsbBatchProgress { Current = index, Total = total, Disk = target, Stage = tr.StatusLine, Successful = success, Failed = failed });
        }

        Log("");
        Log(result.SummaryText());
        return result;
    }

    private static UsbBatchResult GlobalAbort(UsbBatchResult r, string error)
    {
        r.GlobalAbort = true;
        r.GlobalError = error;
        return r;
    }

    /// <summary>Record a per-stage failure on the result and return it (for early returns).</summary>
    private UsbCreationResult FailStage(UsbCreationResult r, string stage, string message)
    {
        MarkStageFailed(r, stage, message);
        return r;
    }

    /// <summary>Mark a stage failed: set FailedStage (first failure wins), record the stage FAIL with
    /// its message, add the error, and set the overall status to FAIL.</summary>
    private void MarkStageFailed(UsbCreationResult r, string stage, string message)
    {
        r.FailedStage ??= stage;
        r.SetStage(stage, UsbStage.Fail, message);
        r.Errors.Add(message);
        r.UsbCreationStatus = "FAIL";
        _log.Fail("USB", $"[{stage}] {message}");
    }

    protected virtual async Task<string?> DetectDriveLetterAsync(int diskNumber, string label, CancellationToken ct)
    {
        for (int i = 0; i < 10; i++)
        {
            ct.ThrowIfCancellationRequested();
            var disk = await RefreshDiskAsync(diskNumber, ct).ConfigureAwait(false);
            var letter = disk?.DriveLetters.FirstOrDefault();
            if (letter is not null) return letter.TrimEnd('\\');
            await Task.Delay(1000, ct).ConfigureAwait(false);
        }
        return null;
    }

    protected virtual (int files, long bytes) CopyTree(string source, string destRoot, CancellationToken ct)
    {
        int files = 0; long bytes = 0;
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            ct.ThrowIfCancellationRequested();
            var rel = Path.GetRelativePath(source, file);
            var dest = Path.Combine(destRoot, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            File.Copy(file, dest, overwrite: true);
            files++;
            try { bytes += new FileInfo(dest).Length; } catch { }
        }
        return (files, bytes);
    }

    /// <summary>
    /// Final verification. A USB is NEVER successful just because files were copied: this confirms the
    /// destination volume exists, the boot skeleton is present, boot.wim exists, the UEFI loader and the
    /// BIOS boot manager exist, and that every prior mandatory stage passed. It can only ever DEMOTE a
    /// result — it never converts a failed result into success.
    /// </summary>
    protected virtual void VerifyMedia(string driveRoot, UsbCreationResult result, CancellationToken ct)
    {
        // Guard: verification presupposes the earlier mandatory stages. If any already failed, do not
        // let a passing structural check paper over it.
        var priors = new[] { UsbStage.Revalidation, UsbStage.DiskPart, UsbStage.DriveLetter, UsbStage.MediaCopy, UsbStage.BootConfig };
        var failedPrior = priors.FirstOrDefault(p => result.StageStatus(p) != UsbStage.Pass);
        if (failedPrior is not null)
        {
            result.BootStructureStatus = "FAIL";
            result.OfflineStructuralValidationStatus = "FAIL";
            MarkStageFailed(result, UsbStage.Verification, $"Verification skipped — a prior stage did not pass ({failedPrior}).");
            return;
        }

        // Destination volume must exist.
        if (!Directory.Exists(driveRoot))
        {
            result.MissingFiles.Add("(destination volume)");
            result.BootStructureStatus = "FAIL";
            result.OfflineStructuralValidationStatus = "FAIL";
            MarkStageFailed(result, UsbStage.Verification, $"Destination volume {driveRoot} does not exist.");
            return;
        }

        var missing = new List<string>();
        void NeedDir(string rel) { if (!Directory.Exists(Path.Combine(driveRoot, rel))) missing.Add(rel + "\\"); }
        void NeedFile(string rel, string label) { if (!File.Exists(Path.Combine(driveRoot, rel))) missing.Add(label); }

        // Boot skeleton.
        NeedDir("Boot"); NeedDir("EFI"); NeedDir("Sources");

        // boot.wim (anywhere under a sources folder — handles combined multi-arch layouts).
        var bootWims = SafeEnumerate(driveRoot, "boot.wim");
        if (bootWims.Count == 0) missing.Add(@"Sources\boot.wim");

        // UEFI boot loader (any architecture).
        var efiLoaders = new[] { @"EFI\Boot\bootx64.efi", @"EFI\Boot\bootia32.efi", @"EFI\Boot\bootaa64.efi" };
        if (!efiLoaders.Any(r => File.Exists(Path.Combine(driveRoot, r))))
            missing.Add(@"EFI\Boot\boot*.efi (UEFI loader)");

        // BIOS boot manager — this layout formats MBR + marks the partition active, so bootmgr is required.
        NeedFile("bootmgr", "bootmgr (BIOS boot manager)");

        result.MissingFiles.AddRange(missing);
        bool ok = missing.Count == 0;
        result.BootStructureStatus = ok ? "PASS" : "FAIL";
        result.OfflineStructuralValidationStatus = ok ? "PASS" : "FAIL";

        // Critical file hashes: every boot.wim found, plus boot managers / BCD if present.
        var criticalRel = new List<string> { "bootmgr", "bootmgr.efi", @"efi\microsoft\boot\bcd", @"boot\bcd" };
        var criticalFull = criticalRel.Select(r => Path.Combine(driveRoot, r)).Concat(bootWims);
        foreach (var full in criticalFull)
        {
            if (!File.Exists(full)) continue;
            try
            {
                var entry = _hash.ComputeEntryAsync(full, driveRoot, ct).GetAwaiter().GetResult();
                result.CriticalHashes.Add(entry);
            }
            catch (Exception ex) { _log.Debug("USB", $"Hash failed {full}: {ex.Message}"); }
        }

        if (!ok)
        {
            result.RecommendedAction = "The finished volume is missing required boot files; re-create the USB.";
            MarkStageFailed(result, UsbStage.Verification, "Verification failed — missing: " + string.Join(", ", missing));
            return;
        }
        result.SetStage(UsbStage.Verification, UsbStage.Pass);
    }

    /// <summary>Persist a UsbRecord to the reports directory so build reports can include USB details.</summary>
    private void PersistUsbRecord(DiskInfo disk, string mediaSourcePath, string label, UsbCreationResult result)
    {
        try
        {
            var finalStatus = result.Simulated ? "SIMULATED" : (result.Success ? "SUCCESS" : "FAILED");
            var record = new UsbRecord
            {
                DiskNumber = disk.Number,
                Model = disk.Model,
                SerialNumber = disk.SerialNumber,
                UniqueId = disk.UniqueId,
                BusType = disk.BusType,
                CapacityBytes = disk.SizeBytes,
                AssignedDriveLetter = result.AssignedDriveLetter,
                MediaSourcePath = mediaSourcePath,
                Label = label,
                StartTime = result.StartTime,
                EndTime = result.EndTime,
                FilesCopied = result.FilesCopied,
                BytesCopied = result.BytesCopied,
                DiskPartCommand = result.DiskPartCommand,
                DiskPartExitCode = result.DiskPartExitCode,
                DiskPartTimedOut = result.DiskPartTimedOut,
                BootSectCommand = result.BootSectExecutable is null ? null : $"{result.BootSectExecutable} {result.BootSectArguments}",
                BootSectExitCode = result.BootSectExitCode,
                BootSectTimedOut = result.BootSectTimedOut,
                BootSectError = result.BootSectError,
                FailedStage = result.FailedStage,
                ErrorMessage = result.Errors.FirstOrDefault(),
                UsbCreationStatus = result.UsbCreationStatus,
                BootStructureStatus = result.BootStructureStatus,
                OfflineStructuralValidationStatus = result.OfflineStructuralValidationStatus,
                FinalStatus = finalStatus,
            };
            record.Stages.AddRange(result.Stages.Select(s => new StageResult { Name = s.Name, Status = s.Status, Detail = s.Detail }));
            record.MissingFiles.AddRange(result.MissingFiles);
            record.CriticalHashes.AddRange(result.CriticalHashes);
            record.Warnings.AddRange(result.Warnings);

            Directory.CreateDirectory(_paths.ReportDir);
            var path = Path.Combine(_paths.ReportDir, $"usb-record_{DateTime.Now:yyyy-MM-dd_HHmmss}.json");
            File.WriteAllText(path, JsonSerializer.Serialize(record, new JsonSerializerOptions { WriteIndented = true }));
            _log.Info("USB", $"Wrote USB record: {path}");
        }
        catch (Exception ex)
        {
            _log.Debug("USB", $"Could not write USB record: {ex.Message}");
        }
    }

    /// <summary>A media root is valid if it has the Boot/EFI/Sources skeleton and a boot.wim under it.</summary>
    private static bool IsValidMediaRoot(string root)
    {
        var skeleton = Directory.Exists(Path.Combine(root, "Boot"))
                       && Directory.Exists(Path.Combine(root, "EFI"))
                       && Directory.Exists(Path.Combine(root, "Sources"));
        return skeleton && SafeEnumerate(root, "boot.wim").Count > 0;
    }

    private static List<string> SafeEnumerate(string root, string fileName)
    {
        try { return Directory.EnumerateFiles(root, fileName, SearchOption.AllDirectories).ToList(); }
        catch { return new List<string>(); }
    }

    private UsbCreationResult FailFast(UsbCreationResult r, string message, string? action)
    {
        r.Errors.Add(message);
        r.RecommendedAction ??= action;
        r.UsbCreationStatus = "FAIL";
        _log.Fail("USB", message);
        return r;
    }

    // ---------------------------------------------------------------------
    // Simulation + WMI helpers
    // ---------------------------------------------------------------------

    private static IEnumerable<DiskInfo> GetSimulatedDisks() => new[]
    {
        new DiskInfo
        {
            Number = 99,
            FriendlyName = "SIMULATED USB Flash Drive",
            Manufacturer = "WinFEBuilder",
            Model = "DemoStick 32GB",
            SerialNumber = "SIM-0000-0001",
            UniqueId = "SIM-UNIQUE-0001",
            BusType = "USB",
            SizeBytes = 32L * 1024 * 1024 * 1024,
            PartitionCount = 1,
            DriveLetters = { "Z:" },
            FileSystems = { "FAT32" },
            HealthStatus = "Healthy",
            IsRemovable = true,
            IsSimulated = true
        }
    };

    private static int ToInt(object? o) => o is null ? 0 : Convert.ToInt32(o);
    private static long ToLong(object? o) => o is null ? 0 : Convert.ToInt64(o);
    private static bool ToBool(object? o) => o is not null && Convert.ToBoolean(o);

    private static string? LetterFromValue(object? v)
    {
        if (v is null) return null;
        // MSFT_Partition.DriveLetter is a char (uint16); 0 means none.
        try
        {
            var c = Convert.ToChar(v);
            return char.IsLetter(c) ? $"{char.ToUpperInvariant(c)}:" : null;
        }
        catch { return null; }
    }

    private static string MapBusType(int b) => b switch
    {
        1 => "SCSI", 2 => "ATAPI", 3 => "ATA", 4 => "1394", 5 => "SSA", 6 => "FibreChannel",
        7 => "USB", 8 => "RAID", 9 => "iSCSI", 10 => "SAS", 11 => "SATA", 12 => "SD",
        13 => "MMC", 15 => "FileBackedVirtual", 16 => "StorageSpaces", 17 => "NVMe",
        _ => "Unknown"
    };

    private static string MapHealth(int h) => h switch
    {
        0 => "Healthy", 1 => "Warning", 2 => "Unhealthy", _ => "Unknown"
    };
}
