using Microsoft.Win32;
using WinFEBuilder.Core.Logging;
using WinFEBuilder.Core.Models;

namespace WinFEBuilder.Core.Services;

/// <summary>
/// Detects the Windows ADK and WinPE add-on. Searches multiple candidate locations and
/// discovers the installed version rather than assuming one. Purely read-only.
/// </summary>
public sealed class AdkDetectionService : IAdkDetectionService
{
    private readonly ILogService _log;

    // Relative sub-paths inside a Windows Kits root.
    private const string DeploymentToolsRel = @"Assessment and Deployment Kit\Deployment Tools";
    private const string WinPeRel = @"Assessment and Deployment Kit\Windows Preinstallation Environment";

    public AdkDetectionService(ILogService log) => _log = log;

    public AdkInstallation Detect()
    {
        var result = new AdkInstallation();

        var kitsRoot = FindKitsRoot();
        if (kitsRoot is null)
        {
            result.Found = false;
            result.Warnings.Add("No Windows Kits root could be located via registry, Program Files, or environment variables.");
            _log.Fail("ADK", "Windows ADK not detected.");
            return result;
        }

        _log.Info("ADK", $"Windows Kits root: {kitsRoot}");

        var deploymentTools = Path.Combine(kitsRoot, DeploymentToolsRel);
        var winpe = Path.Combine(kitsRoot, WinPeRel);

        result.AdkRoot = Directory.Exists(Path.Combine(kitsRoot, "Assessment and Deployment Kit"))
            ? Path.Combine(kitsRoot, "Assessment and Deployment Kit")
            : kitsRoot;

        // DandISetEnv.bat (Deployment and Imaging Tools Environment)
        var dandi = Path.Combine(deploymentTools, "DandISetEnv.bat");
        if (File.Exists(dandi)) result.DandISetEnvPath = dandi;
        else result.Warnings.Add("DandISetEnv.bat (Deployment and Imaging Tools Environment) not found.");

        // DISM (prefer amd64)
        result.DismPath = FirstExisting(
            Path.Combine(deploymentTools, "amd64", "DISM", "dism.exe"),
            Path.Combine(deploymentTools, "x86", "DISM", "dism.exe"));
        if (result.DismPath is null)
            result.Warnings.Add("ADK-bundled DISM not found under Deployment Tools.");

        // Oscdimg (prefer amd64)
        result.OscdimgPath = FirstExisting(
            Path.Combine(deploymentTools, "amd64", "Oscdimg", "oscdimg.exe"),
            Path.Combine(deploymentTools, "x86", "Oscdimg", "oscdimg.exe"));
        if (result.OscdimgPath is null)
            result.Warnings.Add("Oscdimg (ISO creation tool) not found under Deployment Tools.");

        // WinPE add-on
        if (Directory.Exists(winpe))
        {
            result.WinPeRoot = winpe;

            var ocs = FirstExistingDir(
                Path.Combine(winpe, "amd64", "WinPE_OCs"));
            result.WinPeOptionalComponentsPath = ocs;
            if (ocs is null)
                result.Warnings.Add("WinPE optional components (amd64\\WinPE_OCs) not found — install the WinPE add-on.");

            var media = FirstExistingDir(
                Path.Combine(winpe, "amd64", "Media"));
            result.WinPeMediaPath = media;
            if (media is null)
                result.Warnings.Add("WinPE base media (amd64\\Media) not found — install the WinPE add-on.");

            if (Directory.Exists(Path.Combine(winpe, "amd64")))
                result.SupportedArchitectures.Add("amd64");
            if (Directory.Exists(Path.Combine(winpe, "x86")))
                result.SupportedArchitectures.Add("x86");
            if (Directory.Exists(Path.Combine(winpe, "arm64")))
                result.SupportedArchitectures.Add("arm64");
        }
        else
        {
            result.Warnings.Add("WinPE add-on directory not found (Windows Preinstallation Environment).");
        }

        result.DetectedVersions.AddRange(DetectVersions(kitsRoot));
        result.Version = result.DetectedVersions.FirstOrDefault();

        // WinFE only works with ADK 1809 — classify what we found (never a hard block on 'unknown').
        result.VersionSupport = AdkVersionPolicy.Evaluate(result.DetectedVersions);
        result.HasMixedVersionInstalls = AdkVersionPolicy.HasMixedInstalls(result.DetectedVersions);

        switch (result.VersionSupport)
        {
            case AdkVersionSupport.Unsupported:
                result.Warnings.Add(
                    $"ADK version {result.Version} is not compatible with the WinFE framework. "
                    + AdkVersionPolicy.Requirement);
                break;
            case AdkVersionSupport.Unknown:
                result.Warnings.Add(
                    "ADK version could not be determined — verify manually that it is "
                    + $"{AdkVersionPolicy.RequiredVersionDisplay}.");
                break;
            case AdkVersionSupport.Supported when result.HasMixedVersionInstalls:
                result.Warnings.Add(
                    "A compatible ADK is present, but other ADK versions are "
                    + $"installed side-by-side ({string.Join(", ", result.DetectedVersions)}). A leftover "
                    + "newer WinPE payload is a common cause of confusing build failures — consider "
                    + "uninstalling the newer ADK and WinPE add-on.");
                break;
        }

        // "Found" requires the essentials for the WinFE workflow.
        result.Found = result.DismPath is not null
                       && result.OscdimgPath is not null
                       && result.WinPeRoot is not null;

        if (result.Found && result.VersionSupport == AdkVersionSupport.Unsupported)
        {
            _log.Warning("ADK",
                $"Windows ADK detected, but version {result.Version} is not compatible with WinFE "
                + $"(requires {AdkVersionPolicy.RequiredVersionDisplay}).",
                AdkVersionPolicy.Guidance);
        }
        else if (result.Found)
        {
            _log.Pass("ADK", $"Windows ADK detected (version {result.Version ?? "unknown"}).");
        }
        else
        {
            _log.Warning("ADK", "Windows ADK partially detected — see warnings.",
                $"Install the Windows ADK {AdkVersionPolicy.DocumentedRelease} or {AdkVersionPolicy.NewestSupportedRelease} and the matching WinPE add-on.");
        }

        return result;
    }

