using System.Text.RegularExpressions;

namespace WinFEBuilder.Core.Validation;

/// <summary>
/// Pure, IO-free parsing of Windows .inf driver files: architecture, class, provider. Architecture
/// is inferred from decorated section names (e.g. [Manufacturer]'s NTamd64/NTx86/NTarm64 targets).
/// </summary>
public static class InfParser
{
    /// <summary>Detect the architectures a .inf targets from its content.</summary>
    public static List<string> DetectArchitectures(string infContent)
    {
        var archs = new List<string>();
        if (string.IsNullOrWhiteSpace(infContent)) return archs;

        // Decorated section targets appear as ".NTamd64", ".NTx86", ".NTarm64" (case-insensitive).
        if (Regex.IsMatch(infContent, @"\.nt(amd64|x64)", RegexOptions.IgnoreCase)) archs.Add("amd64");
        if (Regex.IsMatch(infContent, @"\.ntx86\b", RegexOptions.IgnoreCase)) archs.Add("x86");
        if (Regex.IsMatch(infContent, @"\.ntarm64\b", RegexOptions.IgnoreCase)) archs.Add("arm64");

        // A bare ".NT" (no arch) targets all architectures.
        if (archs.Count == 0 && Regex.IsMatch(infContent, @"\.nt\b", RegexOptions.IgnoreCase))
            archs.Add("all");

        return archs.Distinct().ToList();
    }

    public static string? GetValue(string infContent, string section, string key)
    {
        if (string.IsNullOrWhiteSpace(infContent)) return null;
        var lines = infContent.Split('\n');
        bool inSection = false;
        foreach (var raw in lines)
        {
            var line = StripComment(raw).Trim();
            if (line.StartsWith("[") && line.EndsWith("]"))
            {
                inSection = string.Equals(line.Trim('[', ']'), section, StringComparison.OrdinalIgnoreCase);
                continue;
            }
            if (!inSection) continue;
            var idx = line.IndexOf('=');
            if (idx <= 0) continue;
            var k = line[..idx].Trim();
            if (string.Equals(k, key, StringComparison.OrdinalIgnoreCase))
                return line[(idx + 1)..].Trim().Trim('"');
        }
        return null;
    }

    public static string? GetClass(string infContent) => GetValue(infContent, "Version", "Class");
    public static string? GetProvider(string infContent) => GetValue(infContent, "Version", "Provider");

    /// <summary>
    /// A driver is considered compatible with <paramref name="targetArch"/> (e.g. "amd64") if it
    /// declares that arch, declares "all", or declares nothing determinable.
    /// </summary>
    public static bool IsCompatibleWith(IReadOnlyCollection<string> declaredArchs, string targetArch)
    {
        if (declaredArchs.Count == 0) return true;                 // undetermined → don't block
        if (declaredArchs.Contains("all", StringComparer.OrdinalIgnoreCase)) return true;
        return declaredArchs.Contains(targetArch, StringComparer.OrdinalIgnoreCase);
    }

    private static string StripComment(string line)
    {
        var i = line.IndexOf(';');
        return i >= 0 ? line[..i] : line;
    }
}
