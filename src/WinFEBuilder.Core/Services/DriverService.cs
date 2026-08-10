using WinFEBuilder.Core.Configuration;
using WinFEBuilder.Core.Hashing;
using WinFEBuilder.Core.Logging;
using WinFEBuilder.Core.Models;
using WinFEBuilder.Core.Validation;

namespace WinFEBuilder.Core.Services;

/// <summary>
/// Injects .inf drivers into a copied boot.wim using DISM. The mount is ALWAYS cleaned up — on
/// success (commit) and on any failure (discard + cleanup-mountpoints) — so an image is never left
/// mounted silently.
/// </summary>
public sealed class DriverService : IDriverService
{
    private readonly ILogService _log;
    private readonly IProcessRunner _runner;
    private readonly IDismService _dism;
    private readonly IHashService _hash;
    private readonly AppPaths _paths;

    public DriverService(ILogService log, IProcessRunner runner, IDismService dism, IHashService hash, AppPaths paths)
    {
        _log = log;
        _runner = runner;
        _dism = dism;
        _hash = hash;
        _paths = paths;
    }

    public async Task<List<DriverInfo>> EnumerateDriversAsync(string folder, string targetArch,
        int targetBuild = InfOsApplicability.Adk1809Build, CancellationToken ct = default)
    {
        var list = new List<DriverInfo>();
        if (!Directory.Exists(folder)) return list;

        foreach (var inf in Directory.EnumerateFiles(folder, "*.inf", SearchOption.AllDirectories))
        {
            ct.ThrowIfCancellationRequested();
            string content;
            try { content = await File.ReadAllTextAsync(inf, ct).ConfigureAwait(false); }
            catch { continue; }

            var archs = InfParser.DetectArchitectures(content);
            var compatible = InfParser.IsCompatibleWith(archs, targetArch);

            // Which Windows builds the device entries actually apply to. A driver decorated only for a
            // newer Windows (e.g. NTamd64.10.0...22000 = Windows 11) installs into a WinPE 1809 image
            // without complaint and then never binds — the operator just sees missing hardware.
            var osSupport = InfOsApplicability.Analyze(content, targetBuild, targetArch);
            string? osWarning = null;
            if (osSupport.Sections.Count > 0 && !osSupport.HasUsableDevices && osSupport.HasRestrictedDevices)
            {
                osWarning =
                    $"Every device this driver serves requires Windows build {osSupport.LowestRestrictedBuild} or newer. " +
                    $"The target image is build {targetBuild}, so DISM will install it successfully and it will " +
                    "never load. Obtain a version of this driver that supports your Windows build.";
            }

            var item = new DriverInfo
            {
                InfPath = inf,
                InfName = Path.GetFileName(inf),
                Architectures = archs,
                DriverClass = InfParser.GetClass(content),
                Provider = InfParser.GetProvider(content),
                CompatibleWithTarget = compatible,
                IncompatibilityReason = compatible ? null : $"Declares {string.Join("/", archs)}, target is {targetArch}.",
                OsSupport = osSupport,
                OsSupportWarning = osWarning,
                // Don't pre-select a driver that provably cannot bind; the operator can still tick it.
                Selected = compatible && osWarning is null
            };
            list.Add(item);

            if (osWarning is not null)
                _log.Warning("Drivers", $"{item.InfName}: {osSupport.Summary}", osWarning);
            else if (osSupport.HasRestrictedDevices)
                _log.Info("Drivers", $"{item.InfName}: {osSupport.Summary}");
        }

        _log.Info("Drivers", $"Found {list.Count} .inf driver(s) in {folder}.");
        return list;
    }