    /// <summary>Locate the Windows Kits 10 root via registry, then Program Files, then env vars.</summary>
    private string? FindKitsRoot()
    {
        // 1. Registry (both native and WOW6432 views).
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
                using var key = baseKey.OpenSubKey(@"SOFTWARE\Microsoft\Windows Kits\Installed Roots");
                var root = key?.GetValue("KitsRoot10") as string;
                if (!string.IsNullOrWhiteSpace(root) && Directory.Exists(root))
                {
                    _log.Debug("ADK", $"KitsRoot10 from registry ({view}): {root}");
                    return root.TrimEnd('\\');
                }
            }
            catch (Exception ex)
            {
                _log.Debug("ADK", $"Registry read failed ({view}): {ex.Message}");
            }
        }

        // 2. Program Files candidates.
        foreach (var pf in new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles)
        })
        {
            if (string.IsNullOrEmpty(pf)) continue;
            var candidate = Path.Combine(pf, "Windows Kits", "10");
            if (Directory.Exists(candidate)) return candidate;
        }

        // 3. Environment variable sometimes set by DandISetEnv.
        var envRoot = Environment.GetEnvironmentVariable("WinPERoot")
                      ?? Environment.GetEnvironmentVariable("KitsRoot10");
        if (!string.IsNullOrWhiteSpace(envRoot))
        {
            // WinPERoot points into the WinPE folder; walk up to the kits root.
            var idx = envRoot.IndexOf("Windows Kits", StringComparison.OrdinalIgnoreCase);
            if (idx >= 0)
            {
                var guess = envRoot.Substring(0, idx) + @"Windows Kits\10";
                if (Directory.Exists(guess)) return guess.TrimEnd('\\');
            }
            if (Directory.Exists(envRoot)) return envRoot.TrimEnd('\\');
        }

        return null;
    }

    /// <summary>
    /// Discover every installed ADK version from the kits 'bin' version folders, newest first.
    /// All of them are reported, not just the newest: WinFE needs 1809 specifically, so a 1809 kit
    /// sitting beside a newer one must not be hidden by the newer version winning.
    /// </summary>
    private List<string> DetectVersions(string kitsRoot)
    {
        try
        {
            var bin = Path.Combine(kitsRoot, "bin");
            if (Directory.Exists(bin))
            {
                var versions = Directory.GetDirectories(bin)
                    .Select(Path.GetFileName)
                    .Where(n => !string.IsNullOrEmpty(n) && Version.TryParse(n, out _))
                    .Select(n => Version.Parse(n!))
                    .OrderByDescending(v => v)
                    .Select(v => v.ToString())
                    .ToList();
                if (versions.Count > 0)
                    return versions;
            }
        }
        catch (Exception ex)
        {
            _log.Debug("ADK", $"Version detection failed: {ex.Message}");
        }

        // Fallback: registry uninstall DisplayVersion.
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32);
            using var uninstall = baseKey.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
            if (uninstall is not null)
            {
                foreach (var sub in uninstall.GetSubKeyNames())
                {
                    using var k = uninstall.OpenSubKey(sub);
                    var name = k?.GetValue("DisplayName") as string;
                    if (name is not null && name.Contains("Assessment and Deployment Kit", StringComparison.OrdinalIgnoreCase)
                        && k?.GetValue("DisplayVersion") is string display && !string.IsNullOrWhiteSpace(display))
                    {
                        return new List<string> { display };
                    }
                }
            }
        }
        catch { /* ignore */ }

        return new List<string>();
    }

    private static string? FirstExisting(params string[] candidates) =>
        candidates.FirstOrDefault(File.Exists);

    private static string? FirstExistingDir(params string[] candidates) =>
        candidates.FirstOrDefault(Directory.Exists);
}
