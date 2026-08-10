using System.Text.RegularExpressions;

namespace WinFEBuilder.Core.Validation;

/// <summary>One decorated device-list section in an .inf and the OS build it requires.</summary>
public sealed record InfDeviceSection(string SectionName, string Decoration, int MinimumBuild, int DeviceCount)
{
    /// <summary>True when this section applies to an image of the given Windows build.</summary>
    public bool AppliesTo(int targetBuild) => MinimumBuild <= targetBuild;
}

/// <summary>What an .inf supports relative to a particular Windows build.</summary>
public sealed class InfOsSupport
{
    public List<InfDeviceSection> Sections { get; } = new();

    /// <summary>Devices listed in sections usable on the target build.</summary>
    public int UsableDeviceCount { get; set; }

    /// <summary>
    /// Devices listed ONLY in sections requiring a newer Windows build. These install fine but never
    /// bind — the driver is present and the hardware still shows up as unsupported.
    /// </summary>
    public int RestrictedDeviceCount { get; set; }

    /// <summary>Lowest build required by any decorated section that lists devices.</summary>
    public int? LowestRestrictedBuild { get; set; }

    public bool HasUsableDevices => UsableDeviceCount > 0;
    public bool HasRestrictedDevices => RestrictedDeviceCount > 0;

    /// <summary>Short phrase for a list column.</summary>
    public string Summary
    {
        get
        {
            if (Sections.Count == 0) return "OS targets not declared";
            if (!HasUsableDevices && HasRestrictedDevices)
                return $"Requires Windows build {LowestRestrictedBuild}+ — will not bind";
            if (HasRestrictedDevices)
                return $"{UsableDeviceCount} usable; {RestrictedDeviceCount} need build {LowestRestrictedBuild}+";
            return $"{UsableDeviceCount} device(s), no build restriction";
        }
    }
}

/// <summary>
/// Reads which Windows versions an .inf's device lists actually apply to.
/// <para>
/// This exists because of a failure that is invisible at build time: a driver whose device entries
/// live only in a section decorated for a newer Windows — e.g. <c>[Realtek.NTamd64.10.0...22000]</c>,
/// meaning Windows 11 build 22000+ — installs into a WinPE 1809 image perfectly (DISM reports
/// success, the package is signed) and then never binds to the hardware, because Windows only reads
/// sections applicable to the running build. The operator sees "no network adapter" and has no way to
/// tell the driver apart from a correct one.
/// </para>
/// Decoration grammar: <c>NT&lt;arch&gt;[.&lt;major&gt;.&lt;minor&gt;][...&lt;build&gt;]</c>, so
/// <c>NTamd64</c> has no build floor while <c>NTamd64.10.0...22000</c> requires build 22000.
/// </summary>
public static class InfOsApplicability
{
    /// <summary>WinPE from ADK 1809 — Windows 10 build 17763.</summary>
    public const int Adk1809Build = 17763;

    /// <summary>WinPE from ADK 1803 — Windows 10 build 17134.</summary>
    public const int Adk1803Build = 17134;

    private static readonly Regex DecorationRx = new(
        @"^NT(?<arch>x86|amd64|arm64|ia64)?(?:\.(?<major>\d+)\.(?<minor>\d+))?(?:\.\.\.(?<build>\d+))?$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Analyses an .inf against a target Windows build. <paramref name="targetArch"/> filters device
    /// sections to the relevant architecture (e.g. "amd64").
    /// </summary>
    public static InfOsSupport Analyze(string infContent, int targetBuild = Adk1809Build, string targetArch = "amd64")
    {
        var support = new InfOsSupport();
        if (string.IsNullOrWhiteSpace(infContent)) return support;

        var manufacturerSections = ReadManufacturerSections(infContent, targetArch);
        if (manufacturerSections.Count == 0) return support;

        // Count hardware-ID entries per device-list section. A device may appear in several sections
        // (one per OS generation); it is only "restricted" if none of its sections apply.
        var byDevice = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);

        foreach (var (sectionName, decoration, minBuild) in manufacturerSections)
        {
            var ids = ReadHardwareIds(infContent, sectionName);
            support.Sections.Add(new InfDeviceSection(sectionName, decoration, minBuild, ids.Count));

            foreach (var id in ids)
            {
                if (!byDevice.TryGetValue(id, out var builds)) byDevice[id] = builds = new List<int>();
                builds.Add(minBuild);
            }
        }

        foreach (var (_, builds) in byDevice)
        {
            if (builds.Any(b => b <= targetBuild)) support.UsableDeviceCount++;
            else
            {
                support.RestrictedDeviceCount++;
                var lowest = builds.Min();
                if (support.LowestRestrictedBuild is null || lowest < support.LowestRestrictedBuild)
                    support.LowestRestrictedBuild = lowest;
            }
        }

        return support;
    }

