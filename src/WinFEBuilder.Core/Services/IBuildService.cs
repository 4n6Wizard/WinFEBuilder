using WinFEBuilder.Core.Models;

namespace WinFEBuilder.Core.Services;

/// <summary>Inputs for a build. Script names are relative file names within the framework.</summary>
public sealed class BuildRequest
{
    /// <summary>Framework folder to build from. Defaults to the last validated framework.</summary>
    public string? FrameworkPath { get; init; }

    /// <summary>Media build batch file name (auto-selected if null).</summary>
    public string? MediaScriptName { get; init; }

    /// <summary>ISO build batch file name (auto-selected if null).</summary>
    public string? IsoScriptName { get; init; }

    /// <summary>Per-batch timeout in minutes.</summary>
    public int TimeoutMinutes { get; init; } = 45;

    /// <summary>If true, skip the ISO build step (media only).</summary>
    public bool SkipIso { get; init; }

    /// <summary>
    /// If true, add the WinPE .NET Framework (WinPE-NetFx) to boot.wim after the media build and
    /// before the ISO build, so .NET tools like FTK Imager run (otherwise: "mscoree.dll not found").
    /// </summary>
    public bool IncludeDotNet { get; init; } = true;
}

public interface IBuildService
{
    /// <summary>
    /// Run the full build workflow: re-audit, revalidate framework, create workspace, copy, run the
    /// official media build batch, verify media + boot.wim (read-only DISM), run the ISO batch,
    /// verify + hash + copy the ISO, and write a build manifest. Reports progress lines as it goes.
    /// </summary>
    Task<BuildResult> RunBuildAsync(BuildRequest request, IProgress<string>? progress, CancellationToken ct);
}
