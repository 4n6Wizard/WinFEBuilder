namespace WinFEBuilder.Core.Validation;

/// <summary>
/// A user-facing Windows capability (e.g. ".NET Framework") mapped to the underlying WinPE optional
/// components required to provide it — including their prerequisites. Users choose capabilities;
/// the Microsoft package names are never shown.
/// </summary>
public sealed record WindowsCapability(
    string Key,
    string DisplayName,
    string Description,
    IReadOnlyList<string> FeatureNames);

/// <summary>
/// Maps friendly forensic capabilities to WinPE feature sets and resolves dependencies. This is a
/// presentation/mapping layer over <see cref="WinPeFeatureCatalog"/>; it does not change DISM logic.
/// </summary>
public static class WindowsCapabilityCatalog
{
    public static readonly IReadOnlyList<WindowsCapability> All = new[]
    {
        new WindowsCapability("DotNet", ".NET Framework",
            "Required for applications such as FTK Imager.",
            new[] { "WinPE-WMI", "WinPE-NetFx" }),

        new WindowsCapability("PowerShell", "PowerShell",
            "Enable PowerShell inside WinFE.",
            new[] { "WinPE-WMI", "WinPE-NetFx", "WinPE-Scripting", "WinPE-PowerShell" }),

        new WindowsCapability("ScriptHost", "Windows Script Host",
            "Enable Windows scripting support.",
            new[] { "WinPE-Scripting" }),

        new WindowsCapability("StorageManagement", "Storage Management",
            "Adds advanced storage management tools.",
            new[] { "WinPE-WMI", "WinPE-Scripting", "WinPE-NetFx", "WinPE-PowerShell", "WinPE-StorageWMI" }),

        new WindowsCapability("DeploymentTools", "Deployment Tools",
            "Adds advanced deployment tools.",
            new[] { "WinPE-WMI", "WinPE-NetFx", "WinPE-Scripting", "WinPE-PowerShell", "WinPE-DismCmdlets" }),
    };

    public static WindowsCapability? ByKey(string key) =>
        All.FirstOrDefault(c => string.Equals(c.Key, key, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Resolve selected capability keys to the full, de-duplicated, dependency-ordered set of WinPE
    /// feature names (ordered per <see cref="WinPeFeatureCatalog"/> so prerequisites install first).
    /// </summary>
    public static IReadOnlyList<string> ResolveFeatures(IEnumerable<string> capabilityKeys)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in capabilityKeys ?? Enumerable.Empty<string>())
        {
            var cap = ByKey(key);
            if (cap is null) continue;
            foreach (var f in cap.FeatureNames) set.Add(f);
        }
        // Order by the catalog's install order so dependencies come first.
        return WinPeFeatureCatalog.All
            .Where(f => set.Contains(f.Name))
            .Select(f => f.Name)
            .ToList();
    }

    /// <summary>Friendly display names for selected capabilities (never package names).</summary>
    public static IReadOnlyList<string> DisplayNames(IEnumerable<string> capabilityKeys) =>
        (capabilityKeys ?? Enumerable.Empty<string>())
            .Select(ByKey).Where(c => c is not null).Select(c => c!.DisplayName).ToList();
}