    /// <summary>
    /// Whether a specific hardware ID is served by a section applicable to the target build. Matching
    /// is a prefix comparison, so <c>PCI\VEN_10EC&amp;DEV_8125</c> matches a fuller entry that also
    /// carries SUBSYS/REV.
    /// </summary>
    public static bool SupportsHardwareId(string infContent, string hardwareId,
        int targetBuild = Adk1809Build, string targetArch = "amd64")
    {
        if (string.IsNullOrWhiteSpace(hardwareId)) return false;
        var needle = hardwareId.Trim();

        foreach (var (sectionName, _, minBuild) in ReadManufacturerSections(infContent, targetArch))
        {
            if (minBuild > targetBuild) continue;
            foreach (var id in ReadHardwareIds(infContent, sectionName))
            {
                if (id.StartsWith(needle, StringComparison.OrdinalIgnoreCase) ||
                    needle.StartsWith(id, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Reads [Manufacturer] and expands each decorated target into the device-list section it names,
    /// e.g. <c>%Realtek%=Realtek, NTamd64, NTamd64.10.0...22000</c> yields [Realtek.NTamd64] and
    /// [Realtek.NTamd64.10.0...22000].
    /// </summary>
    private static List<(string SectionName, string Decoration, int MinimumBuild)> ReadManufacturerSections(
        string infContent, string targetArch)
    {
        var result = new List<(string, string, int)>();

        foreach (var line in ReadSectionLines(infContent, "Manufacturer"))
        {
            var eq = line.IndexOf('=');
            if (eq <= 0) continue;

            var parts = line[(eq + 1)..].Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) continue;

            var baseName = parts[0].Trim();
            if (baseName.Length == 0) continue;

            if (parts.Length == 1)
            {
                // Undecorated: the base section serves every OS.
                result.Add((baseName, "", 0));
                continue;
            }

            foreach (var decoration in parts.Skip(1))
            {
                var m = DecorationRx.Match(decoration);
                if (!m.Success) continue;

                var arch = m.Groups["arch"].Success ? m.Groups["arch"].Value : null;
                if (arch is not null && !arch.Equals(targetArch, StringComparison.OrdinalIgnoreCase))
                {
                    // Treat x64 and amd64 as the same thing; otherwise skip other architectures.
                    var bothAmd64 = arch.Equals("amd64", StringComparison.OrdinalIgnoreCase) &&
                                    targetArch.Equals("x64", StringComparison.OrdinalIgnoreCase);
                    if (!bothAmd64) continue;
                }

                var build = m.Groups["build"].Success ? int.Parse(m.Groups["build"].Value) : 0;
                result.Add(($"{baseName}.{decoration}", decoration, build));
            }
        }

        return result;
    }

    /// <summary>Hardware IDs listed in a device-list section (the text after the last comma).</summary>
    private static List<string> ReadHardwareIds(string infContent, string sectionName)
    {
        var ids = new List<string>();

        foreach (var line in ReadSectionLines(infContent, sectionName))
        {
            // Form: %Description% = InstallSection, HardwareId[, HardwareId...]
            var eq = line.IndexOf('=');
            if (eq <= 0) continue;

            var rhs = line[(eq + 1)..];
            var parts = rhs.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            // parts[0] is the install section; the remainder are hardware IDs.
            for (var i = 1; i < parts.Length; i++)
                if (parts[i].Length > 0) ids.Add(parts[i]);
        }

        return ids;
    }

    private static IEnumerable<string> ReadSectionLines(string infContent, string section)
    {
        var inSection = false;
        foreach (var raw in infContent.Split('\n'))
        {
            var line = StripComment(raw).Trim();
            if (line.Length == 0) continue;

            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                inSection = string.Equals(line.Trim('[', ']'), section, StringComparison.OrdinalIgnoreCase);
                continue;
            }

            if (inSection) yield return line;
        }
    }

    private static string StripComment(string line)
    {
        var i = line.IndexOf(';');
        return i >= 0 ? line[..i] : line;
    }
}
