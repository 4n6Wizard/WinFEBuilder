namespace WinFEBuilder.Core.Validation;

/// <summary>
/// Automatically determines which Windows capabilities (see <see cref="WindowsCapabilityCatalog"/>)
/// are required by the tools included in a build. The user never chooses components — this maps the
/// selected tools to capabilities, and the build resolves the underlying WinPE packages internally.
/// </summary>
public static class ToolComponentResolver
{
    /// <summary>
    /// Resolve required capability keys from the included tool folder names. .NET Framework is the
    /// baseline (the WinFE GUI shell and common forensic imagers such as FTK Imager all require it),
    /// with extra capabilities added when a tool clearly needs them.
    /// </summary>
    public static IReadOnlyList<string> Resolve(IEnumerable<string> toolNames)
    {
        // Baseline: .NET is required for the common forensic tools and the WinFE shell.
        var caps = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "DotNet" };

        foreach (var name in toolNames ?? Enumerable.Empty<string>())
        {
            var n = (name ?? string.Empty).ToLowerInvariant();
            if (n.Contains("powershell") || n.Contains("posh"))
                caps.Add("PowerShell");
        }

        return caps.ToList();
    }
}