    public async Task<DriverInjectionResult> AddWinPeFeaturesAsync(
        string bootWimPath,
        IEnumerable<string> cabPaths,
        IProgress<string>? progress,
        IReadOnlyList<WinFeRegistryOperation>? reapplyRegistryPatches = null,
        CancellationToken ct = default)
    {
        var result = new DriverInjectionResult { BootWimPath = bootWimPath };
        void Report(string m) { progress?.Report(m); _log.Info("Drivers", m); }

        var cabs = (cabPaths ?? Enumerable.Empty<string>()).Where(File.Exists).ToList();

        var dism = _dism.ResolveDismPath();
        if (dism is null) { result.Errors.Add("DISM not found."); result.RecommendedAction = "Install the Windows ADK."; return result; }
        if (!File.Exists(bootWimPath)) { result.Errors.Add("boot.wim not found."); return result; }
        if (cabs.Count == 0) { result.Errors.Add("No WinPE component .cab files were found in the ADK."); result.RecommendedAction = "Confirm the WinPE add-on is installed."; return result; }

        // Session folder, and only the time in the name — the folder carries the date, and an operation
        // can run more than once per session.
        var logPath = Path.Combine(_paths.SessionLogDir, $"dism-winpefeatures_{DateTime.Now:HHmmss}.log");
        result.DismLogPath = logPath;

        try { result.Sha256Before = await _hash.ComputeSha256Async(bootWimPath, ct).ConfigureAwait(false); } catch { }

        Report("Cleaning up any stale DISM mounts…");
        await CleanupMountsAsync(ct).ConfigureAwait(false);

        var mountDir = Path.Combine(Path.GetTempPath(), $"winfe_mount_{Guid.NewGuid():N}");
        Directory.CreateDirectory(mountDir);
        result.MountDirectory = mountDir;

        try
        {
            Report($"Mounting boot.wim → {mountDir}");
            var mount = await RunDismAsync(new[] { "/Mount-Wim", $"/WimFile:{bootWimPath}", "/index:1", $"/MountDir:{mountDir}", $"/LogPath:{logPath}" }, ct).ConfigureAwait(false);
            if (mount.ExitCode != 0) { result.Errors.Add($"Mount failed (exit {mount.ExitCode})."); return result; }
            result.ImageMounted = true;

            foreach (var cab in cabs)
            {
                ct.ThrowIfCancellationRequested();
                Report($"Adding package: {Path.GetFileName(cab)}");
                var add = await RunDismAsync(new[] { $"/Image:{mountDir}", "/Add-Package", $"/PackagePath:{cab}", $"/LogPath:{logPath}" }, ct).ConfigureAwait(false);
                result.Added.Add(new DriverAddResult { InfName = Path.GetFileName(cab), ExitCode = add.ExitCode });
                if (add.ExitCode != 0)
                    result.Warnings.Add($"Package '{Path.GetFileName(cab)}' returned exit {add.ExitCode} (it may already be present).");
            }

            // WinFE's write-protection settings must be the LAST thing written to the image. DISM
            // package servicing above re-applies each package's own registry state, which reverts
            // values the framework's batch wrote before we were called. Replaying them here — while
            // still mounted, before the single commit — restores the required ordering.
            if (reapplyRegistryPatches is { Count: > 0 })
            {
                Report("Re-applying the framework's write-protection settings after component install…");
                var regOk = await ReapplyRegistryAsync(mountDir, reapplyRegistryPatches, result, ct).ConfigureAwait(false);
                result.RegistryReapplySucceeded = regOk;
                Report(regOk
                    ? $"Re-applied {result.RegistrySettingsReapplied} protection setting(s)."
                    : "One or more protection settings could not be re-applied.");
            }

            Report("Committing changes and unmounting…");
            var unmount = await RunDismAsync(new[] { "/Unmount-Wim", $"/MountDir:{mountDir}", "/Commit", $"/LogPath:{logPath}" }, ct).ConfigureAwait(false);
            if (unmount.ExitCode == 0) { result.Committed = true; result.ImageUnmounted = true; }
            else
            {
                result.Errors.Add($"Commit/unmount failed (exit {unmount.ExitCode}); discarding.");
                await DiscardAsync(mountDir, logPath, ct).ConfigureAwait(false);
                result.ImageUnmounted = true;
                return result;
            }

            try { result.Sha256After = await _hash.ComputeSha256Async(bootWimPath, ct).ConfigureAwait(false); } catch { }
            result.RevalidatedWim = await _dism.GetWimInfoAsync(bootWimPath, ct).ConfigureAwait(false);
            // A reverted protection setting is a correctness failure, not a warning: the image would
            // boot without write blocking. Treat it as unsuccessful so the build surfaces it.
            result.Success = result.DriversAdded > 0
                             && result.Committed
                             && result.RegistryReapplySucceeded != false;
            Report(result.Success ? $"Added {result.DriversAdded} package(s)." : "Component preparation did not complete cleanly.");
            return result;
        }
        catch (Exception ex)
        {
            _log.Error("Drivers", "WinPE feature injection failed.", ex);
            result.Errors.Add(ex.Message);
            await SafeDiscardAsync(result, mountDir, logPath).ConfigureAwait(false);
            return result;
        }
        finally
        {
            if (result.ImageMounted && !result.ImageUnmounted)
            {
                result.Warnings.Add("Image was still mounted at cleanup — discarding.");
                await SafeDiscardAsync(result, mountDir, logPath).ConfigureAwait(false);
            }
            try { if (Directory.Exists(mountDir) && !Directory.EnumerateFileSystemEntries(mountDir).Any()) Directory.Delete(mountDir); } catch { }
        }
    }

