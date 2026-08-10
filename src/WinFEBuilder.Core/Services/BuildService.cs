using System.Text;
using System.Text.Json;
using WinFEBuilder.Core.Configuration;
using WinFEBuilder.Core.Hashing;
using WinFEBuilder.Core.Logging;
using WinFEBuilder.Core.Models;
using WinFEBuilder.Core.Validation;

namespace WinFEBuilder.Core.Services;

/// <summary>
/// Orchestrates the WinFE build. Invokes the official framework batch files (never reimplements the
/// build) and validates the produced media/ISO. Build success is kept strictly separate from the
/// manual boot / write-protection tests, which are never set here.
/// </summary>
public sealed class BuildService : IBuildService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly ILogService _log;
    private readonly ISettingsService _settings;
    private readonly IEnvironmentService _environment;
    private readonly IFrameworkService _framework;
    private readonly IDismService _dism;
    private readonly IProcessRunner _runner;
    private readonly IHashService _hash;
    private readonly IDriverService _drivers;

    public BuildService(
        ILogService log, ISettingsService settings, IEnvironmentService environment,
        IFrameworkService framework, IDismService dism, IProcessRunner runner, IHashService hash,
        IDriverService drivers)
    {
        _log = log;
        _settings = settings;
        _environment = environment;
        _framework = framework;
        _dism = dism;
        _runner = runner;
        _hash = hash;
        _drivers = drivers;
    }

    public async Task<BuildResult> RunBuildAsync(BuildRequest request, IProgress<string>? progress, CancellationToken ct)
    {
        var result = new BuildResult { StartTime = DateTimeOffset.Now };
        void Report(string m) { progress?.Report(m); _log.Info("Build", m); }

        try
        {
            // 1) Re-run environment audit and gate on prerequisites.
            var auditStage = result.AddStage("Environment audit");
            Report("Re-running environment audit…");
            var audit = await _environment.RunAuditAsync(ct).ConfigureAwait(false);
            var adk = audit.Adk;

            if (!_environment.IsElevated())
            {
                Fail(result, auditStage, "Administrator privileges are required to build WinFE media.",
                    "Relaunch WinFE Builder as Administrator.");
                return Finish(result);
            }
            if (adk is null || adk.DismPath is null || adk.WinPeRoot is null)
            {
                Fail(result, auditStage, "The Windows ADK / WinPE add-on is not fully installed.",
                    AdkVersionPolicy.Guidance);
                return Finish(result);
            }
            // The framework's batch files only work with ADK 1809. A newer ADK does not fail
            // cleanly — it can produce media that looks built but is not correct — so stop here
            // rather than hand the operator unusable forensic media. 'Unknown' never blocks:
            // version detection is best-effort.
            if (adk.IsUnsupportedVersion)
            {
                Fail(result, auditStage,
                    $"The installed Windows ADK ({adk.Version}) is not compatible with the WinFE framework. "
                    + AdkVersionPolicy.Requirement,
                    AdkVersionPolicy.Guidance);
                return Finish(result);
            }
            if (adk.VersionSupport == AdkVersionSupport.Unknown)
            {
                result.Warnings.Add(
                    "The ADK version could not be determined. WinFE requires "
                    + $"{AdkVersionPolicy.RequiredVersionDisplay} — "
                    + "verify this before relying on the media.");
            }
            if (adk.HasMixedVersionInstalls)
            {
                result.Warnings.Add(
                    $"Multiple ADK versions are installed ({string.Join(", ", adk.DetectedVersions)}). "
                    + "A leftover newer WinPE payload can cause confusing build failures.");
            }

            if (adk.OscdimgPath is null && !request.SkipIso)
                result.Warnings.Add("Oscdimg not found — ISO creation may fail.");
            auditStage.Status = CheckStatus.Pass;
            auditStage.Detail = $"ADK {adk.Version ?? "detected"} "
                                + (adk.VersionSupport == AdkVersionSupport.Supported ? "(compatible)" : "(version unverified)")
                                + "; DISM + WinPE present.";

            // 2) Revalidate framework.
            var fwStage = result.AddStage("Revalidate framework");
            var frameworkPath = request.FrameworkPath ?? _settings.Settings.LastFrameworkPath;
            if (string.IsNullOrWhiteSpace(frameworkPath))
            {
                Fail(result, fwStage, "No framework selected.", "Select and validate a framework on the Framework page.");
                return Finish(result);
            }
            Report($"Validating framework: {frameworkPath}");
            var validation = await _framework.ValidateAsync(frameworkPath, ct).ConfigureAwait(false);
            if (!validation.IsValid)
            {
                Fail(result, fwStage, $"Framework is not valid: {validation.Summary}", validation.RecommendedAction);
                return Finish(result);
            }
            fwStage.Status = CheckStatus.Pass;
            fwStage.Detail = validation.Summary;

            // 3+4) Create workspace and copy framework.
            var copyStage = result.AddStage("Create workspace + copy framework");
            Report("Creating workspace and copying framework…");
            var copy = await _framework.CopyToWorkspaceAsync(validation,
                new Progress<string>(Report), ct).ConfigureAwait(false);
            if (!copy.Success || copy.OutputPaths.Count < 2)
            {
                Fail(result, copyStage, copy.Message, copy.RecommendedAction);
                return Finish(result);
            }
            result.WorkspacePath = copy.OutputPaths[0];
            result.FrameworkInWorkspace = copy.OutputPaths[1];
            copyStage.Status = CheckStatus.Pass;
            copyStage.Detail = result.WorkspacePath;

            // 5) Select + run the media build batch.
            var scriptNames = validation.BuildScripts.Select(s => s.Name).ToList();
            var mediaName = request.MediaScriptName ?? BuildScriptSelector.SelectMediaScript(scriptNames);
            result.MediaScript = mediaName;
            var mediaScriptPath = ResolveScriptPath(validation, result.FrameworkInWorkspace!, mediaName);

            var mediaStage = result.AddStage("Run WinFE media build");
            if (mediaScriptPath is null || !File.Exists(mediaScriptPath))
            {
                Fail(result, mediaStage, "Could not locate the media build script in the workspace.",
                    "Confirm the framework contains a MakeWinFE*.bat build script.");
                return Finish(result);
            }
            Report($"Running media build: {mediaName}");
            result.MediaBuildRun = await _runner.RunBatchFileAsync(
                mediaScriptPath,
                Path.GetDirectoryName(mediaScriptPath),
                timeoutMs: request.TimeoutMinutes * 60_000,
                onOutputLine: ProcessOutputFilter.Wrap(l => _log.Debug("Build", l)),
                onErrorLine: l => _log.Warning("Build", l),
                ct: ct).ConfigureAwait(false);

            if (result.MediaBuildRun.TimedOut)
            {
                Fail(result, mediaStage, "The media build timed out.", "Increase the timeout or run the batch manually to diagnose.");
                return Finish(result);
            }
            // WinFE batch files do NOT check errorlevel, so cmd can exit 0 even when DISM failed.
            // Scan the captured output for DISM errors so we don't report a false success.
            var mediaOut = (result.MediaBuildRun.StandardOutput ?? "") + "\n" + (result.MediaBuildRun.StandardError ?? "");
            // Only treat genuine DISM error codes ("Error: 2", "Error: 0x...") as failures.
            // Benign batch copy messages ("The system cannot find the file specified." for optional
            // files) are NOT DISM failures and must not trip a warning on an otherwise-good build.
            var dismErrors = System.Text.RegularExpressions.Regex.IsMatch(mediaOut, @"Error:\s*(0x)?[0-9A-Fa-f]{1,8}\b");
            var x86WinPeMissing =
                System.Text.RegularExpressions.Regex.IsMatch(mediaOut, @"x86\\winpe\.wim", System.Text.RegularExpressions.RegexOptions.IgnoreCase)
                && mediaOut.Contains("cannot find the file specified", StringComparison.OrdinalIgnoreCase);

            mediaStage.Status = (result.MediaBuildRun.ExitCode == 0 && !dismErrors) ? CheckStatus.Pass : CheckStatus.Warning;
            mediaStage.Detail = $"Exit code {result.MediaBuildRun.ExitCode} in {result.MediaBuildRun.Duration.TotalSeconds:F0}s"
                                + (dismErrors ? " — DISM reported errors (batch ignores errorlevel)." : ".");
            if (result.MediaBuildRun.ExitCode != 0)
                result.Warnings.Add($"Media build returned exit code {result.MediaBuildRun.ExitCode}; verifying output anyway.");
            if (dismErrors)
                result.Warnings.Add("The build batch reported DISM errors even though it exited 0. WinFE batch files do not check errorlevel, so a zero exit code does not mean success.");

            // 6) Verify media + boot.wim.
            var verifyStage = result.AddStage("Verify media + boot.wim");
            Report("Verifying boot structure and inspecting boot.wim…");
            result.Media = await VerifyMediaAsync(result.WorkspacePath!, ct).ConfigureAwait(false);
            verifyStage.Status = result.Media.Status;
            verifyStage.Detail = result.Media.Summary;
            if (result.Media.StructureValid)
                result.OperationalStatus = ValidationStatus.BootStructureValidated;
            else
            {
                // Give a targeted diagnosis for the common modern-ADK case: no 32-bit WinPE.
                var action = x86WinPeMissing
                    ? "This framework builds 32-bit (x86) media, but your Windows ADK has no x86 WinPE "
                      + "(32-bit WinPE was removed in ADK 10.1.26100+). Use an x64-only WinFE framework, "
                      + "or install an ADK/WinPE add-on that still includes x86."
                    : result.Media.RecommendedAction;
                Fail(result, verifyStage, result.Media.Summary, action);
                // Boot structure is required before the ISO step; stop here.
                return Finish(result);
            }

            // 6.5) Automatically prepare the Windows environment: determine which components the
            // included tools require (FTK Imager → .NET, etc.), resolve WinPE dependencies internally,
            // and install them silently into every boot.wim. No Microsoft package names are surfaced;
            // exact DISM package names appear only in the DISM log for troubleshooting.
            if (request.IncludeDotNet)
            {
                var prepStage = result.AddStage("Preparing the Windows environment");
                try
                {
                    var toolNames = DiscoverIncludedTools(result.WorkspacePath!);
                    var capabilities = ToolComponentResolver.Resolve(toolNames);
                    var featureNames = WindowsCapabilityCatalog.ResolveFeatures(capabilities);

                    // Installing components re-applies each package's registry state, reverting the
                    // write-protection values the framework's batch wrote earlier. Read those values
                    // back out of the batch so they can be re-applied last, inside the same mount.
                    List<WinFeRegistryOperation> protectionPatches = new();
                    try
                    {
                        var batchText = await File.ReadAllTextAsync(mediaScriptPath, ct).ConfigureAwait(false);
                        protectionPatches = WinFeRegistryPatchParser.Parse(batchText);
                    }
                    catch (Exception ex)
                    {
                        _log.Warning("Build", $"Could not read registry settings from {mediaName}: {ex.Message}");
                    }

                    if (protectionPatches.Count == 0)
                    {
                        result.Warnings.Add(
                            "The framework's write-protection registry settings could not be read from "
                            + $"{mediaName}, so they cannot be re-applied after Windows components are "
                            + "installed. Verify write protection on the finished image before using it on evidence.");
                    }
                    else
                    {
                        Report($"Found {protectionPatches.Count} framework registry operation(s) to re-apply after component install.");
                    }

                    // 'adk' from the audit step above is non-null here (the preflight gate returned otherwise).
                    var bootWims = SafeEnumerateFiles(result.WorkspacePath!)
                        .Where(f => f.Replace('/', '\\').EndsWith(@"\sources\boot.wim", StringComparison.OrdinalIgnoreCase))
                        .ToList();

                    int ok = 0;
                    int reapplied = 0;
                    var protectionIntact = true;
                    foreach (var wim in bootWims)
                    {
                        ct.ThrowIfCancellationRequested();
                        var isX86 = wim.Replace('/', '\\').Contains(@"\x86\", StringComparison.OrdinalIgnoreCase);
                        var ocRoot = ResolveOcRoot(adk!, isX86);
                        if (ocRoot is null)
                        {
                            result.Warnings.Add("Some Windows components could not be located; the WinPE add-on may be incomplete.");
                            continue;
                        }

                        var cabs = WinPeFeatureCatalog.CabPaths(ocRoot, "en-us", featureNames);
                        Report($"Configuring the WinFE environment ({(isX86 ? "x86" : "x64")})…");
                        var r = await _drivers.AddWinPeFeaturesAsync(wim, cabs, new Progress<string>(Report), protectionPatches, ct).ConfigureAwait(false);
                        if (r.Success) ok++;
                        else result.Warnings.Add($"A required Windows component could not be installed for the {(isX86 ? "32-bit" : "64-bit")} image. See the DISM log for details ({r.DismLogPath}).");

                        reapplied += r.RegistrySettingsReapplied;
                        if (r.RegistryReapplySucceeded == false)
                        {
                            protectionIntact = false;
                            result.Warnings.Add(
                                $"Write-protection settings could not be fully re-applied to the {(isX86 ? "32-bit" : "64-bit")} "
                                + "image after installing Windows components. Do NOT use this image on evidence until "
                                + $"verified. See {r.DismLogPath}.");
                        }
                    }
                    prepStage.Status = (ok > 0 && protectionIntact) ? CheckStatus.Pass : CheckStatus.Warning;
                    prepStage.Detail = $"Windows environment prepared for {ok}/{bootWims.Count} image(s)"
                                       + (protectionPatches.Count > 0
                                           ? $"; {reapplied} write-protection setting(s) re-applied last."
                                           : ".");
                }
                catch (Exception ex)
                {
                    prepStage.Status = CheckStatus.Warning;
                    prepStage.Detail = "Could not fully prepare the Windows environment: " + ex.Message;
                    result.Warnings.Add("Could not fully prepare the Windows environment: " + ex.Message);
                }
            }

            // 7+8) ISO build + verification.
            if (!request.SkipIso)
            {
                var isoName = request.IsoScriptName ?? BuildScriptSelector.SelectIsoScript(scriptNames);
                result.IsoScript = isoName;
                var isoScriptPath = ResolveScriptPath(validation, result.FrameworkInWorkspace!, isoName);

                var isoBuildStage = result.AddStage("Run ISO build");
                if (isoScriptPath is null || !File.Exists(isoScriptPath))
                {
                    isoBuildStage.Status = CheckStatus.Warning;
                    isoBuildStage.Detail = "No ISO build script found; skipping ISO creation.";
                    result.Warnings.Add("No ISO build script was found in the framework.");
                }
                else
                {
                    Report($"Running ISO build: {isoName}");
                    result.IsoBuildRun = await _runner.RunBatchFileAsync(
                        isoScriptPath,
                        Path.GetDirectoryName(isoScriptPath),
                        timeoutMs: request.TimeoutMinutes * 60_000,
                        onOutputLine: ProcessOutputFilter.Wrap(l => _log.Debug("Build", l)),
                        onErrorLine: l => _log.Warning("Build", l),
                        ct: ct).ConfigureAwait(false);
                    isoBuildStage.Status = result.IsoBuildRun.ExitCode == 0 ? CheckStatus.Pass : CheckStatus.Warning;
                    isoBuildStage.Detail = $"Exit code {result.IsoBuildRun.ExitCode}.";

                    var isoVerifyStage = result.AddStage("Verify + copy ISO");
                    Report("Locating and validating ISO…");
                    result.Iso = await VerifyIsoAsync(result.WorkspacePath!, result.FrameworkInWorkspace!, ct).ConfigureAwait(false);
                    isoVerifyStage.Status = result.Iso.Status;
                    isoVerifyStage.Detail = result.Iso.Summary;
                    if (!result.Iso.Valid)
                        result.Warnings.Add(result.Iso.RecommendedAction ?? "ISO could not be validated.");
                }
            }

            // Determine build success.
            if (result.MediaBuildRun?.ExitCode == 0)
                result.OperationalStatus = ValidationStatus.BuildSuccessful;
            if (result.Media.StructureValid)
                result.OperationalStatus = ValidationStatus.BootStructureValidated;

            result.Success = result.Media.StructureValid && (request.SkipIso || result.Iso?.Valid == true);

            // 9) Manifest.
            var manifestStage = result.AddStage("Write build manifest");
            result.ManifestPath = WriteManifest(result, validation);
            WriteHumanReadableReport(result);
            manifestStage.Status = CheckStatus.Pass;
            manifestStage.Detail = result.ManifestPath;

            Report(result.Success ? "Build completed and validated." : "Build finished with warnings — see stages.");
            return Finish(result);
        }
        catch (OperationCanceledException)
        {
            _log.Warning("Build", "Build canceled by user.");
            result.Errors.Add("Build canceled by user.");
            result.AddStage("Canceled").Status = CheckStatus.Warning;
            return Finish(result);
        }
        catch (Exception ex)
        {
            _log.Error("Build", "Build failed unexpectedly.", ex);
            result.Errors.Add(ex.Message);
            result.RecommendedAction = "See the log for details.";
            var s = result.AddStage("Error");
            s.Status = CheckStatus.Fail;
            s.Detail = ex.Message;
            return Finish(result);
        }
    }

    /// <summary>Tool folder names present in the workspace media's tools\x64 / tools\x86 folders.</summary>
    private static List<string> DiscoverIncludedTools(string workspacePath)
    {
        var names = new List<string>();
        try
        {
            var mediaRoot = MediaLocator.FindMediaRoot(Directory.EnumerateDirectories(workspacePath, "*", SearchOption.AllDirectories));
            if (mediaRoot is null) return names;
            foreach (var arch in new[] { "x64", "x86" })
            {
                var toolsDir = Path.Combine(mediaRoot, "tools", arch);
                if (Directory.Exists(toolsDir))
                    names.AddRange(Directory.GetDirectories(toolsDir).Select(d => Path.GetFileName(d)!));
            }
        }
        catch { /* best effort; .NET baseline still applies */ }
        return names;
    }

    private static string? ResolveOcRoot(Models.AdkInstallation adk, bool isX86)
    {
        if (string.IsNullOrEmpty(adk.WinPeRoot)) return null;
        var root = isX86
            ? Path.Combine(adk.WinPeRoot, "x86", "WinPE_OCs")
            : (adk.WinPeOptionalComponentsPath ?? Path.Combine(adk.WinPeRoot, "amd64", "WinPE_OCs"));
        return Directory.Exists(root) ? root : null;
    }

    private static string? ResolveScriptPath(FrameworkValidationResult validation, string frameworkRoot, string? scriptName)
    {
        if (string.IsNullOrWhiteSpace(scriptName)) return null;
        var script = validation.BuildScripts.FirstOrDefault(s =>
            string.Equals(s.Name, scriptName, StringComparison.OrdinalIgnoreCase));
        // Preserve the relative location inside the copied framework.
        var rel = script?.RelativePath ?? scriptName;
        return Path.Combine(frameworkRoot, rel);
    }

    private async Task<MediaValidationResult> VerifyMediaAsync(string workspace, CancellationToken ct)
    {
        var media = new MediaValidationResult();

        var allFiles = SafeEnumerateFiles(workspace).ToList();
        var bootWim = MediaLocator.FindBootWim(allFiles);
        var root = MediaLocator.MediaRootFromBootWim(bootWim);

        if (bootWim is null || root is null)
        {
            media.Status = CheckStatus.Fail;
            media.Summary = "boot.wim was not found under the workspace after the build.";
            media.RecommendedAction = "Check the build output; the framework may write media elsewhere. Review the build log.";
            return media;
        }

        media.MediaRoot = root;
        media.BootWimPath = bootWim;

        foreach (var comp in MediaLocator.ExpectedBootComponents)
        {
            var full = Path.Combine(root, comp);
            media.Expected[comp] = comp.EndsWith("boot.wim", StringComparison.OrdinalIgnoreCase)
                ? File.Exists(full)
                : Directory.Exists(full) || File.Exists(full);
        }

        // Read-only boot.wim inspection.
        media.Wim = await _dism.GetWimInfoAsync(bootWim, ct).ConfigureAwait(false);

        if (!media.StructureValid)
        {
            media.Status = CheckStatus.Fail;
            var missing = media.Expected.Where(kv => !kv.Value).Select(kv => kv.Key);
            media.Summary = "Media structure incomplete. Missing: " + string.Join(", ", missing);
            media.RecommendedAction = "Review the build log; the media build may not have completed.";
        }
        else if (media.Wim?.DismSucceeded != true)
        {
            media.Status = CheckStatus.Warning;
            media.Summary = "Boot structure present, but DISM could not inspect boot.wim.";
            media.RecommendedAction = "Verify the ADK DISM is installed and boot.wim is not locked.";
        }
        else
        {
            media.Status = CheckStatus.Pass;
            media.Summary = $"Boot structure validated. boot.wim: {media.Wim.ImageCount} image(s), "
                            + $"arch {media.Wim.Architecture ?? "unknown"}, "
                            + $"{media.Wim.SizeBytes / 1024d / 1024d:F0} MB.";
        }

        return media;
    }

    private async Task<IsoValidationResult> VerifyIsoAsync(string workspace, string frameworkRoot, CancellationToken ct)
    {
        var iso = new IsoValidationResult();

        var candidates = new List<(string, long, DateTimeOffset)>();
        foreach (var dir in new[] { workspace, frameworkRoot }.Distinct())
        {
            foreach (var f in SafeEnumerateFiles(dir).Where(f => f.EndsWith(".iso", StringComparison.OrdinalIgnoreCase)))
            {
                try
                {
                    var fi = new FileInfo(f);
                    candidates.Add((f, fi.Length, fi.LastWriteTimeUtc));
                }
                catch { /* ignore */ }
            }
        }

        var chosen = MediaLocator.SelectNewestIso(candidates);
        if (chosen is null)
        {
            iso.Status = CheckStatus.Fail;
            iso.Found = false;
            iso.Summary = "No non-empty ISO was found after the ISO build.";
            iso.RecommendedAction = "Confirm Oscdimg is installed and the ISO build script completed successfully.";
            return iso;
        }

        iso.Found = true;
        iso.SourcePath = chosen;
        var info = new FileInfo(chosen);
        iso.SizeBytes = info.Length;
        iso.Timestamp = info.LastWriteTimeUtc;

        if (iso.SizeBytes == 0)
        {
            iso.Status = CheckStatus.Fail;
            iso.Summary = "The generated ISO is zero bytes.";
            iso.RecommendedAction = "Re-run the ISO build; the previous attempt produced an empty file.";
            return iso;
        }

        iso.Sha256 = await _hash.ComputeSha256Async(chosen, ct).ConfigureAwait(false);

        // Copy to the controlled output directory.
        try
        {
            var outRoot = _settings.Settings.OutputRoot;
            Directory.CreateDirectory(outRoot);
            var name = $"{Path.GetFileNameWithoutExtension(chosen)}_{DateTime.Now:yyyy-MM-dd_HHmmss}.iso";
            var dest = Path.Combine(outRoot, name);
            File.Copy(chosen, dest, overwrite: false);
            iso.DestinationPath = dest;
            iso.Valid = true;
            iso.Status = CheckStatus.Pass;
            iso.Summary = $"ISO validated ({iso.SizeBytes / 1024d / 1024d:F0} MB) and copied to output.";
        }
        catch (Exception ex)
        {
            iso.Valid = true; // source ISO is valid even if the copy failed
            iso.Status = CheckStatus.Warning;
            iso.Summary = "ISO validated but copy to output failed.";
            iso.RecommendedAction = "Check permissions/space on the output directory.";
            _log.Error("Build", "ISO copy failed.", ex);
        }

        return iso;
    }

    private string WriteManifest(BuildResult r, FrameworkValidationResult validation)
    {
        var manifest = new BuildManifest
        {
            ComputerName = Environment.MachineName,
            Operator = _settings.Settings.OperatorName,
            Organization = _settings.Settings.OrganizationName,
            FrameworkSource = validation.SourcePath,
            WorkspacePath = r.WorkspacePath,
            FrameworkInWorkspace = r.FrameworkInWorkspace,
            MediaScript = r.MediaScript,
            IsoScript = r.IsoScript,
            MediaBuildExitCode = r.MediaBuildRun?.ExitCode,
            IsoBuildExitCode = r.IsoBuildRun?.ExitCode,
            MediaRoot = r.Media?.MediaRoot,
            BootStructureValidated = r.Media?.StructureValid ?? false,
            BootWimPath = r.Media?.BootWimPath,
            BootWimSize = r.Media?.Wim?.SizeBytes ?? 0,
            BootWimSha256 = r.Media?.Wim?.Sha256,
            BootWimArchitecture = r.Media?.Wim?.Architecture,
            BootWimImageCount = r.Media?.Wim?.ImageCount ?? 0,
            IsoSourcePath = r.Iso?.SourcePath,
            IsoDestinationPath = r.Iso?.DestinationPath,
            IsoSize = r.Iso?.SizeBytes ?? 0,
            IsoSha256 = r.Iso?.Sha256,
            BuildStatus = r.MediaBuildRun?.ExitCode == 0 ? "Build Successful" : "Build Completed With Warnings",
            BootStructureStatus = (r.Media?.StructureValid ?? false) ? "Boot Structure Validated" : "Not Validated",
            BootTestStatus = "NOT TESTED",
            WriteProtectionTestStatus = "NOT TESTED",
            OrganizationApprovalStatus = "NOT APPROVED"
        };
        if (r.Media?.Expected is not null)
            foreach (var kv in r.Media.Expected) manifest.ExpectedBootComponents[kv.Key] = kv.Value;
        if (r.Media?.Wim?.Images is not null)
            manifest.BootWimImages.AddRange(r.Media.Wim.Images);
        manifest.Warnings.AddRange(r.Warnings);
        manifest.Errors.AddRange(r.Errors);

        var path = Path.Combine(r.WorkspacePath!, "build-manifest.json");
        File.WriteAllText(path, JsonSerializer.Serialize(manifest, JsonOptions));
        _log.Info("Build", $"Wrote build manifest: {path}");
        return path;
    }

    private void WriteHumanReadableReport(BuildResult r)
    {
        try
        {
            var sb = new StringBuilder();
            sb.AppendLine("WinFE Builder — Build Report");
            sb.AppendLine($"Generated: {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"Computer:  {Environment.MachineName}");
            sb.AppendLine($"Workspace: {r.WorkspacePath}");
            sb.AppendLine();
            sb.AppendLine("Stages:");
            foreach (var s in r.Stages)
                sb.AppendLine($"  [{s.Status,-13}] {s.Name} — {s.Detail}");
            sb.AppendLine();
            sb.AppendLine($"Media script: {r.MediaScript}  (exit {r.MediaBuildRun?.ExitCode})");
            sb.AppendLine($"ISO script:   {r.IsoScript}  (exit {r.IsoBuildRun?.ExitCode})");
            if (r.Media?.Wim is not null)
            {
                sb.AppendLine($"boot.wim:     {r.Media.BootWimPath}");
                sb.AppendLine($"  SHA-256:    {r.Media.Wim.Sha256}");
                sb.AppendLine($"  Arch:       {r.Media.Wim.Architecture}");
                sb.AppendLine($"  Images:     {r.Media.Wim.ImageCount}");
            }
            if (r.Iso is not null)
            {
                sb.AppendLine($"ISO source:   {r.Iso.SourcePath}");
                sb.AppendLine($"ISO output:   {r.Iso.DestinationPath}");
                sb.AppendLine($"ISO SHA-256:  {r.Iso.Sha256}");
            }
            sb.AppendLine();
            sb.AppendLine("Operational status (build vs. forensic):");
            sb.AppendLine($"  Build:              {(r.MediaBuildRun?.ExitCode == 0 ? "Successful" : "Completed with warnings")}");
            sb.AppendLine($"  Boot Structure:     {((r.Media?.StructureValid ?? false) ? "Validated" : "Not validated")}");
            sb.AppendLine($"  Boot Test:          {r.BootTestStatus}");
            sb.AppendLine($"  Write-Protection:   {r.WriteProtectionTestStatus}");
            if (r.Warnings.Count > 0) { sb.AppendLine(); sb.AppendLine("Warnings:"); r.Warnings.ForEach(w => sb.AppendLine("  • " + w)); }
            if (r.Errors.Count > 0) { sb.AppendLine(); sb.AppendLine("Errors:"); r.Errors.ForEach(e => sb.AppendLine("  • " + e)); }

            var path = Path.Combine(r.WorkspacePath!, "build-report.txt");
            File.WriteAllText(path, sb.ToString());
        }
        catch (Exception ex)
        {
            _log.Debug("Build", $"Could not write human-readable report: {ex.Message}");
        }
    }

    private void Fail(BuildResult r, BuildStage stage, string message, string? action)
    {
        stage.Status = CheckStatus.Fail;
        stage.Detail = message;
        r.Success = false;
        r.Errors.Add(message);
        r.RecommendedAction ??= action;
        _log.Fail("Build", message);
    }

    private BuildResult Finish(BuildResult r)
    {
        r.FinishTime = DateTimeOffset.Now;
        return r;
    }

    private static IEnumerable<string> SafeEnumerateFiles(string root)
    {
        if (!Directory.Exists(root)) yield break;
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            var dir = pending.Pop();
            string[] subDirs = Array.Empty<string>();
            try { subDirs = Directory.GetDirectories(dir); } catch { }
            foreach (var s in subDirs) pending.Push(s);

            string[] files = Array.Empty<string>();
            try { files = Directory.GetFiles(dir); } catch { }
            foreach (var f in files) yield return f;
        }
    }
}
