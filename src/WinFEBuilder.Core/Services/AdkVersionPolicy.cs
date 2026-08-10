namespace WinFEBuilder.Core.Services;

/// <summary>How a detected ADK installation relates to the version WinFE requires.</summary>
public enum AdkVersionSupport
{
    /// <summary>No version could be determined — proceed, but the operator must verify manually.</summary>
    Unknown = 0,

    /// <summary>A compatible ADK (version 1809) is installed.</summary>
    Supported = 1,

    /// <summary>Only incompatible ADK version(s) are installed — a WinFE build will not work.</summary>
    Unsupported = 2
}

/// <summary>
/// The ADK version rule for WinFE, in one place.
/// <para>
/// Colin Ramsden's WinFE framework (IntelWinFE) is documented for <b>ADK 1803</b>
/// (10.1.17134.x) — his build instructions say "version 1803" and the batch file header repeats it.
/// <b>1809</b> (10.1.17763.x) is the next release and remains compatible; it has been used to build
/// and boot-test working media. From ADK <b>1903</b> onward Microsoft restructured the WinPE payload
/// and the framework's batch files no longer produce a working WinFE image — they either fail
/// outright or, worse, appear to succeed while producing media that is not correct.
/// </para>
/// <para>
/// Both 1803 and 1809 are therefore accepted. Accepting only 1809 would refuse the very version
/// Colin documents, which is how this started life as a bug.
/// </para>
/// Pure logic: no registry, no filesystem, so it is fully unit-testable.
/// </summary>
public static class AdkVersionPolicy
{
    /// <summary>The ADK release Colin's build instructions specify.</summary>
    public const string DocumentedRelease = "1803";

    /// <summary>The newest ADK release still compatible with the framework.</summary>
    public const string NewestSupportedRelease = "1809";

    /// <summary>Windows build number of ADK 1803 (10.1.<b>17134</b>.x) — the documented release.</summary>
    public const int DocumentedBuild = 17134;

    /// <summary>Windows build number of ADK 1809 (10.1.<b>17763</b>.x) — also compatible.</summary>
    public const int NewestSupportedBuild = 17763;

    /// <summary>First ADK build known to break the framework (1903).</summary>
    public const int FirstIncompatibleBuild = 18362;

    /// <summary>Every ADK build that works with the WinFE framework.</summary>
    public static readonly IReadOnlyList<int> SupportedBuilds = new[] { DocumentedBuild, NewestSupportedBuild };

    /// <summary>Human-readable form of the accepted versions.</summary>
    public const string RequiredVersionDisplay = "10.1.17134.x (1803) or 10.1.17763.x (1809)";

    public const string AdkDownloadUrl = "https://go.microsoft.com/fwlink/?linkid=2026036";
    public const string WinPeDownloadUrl = "https://go.microsoft.com/fwlink/?linkid=2022233";

    /// <summary>One-line statement of the requirement, for logs and status summaries.</summary>
    public const string Requirement =
        "WinFE requires the Windows ADK version 1803 or 1809 (10.1.17134.x / 10.1.17763.x) "
        + "and the matching WinPE add-on of the same version.";

    /// <summary>Full operator guidance, used as the recommended action on a failed/warned check.</summary>
    public static string Guidance =>
        "Install the Windows ADK for Windows 10 version " + DocumentedRelease + " or "
        + NewestSupportedRelease + ", and the matching Windows PE add-on of the same version — not the "
        + "current ADK. Colin Ramsden's build instructions specify " + DocumentedRelease + "; "
        + NewestSupportedRelease + " also works. ADK 1903 and later do not produce working WinFE "
        + "media. Uninstall the newer ADK and WinPE add-on first (they share the Windows Kits\\10 "
        + "root). ADK 1809: " + AdkDownloadUrl + "  |  WinPE add-on 1809: " + WinPeDownloadUrl;

    /// <summary>True when a single version string is one of the compatible ADK releases.</summary>
    public static bool IsSupported(string? version) =>
        TryParseBuild(version, out var build) && SupportedBuilds.Contains(build);

    /// <summary>
    /// Classifies every version discovered on the machine. A supported version anywhere in the set
    /// counts as <see cref="AdkVersionSupport.Supported"/>: side-by-side kits are common, and the
    /// required payload being present is what matters. Nothing parseable is
    /// <see cref="AdkVersionSupport.Unknown"/> — never a hard block, because version detection is
    /// best-effort.
    /// </summary>
    public static AdkVersionSupport Evaluate(IEnumerable<string?>? detectedVersions)
    {
        if (detectedVersions is null) return AdkVersionSupport.Unknown;

        var parsed = false;
        foreach (var v in detectedVersions)
        {
            if (!TryParseBuild(v, out var build)) continue;
            parsed = true;
            if (SupportedBuilds.Contains(build)) return AdkVersionSupport.Supported;
        }

        return parsed ? AdkVersionSupport.Unsupported : AdkVersionSupport.Unknown;
    }

    /// <summary>
    /// True when a compatible kit is installed alongside incompatible one(s). The build can still
    /// work, but a leftover newer WinPE payload is a common cause of confusing failures, so the
    /// operator should be told.
    /// </summary>
    public static bool HasMixedInstalls(IEnumerable<string?>? detectedVersions)
    {
        if (detectedVersions is null) return false;

        var supported = false;
        var other = false;
        foreach (var v in detectedVersions)
        {
            if (!TryParseBuild(v, out var build)) continue;
            if (SupportedBuilds.Contains(build)) supported = true;
            else other = true;
        }

        return supported && other;
    }

    /// <summary>Extracts the Windows build component from an ADK version string.</summary>
    private static bool TryParseBuild(string? version, out int build)
    {
        build = 0;
        if (string.IsNullOrWhiteSpace(version)) return false;
        if (!Version.TryParse(version.Trim(), out var parsed)) return false;
        if (parsed.Build < 0) return false;

        build = parsed.Build;
        return true;
    }
}