    /// <summary>
    /// Replays a framework's offline-registry operations against a mounted image.
    /// </summary>
    /// <remarks>
    /// Every hive this loads is unloaded again before returning, including on failure. A hive left
    /// loaded keeps the file handle open and makes the subsequent DISM commit fail, which would
    /// discard the whole image.
    /// </remarks>
    private async Task<bool> ReapplyRegistryAsync(
        string mountDir,
        IReadOnlyList<WinFeRegistryOperation> ops,
        DriverInjectionResult result,
        CancellationToken ct)
    {
        var loaded = new List<string>();
        var allOk = true;

        try
        {
            foreach (var op in ops)
            {
                ct.ThrowIfCancellationRequested();

                switch (op.Verb)
                {
                    case WinFeRegistryVerb.Load:
                    {
                        var hiveFile = Path.Combine(mountDir, op.HiveFileRelativePath!);
                        if (!File.Exists(hiveFile))
                        {
                            result.Warnings.Add($"Registry hive not present in the image: {op.HiveFileRelativePath}");
                            allOk = false;
                            break;
                        }

                        var load = await RunRegAsync(new[] { "load", op.HiveKey!, hiveFile }, ct).ConfigureAwait(false);
                        if (load.ExitCode == 0) loaded.Add(op.HiveKey!);
                        else
                        {
                            result.Warnings.Add($"Could not load hive {op.HiveKey} (exit {load.ExitCode}).");
                            allOk = false;
                        }
                        break;
                    }

                    case WinFeRegistryVerb.Unload:
                    {
                        if (!loaded.Any(h => string.Equals(h, op.HiveKey, StringComparison.OrdinalIgnoreCase)))
                            break;   // Never loaded (or already unloaded); nothing to do.

                        var unload = await RunRegAsync(new[] { "unload", op.HiveKey! }, ct).ConfigureAwait(false);
                        if (unload.ExitCode == 0)
                            loaded.RemoveAll(h => string.Equals(h, op.HiveKey, StringComparison.OrdinalIgnoreCase));
                        else
                        {
                            result.Warnings.Add($"Could not unload hive {op.HiveKey} (exit {unload.ExitCode}).");
                            allOk = false;
                        }
                        break;
                    }

                    default:
                    {
                        var verb = op.Verb == WinFeRegistryVerb.Add ? "add" : "delete";
                        var args = new List<string> { verb };
                        args.AddRange(op.Arguments);

                        var run = await RunRegAsync(args, ct).ConfigureAwait(false);
                        if (run.ExitCode == 0)
                        {
                            result.RegistrySettingsReapplied++;
                        }
                        else
                        {
                            result.Warnings.Add($"Registry setting failed (exit {run.ExitCode}): {op.RawLine.Trim()}");
                            allOk = false;
                        }
                        break;
                    }
                }
            }
        }
        finally
        {
            // Unload anything still open, even if the caller cancelled, so the commit can proceed.
            foreach (var hive in loaded.ToList())
            {
                var unload = await RunRegAsync(new[] { "unload", hive }, CancellationToken.None).ConfigureAwait(false);
                if (unload.ExitCode == 0)
                {
                    loaded.RemoveAll(h => string.Equals(h, hive, StringComparison.OrdinalIgnoreCase));
                }
                else
                {
                    result.Warnings.Add($"Hive {hive} is still loaded; the commit may fail.");
                    allOk = false;
                }
            }
        }

        return allOk;
    }

