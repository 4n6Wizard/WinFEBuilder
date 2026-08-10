using System.Runtime.InteropServices;
using System.Security.Principal;
using WinFEBuilder.Core.Configuration;
using WinFEBuilder.Core.Logging;
using WinFEBuilder.Core.Models;

namespace WinFEBuilder.Core.Services;

/// <summary>Builds the dashboard environment audit from real system detection.</summary>
public sealed class EnvironmentService : IEnvironmentService
{
    private readonly ILogService _log;
    private readonly ISettingsService _settings;
    private readonly IAdkDetectionService _adk;

    public EnvironmentService(ILogService log, ISettingsService settings, IAdkDetectionService adk)
    {
        _log = log;
        _settings = settings;
        _adk = adk;
    }

    public bool IsElevated()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }

    public async Task<EnvironmentAuditResult> RunAuditAsync(CancellationToken ct = default)
    {
        _log.Info("Audit", "Environment audit started.");
        var result = new EnvironmentAuditResult();

        result.Items.Add(CheckAdmin());
        result.Items.Add(CheckArchitecture());
        result.Items.Add(CheckDotNet());
        result.Items.Add(CheckPowerShell());

        ct.ThrowIfCancellationRequested();

        // ADK / WinPE / DISM / Oscdimg all derive from a single detection pass.
        var adk = await Task.Run(() => _adk.Detect(), ct).ConfigureAwait(false);
        result.Adk = adk;
        result.Items.Add(CheckAdk(adk));
        result.Items.Add(CheckWinPe(adk));
        result.Items.Add(CheckDism(adk));
        result.Items.Add(CheckOscdimg(adk));

        result.Items.Add(CheckTempSpace());
        result.Items.Add(CheckWorkspace());
        result.Items.Add(CheckFramework());

        _log.Info("Audit", $"Environment audit finished. Overall: {result.Overall}.");
        LogSummary(result);
        return result;
    }

    private void LogSummary(EnvironmentAuditResult r)
    {
        foreach (var i in r.Items)
        {
            switch (i.Status)
            {
                case CheckStatus.Pass: _log.Pass("Audit", $"{i.Name}: {i.Summary}"); break;
                case CheckStatus.Warning: _log.Warning("Audit", $"{i.Name}: {i.Summary}", i.RecommendedAction); break;
                case CheckStatus.Fail: _log.Fail("Audit", $"{i.Name}: {i.Summary}"); break;
                default: _log.Info("Audit", $"{i.Name}: {i.Summary}"); break;
            }
        }
    }

    private AuditItem CheckAdmin()
    {
        var elevated = IsElevated();
        return new AuditItem
        {
            Key = "admin",
            Name = "Administrator privileges",
            Status = elevated ? CheckStatus.Pass : CheckStatus.Fail,
            Summary = elevated ? "Running elevated." : "Not running as Administrator.",
            Details = elevated
                ? "The application is running with Administrator privileges, which are required for DISM, DiskPart, and mounting operations."
                : "Administrator privileges are required for building WinFE media and creating USB drives.",
            RecommendedAction = elevated ? null : "Close the app and relaunch it as Administrator (right-click → Run as administrator)."
        };
    }

    private AuditItem CheckArchitecture()
    {
        var is64Os = Environment.Is64BitOperatingSystem;
        var is64Proc = Environment.Is64BitProcess;
        var pass = is64Os && is64Proc;
        return new AuditItem
        {
            Key = "arch",
            Name = "64-bit Windows",
            Status = pass ? CheckStatus.Pass : (is64Os ? CheckStatus.Warning : CheckStatus.Fail),
            Summary = $"OS 64-bit: {is64Os}; Process 64-bit: {is64Proc}.",
            Details = $"Operating system: {(is64Os ? "64-bit" : "32-bit")}. "
                      + $"Process: {(is64Proc ? "64-bit" : "32-bit")}. "
                      + $"OS description: {RuntimeInformation.OSDescription}. "
                      + $"Architecture: {RuntimeInformation.OSArchitecture}.",
            RecommendedAction = pass ? null : "WinFE x64 builds require 64-bit Windows. Run on a Windows 10/11 x64 system."
        };
    }

    private AuditItem CheckDotNet()
    {
        var version = RuntimeInformation.FrameworkDescription;
        var major = Environment.Version.Major;
        var pass = major >= 8;
        return new AuditItem
        {
            Key = "dotnet",
            Name = ".NET runtime",
            Status = pass ? CheckStatus.Pass : CheckStatus.Warning,
            Summary = version,
            Details = $"Runtime: {version}. CLR version: {Environment.Version}. .NET 8 (Windows Desktop) is required.",
            RecommendedAction = pass ? null : "Install the .NET 8 Desktop Runtime (x64)."
        };
    }

    private AuditItem CheckPowerShell()
    {
        var winPs = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            "WindowsPowerShell", "v1.0", "powershell.exe");
        var hasWinPs = File.Exists(winPs);
        var pwsh = FindOnPath("pwsh.exe");
        var hasPwsh = pwsh is not null;

        var status = (hasWinPs || hasPwsh) ? CheckStatus.Pass : CheckStatus.Fail;
        var summary = hasWinPs && hasPwsh ? "Windows PowerShell 5.1 and PowerShell 7 available."
                     : hasWinPs ? "Windows PowerShell 5.1 available."
                     : hasPwsh ? "PowerShell 7 (pwsh) available."
                     : "No PowerShell found.";

        return new AuditItem
        {
            Key = "powershell",
            Name = "PowerShell",
            Status = status,
            Summary = summary,
            Details = $"Windows PowerShell 5.1: {(hasWinPs ? winPs : "not found")}\n"
                      + $"PowerShell 7+: {(hasPwsh ? pwsh : "not found")}",
            RecommendedAction = status == CheckStatus.Fail
                ? "Install PowerShell (Windows PowerShell 5.1 ships with Windows; PowerShell 7 is optional)."
                : null
        };
    }

    private AuditItem CheckAdk(AdkInstallation adk)
    {
        var status = adk.AdkRoot is not null
            ? (adk.Found ? CheckStatus.Pass : CheckStatus.Warning)
            : CheckStatus.Fail;

        // An ADK that is installed but the wrong version cannot build WinFE media — a complete
        // install is not the same as a usable one, so it must not read as PASS.
        if (status == CheckStatus.Pass && adk.IsUnsupportedVersion)
            status = CheckStatus.Warning;

        string summary;
        if (adk.AdkRoot is null)
        {
            summary = "Windows ADK not detected.";
        }
        else if (adk.IsUnsupportedVersion)
        {
            summary = $"ADK {adk.Version} is not compatible with WinFE — "
                      + $"{AdkVersionPolicy.RequiredVersionDisplay} required.";
        }
        else
        {
            summary = $"ADK {adk.Version ?? "detected"} at {adk.AdkRoot}";
        }

        string? action = null;
        if (status == CheckStatus.Fail || adk.IsUnsupportedVersion)
            action = AdkVersionPolicy.Guidance;
        else if (status == CheckStatus.Warning)
            action = $"Some ADK components are missing — reinstall the ADK {AdkVersionPolicy.DocumentedRelease} "
                     + $"or {AdkVersionPolicy.NewestSupportedRelease} and the matching WinPE add-on.";

        return new AuditItem
        {
            Key = "adk",
            Name = "Windows ADK",
            Status = status,
            Summary = summary,
            Details = BuildAdkDetails(adk),
            RecommendedAction = action
        };
    }

    private static string DescribeVersionSupport(AdkInstallation adk) => adk.VersionSupport switch
    {
        AdkVersionSupport.Supported when adk.HasMixedVersionInstalls =>
            $"COMPATIBLE ({DescribeSupportedRelease(adk)} present, but other kits are installed side-by-side)",
        AdkVersionSupport.Supported => $"COMPATIBLE (release {DescribeSupportedRelease(adk)})",
        AdkVersionSupport.Unsupported =>
            $"NOT COMPATIBLE — WinFE is documented for ADK {AdkVersionPolicy.DocumentedRelease} and works with "
            + $"{AdkVersionPolicy.NewestSupportedRelease}; ADK 1903 and later do not produce working WinFE media",
        _ => "UNKNOWN — verify manually"
    };

    /// <summary>Names which compatible release was actually found, rather than guessing.</summary>
    private static string DescribeSupportedRelease(AdkInstallation adk)
    {
        foreach (var v in adk.DetectedVersions)
        {
            if (!Version.TryParse(v, out var parsed)) continue;
            if (parsed.Build == AdkVersionPolicy.DocumentedBuild) return AdkVersionPolicy.DocumentedRelease;
            if (parsed.Build == AdkVersionPolicy.NewestSupportedBuild) return AdkVersionPolicy.NewestSupportedRelease;
        }
        return $"{AdkVersionPolicy.DocumentedRelease}/{AdkVersionPolicy.NewestSupportedRelease}";
    }

    private static string BuildAdkDetails(AdkInstallation adk)
    {
        var lines = new List<string>
        {
            $"ADK root: {adk.AdkRoot ?? "not found"}",
            $"Version: {adk.Version ?? "unknown"}",
            $"Required for WinFE: {AdkVersionPolicy.RequiredVersionDisplay}",
            $"Version compatibility: {DescribeVersionSupport(adk)}",
            $"All ADK versions found: {(adk.DetectedVersions.Count > 0 ? string.Join(", ", adk.DetectedVersions) : "none")}",
            $"WinPE root: {adk.WinPeRoot ?? "not found"}",
            $"DISM: {adk.DismPath ?? "not found"}",
            $"Oscdimg: {adk.OscdimgPath ?? "not found"}",
            $"DandISetEnv.bat: {adk.DandISetEnvPath ?? "not found"}",
            $"WinPE optional components: {adk.WinPeOptionalComponentsPath ?? "not found"}",
            $"WinPE media: {adk.WinPeMediaPath ?? "not found"}",
            $"Architectures: {(adk.SupportedArchitectures.Count > 0 ? string.Join(", ", adk.SupportedArchitectures) : "none")}"
        };
        if (adk.Warnings.Count > 0)
        {
            lines.Add("");
            lines.Add("Warnings:");
            lines.AddRange(adk.Warnings.Select(w => "  • " + w));
        }
        return string.Join(Environment.NewLine, lines);
    }

    private AuditItem CheckWinPe(AdkInstallation adk)
    {
        var present = adk.WinPeAddOnPresent;
        return new AuditItem
        {
            Key = "winpe",
            Name = "WinPE add-on",
            Status = present ? CheckStatus.Pass : (adk.WinPeRoot is not null ? CheckStatus.Warning : CheckStatus.Fail),
            Summary = present ? "WinPE add-on present." : "WinPE add-on not found or incomplete.",
            Details = $"WinPE root: {adk.WinPeRoot ?? "not found"}\n"
                      + $"Optional components: {adk.WinPeOptionalComponentsPath ?? "not found"}\n"
                      + $"Media: {adk.WinPeMediaPath ?? "not found"}",
            RecommendedAction = present ? null
                : "Install the 'Windows PE add-on for the ADK' matching your installed ADK version."
        };
    }

    private AuditItem CheckDism(AdkInstallation adk)
    {
        // Prefer ADK DISM, but note the in-box DISM as a fallback.
        var inbox = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "Dism.exe");
        var hasInbox = File.Exists(inbox);
        var path = adk.DismPath ?? (hasInbox ? inbox : null);
        var status = adk.DismPath is not null ? CheckStatus.Pass
                    : hasInbox ? CheckStatus.Warning
                    : CheckStatus.Fail;
        return new AuditItem
        {
            Key = "dism",
            Name = "DISM",
            Status = status,
            Summary = path ?? "DISM not found.",
            Details = $"ADK DISM: {adk.DismPath ?? "not found"}\nIn-box DISM: {(hasInbox ? inbox : "not found")}\n"
                      + "The ADK-bundled DISM is preferred for servicing WinPE images.",
            RecommendedAction = status == CheckStatus.Fail ? "Install the Windows ADK (Deployment Tools feature)." : null
        };
    }

    private AuditItem CheckOscdimg(AdkInstallation adk)
    {
        return new AuditItem
        {
            Key = "oscdimg",
            Name = "Oscdimg",
            Status = adk.OscdimgPath is not null ? CheckStatus.Pass : CheckStatus.Fail,
            Summary = adk.OscdimgPath ?? "Oscdimg not found.",
            Details = $"Oscdimg: {adk.OscdimgPath ?? "not found"}\n"
                      + "Oscdimg builds the bootable ISO. It ships with the ADK Deployment Tools.",
            RecommendedAction = adk.OscdimgPath is null ? "Install the Windows ADK (Deployment Tools feature)." : null
        };
    }

    private AuditItem CheckTempSpace()
    {
        try
        {
            var temp = Path.GetTempPath();
            var root = Path.GetPathRoot(Path.GetFullPath(temp))!;
            var drive = new DriveInfo(root);
            var freeGb = drive.AvailableFreeSpace / 1024d / 1024d / 1024d;
            var min = _settings.Settings.MinimumFreeSpaceGb;
            var status = freeGb >= min ? CheckStatus.Pass
                        : freeGb >= min / 2 ? CheckStatus.Warning
                        : CheckStatus.Fail;
            return new AuditItem
            {
                Key = "tempspace",
                Name = "Available temporary disk space",
                Status = status,
                Summary = $"{freeGb:F1} GB free on {drive.Name}",
                Details = $"Temp path: {temp}\nVolume: {drive.Name}\nFree: {freeGb:F1} GB\nTotal: "
                          + $"{drive.TotalSize / 1024d / 1024d / 1024d:F1} GB\nMinimum required: {min:F0} GB.",
                RecommendedAction = status == CheckStatus.Pass ? null
                    : $"Free up disk space; WinFE builds need at least {min:F0} GB."
            };
        }
        catch (Exception ex)
        {
            return new AuditItem
            {
                Key = "tempspace",
                Name = "Available temporary disk space",
                Status = CheckStatus.Warning,
                Summary = "Could not determine free space.",
                Details = ex.Message
            };
        }
    }

    private AuditItem CheckWorkspace()
    {
        var ws = _settings.Settings.WorkspaceRoot;
        var configured = !string.IsNullOrWhiteSpace(ws);
        var exists = configured && Directory.Exists(ws);
        return new AuditItem
        {
            Key = "workspace",
            Name = "Configured workspace",
            Status = !configured ? CheckStatus.NotConfigured : (exists ? CheckStatus.Pass : CheckStatus.Warning),
            Summary = configured ? ws : "No workspace configured.",
            Details = configured
                ? $"Workspace root: {ws}\nExists: {exists}\n(The folder is created automatically when a build starts.)"
                : "Set a workspace root in Settings.",
            RecommendedAction = configured ? null : "Configure a workspace root on the Settings page."
        };
    }

    private AuditItem CheckFramework()
    {
        var fw = _settings.Settings.LastFrameworkPath;
        var configured = !string.IsNullOrWhiteSpace(fw);
        var exists = configured && Directory.Exists(fw);
        return new AuditItem
        {
            Key = "framework",
            Name = "Selected WinFE framework",
            Status = !configured ? CheckStatus.NotConfigured : (exists ? CheckStatus.Pass : CheckStatus.Warning),
            Summary = configured ? fw! : "No framework selected.",
            Details = configured
                ? $"Framework path: {fw}\nExists: {exists}\nValidate it on the Framework page."
                : "Select and validate a WinFE framework folder on the Framework page.",
            RecommendedAction = configured ? null : "Select the extracted WinFE framework folder on the Framework page."
        };
    }

    private static string? FindOnPath(string exe)
    {
        var pathVar = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(pathVar)) return null;
        foreach (var dir in pathVar.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var candidate = Path.Combine(dir.Trim(), exe);
                if (File.Exists(candidate)) return candidate;
            }
            catch { /* ignore malformed PATH entries */ }
        }
        return null;
    }
}