    private Task<ProcessRunResult> RunRegAsync(IEnumerable<string> arguments, CancellationToken ct)
        => _runner.RunAsync("reg.exe", arguments, workingDirectory: null, timeoutMs: 120_000,
                            onOutputLine: null, onErrorLine: null, closeStandardInput: true, ct: ct);

    public Task<string> GetMountedImagesAsync(CancellationToken ct = default)
        => RunDismAsync(new[] { "/Get-MountedImageInfo" }, ct).ContinueWith(t => t.Result.StandardOutput, ct);

    public async Task<bool> CleanupMountsAsync(CancellationToken ct = default)
    {
        var r = await RunDismAsync(new[] { "/Cleanup-Mountpoints" }, ct).ConfigureAwait(false);
        return r.ExitCode == 0;
    }

    public async Task<DriverInjectionResult> InjectAsync(string bootWimPath, IEnumerable<DriverInfo> drivers, bool forceUnsigned, IProgress<string>? progress, CancellationToken ct = default)
    {
        var result = new DriverInjectionResult { BootWimPath = bootWimPath };
        void Report(string m) { progress?.Report(m); _log.Info("Drivers", m); }

        var selected = drivers?.Where(d => d.Selected).ToList() ?? new List<DriverInfo>();

        var dism = _dism.ResolveDismPath();
        if (dism is null) { result.Errors.Add("DISM not found."); result.RecommendedAction = "Install the Windows ADK."; return result; }
        if (!File.Exists(bootWimPath)) { result.Errors.Add("boot.wim not found."); return result; }
        if (selected.Count == 0) { result.Errors.Add("No drivers selected."); return result; }

        var logPath = Path.Combine(_paths.SessionLogDir, $"dism-driver_{DateTime.Now:HHmmss}.log");
        result.DismLogPath = logPath;

        try { result.Sha256Before = await _hash.ComputeSha256Async(bootWimPath, ct).ConfigureAwait(false); } catch { }

        // Clear any stale mounts before we start.
        Report("Cleaning up any stale DISM mounts…");
        await CleanupMountsAsync(ct).ConfigureAwait(false);

        var mountDir = Path.Combine(Path.GetTempPath(), $"winfe_mount_{Guid.NewGuid():N}");
        Directory.CreateDirectory(mountDir);
        result.MountDirectory = mountDir;

        try
        {
            Report($"Mounting boot.wim → {mountDir}");
            var mount = await RunDismAsync(new[] { "/Mount-Wim", $"/WimFile:{bootWimPath}", "/index:1", $"/MountDir:{mountDir}", $"/LogPath:{logPath}" }, ct).ConfigureAwait(false);
            if (mount.ExitCode != 0)
            {
                result.Errors.Add($"Mount failed (exit {mount.ExitCode}).");
                result.RecommendedAction = "Ensure the boot.wim isn't in use and DISM mounts are clean.";
                return result;
            }
            result.ImageMounted = true;

            foreach (var d in selected)
            {
                ct.ThrowIfCancellationRequested();
                Report($"Adding driver: {d.InfName}");
                var args = new List<string> { $"/Image:{mountDir}", "/Add-Driver", $"/Driver:{d.InfPath}", $"/LogPath:{logPath}" };
                if (forceUnsigned) args.Add("/ForceUnsigned");
                var add = await RunDismAsync(args, ct).ConfigureAwait(false);
                result.Added.Add(new DriverAddResult { InfName = d.InfName, ExitCode = add.ExitCode });
                if (add.ExitCode != 0)
                    result.Warnings.Add($"Driver '{d.InfName}' failed (exit {add.ExitCode}).");
            }

            Report("Committing changes and unmounting…");
            var unmount = await RunDismAsync(new[] { "/Unmount-Wim", $"/MountDir:{mountDir}", "/Commit", $"/LogPath:{logPath}" }, ct).ConfigureAwait(false);
            if (unmount.ExitCode == 0)
            {
                result.Committed = true;
                result.ImageUnmounted = true;
            }
            else
            {
                result.Errors.Add($"Commit/unmount failed (exit {unmount.ExitCode}); discarding changes.");
                await DiscardAsync(mountDir, logPath, ct).ConfigureAwait(false);
                result.ImageUnmounted = true;
                result.RecommendedAction = "See the DISM log; changes were discarded to protect the image.";
                return result;
            }

            // Re-validate + re-hash after a successful commit.
            try { result.Sha256After = await _hash.ComputeSha256Async(bootWimPath, ct).ConfigureAwait(false); } catch { }
            result.RevalidatedWim = await _dism.GetWimInfoAsync(bootWimPath, ct).ConfigureAwait(false);

            result.Success = result.DriversAdded > 0 && result.Committed;
            Report(result.Success
                ? $"Injected {result.DriversAdded} driver(s); {result.DriversFailed} failed."
                : "No drivers were successfully added.");
            return result;
        }
        catch (OperationCanceledException)
        {
            result.Errors.Add("Canceled — discarding changes.");
            await SafeDiscardAsync(result, mountDir, logPath).ConfigureAwait(false);
            return result;
        }
        catch (Exception ex)
        {
            _log.Error("Drivers", "Driver injection failed.", ex);
            result.Errors.Add(ex.Message);
            await SafeDiscardAsync(result, mountDir, logPath).ConfigureAwait(false);
            return result;
        }
        finally
        {
            // Never leave a mount behind.
            if (result.ImageMounted && !result.ImageUnmounted)
            {
                result.Warnings.Add("Image was still mounted at cleanup — discarding.");
                await SafeDiscardAsync(result, mountDir, logPath).ConfigureAwait(false);
            }
            try { if (Directory.Exists(mountDir) && !Directory.EnumerateFileSystemEntries(mountDir).Any()) Directory.Delete(mountDir); } catch { }
        }
    }

    private async Task SafeDiscardAsync(DriverInjectionResult result, string mountDir, string logPath)
    {
        try
        {
            await DiscardAsync(mountDir, logPath, CancellationToken.None).ConfigureAwait(false);
            result.ImageUnmounted = true;
        }
        catch (Exception ex)
        {
            _log.Error("Drivers", "Discard failed; running cleanup-mountpoints.", ex);
            try { await CleanupMountsAsync(CancellationToken.None).ConfigureAwait(false); } catch { }
        }
    }

    private Task DiscardAsync(string mountDir, string logPath, CancellationToken ct)
        => RunDismAsync(new[] { "/Unmount-Wim", $"/MountDir:{mountDir}", "/Discard", $"/LogPath:{logPath}" }, ct);

    private Task<ProcessRunResult> RunDismAsync(IEnumerable<string> args, CancellationToken ct)
    {
        var dism = _dism.ResolveDismPath() ?? "dism.exe";
        var full = new List<string> { "/English" };
        full.AddRange(args);
        return _runner.RunAsync(dism, full, timeoutMs: 600_000,
            onOutputLine: ProcessOutputFilter.Wrap(l => _log.Debug("Drivers", l)),
            onErrorLine: l => _log.Warning("Drivers", l),
            ct: ct);
    }
}
