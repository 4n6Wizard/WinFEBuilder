namespace WinFEBuilder.Core.Validation;

/// <summary>A WinPE optional component (feature) that can be added to boot.wim via DISM.</summary>
public sealed record WinPeFeature(string Name, string Description, string Cab, int Order);

/// <summary>
/// Catalog of common WinPE optional components. Pure/IO-free. The important one for forensic tools
/// is WinPE-NetFx — the .NET Framework — which FTK Imager and many .NET tools require (otherwise
/// they fail with "mscoree.dll was not found").
/// </summary>
public static class WinPeFeatureCatalog
{
    public static readonly IReadOnlyList<WinPeFeature> All = new[]
    {
        new WinPeFeature("WinPE-WMI",         "WMI (usually already present)",                         "WinPE-WMI.cab",         1),
        new WinPeFeature("WinPE-NetFx",       ".NET Framework — required by FTK Imager & .NET tools",  "WinPE-NetFx.cab",       2),
        new WinPeFeature("WinPE-Scripting",   "Windows Script Host",                                   "WinPE-Scripting.cab",   3),
        new WinPeFeature("WinPE-PowerShell",  "Windows PowerShell (needs .NET + Scripting)",           "WinPE-PowerShell.cab",  4),
        new WinPeFeature("WinPE-StorageWMI",  "Storage cmdlets (needs PowerShell)",                    "WinPE-StorageWMI.cab",  5),
        new WinPeFeature("WinPE-DismCmdlets", "DISM cmdlets (needs PowerShell)",                       "WinPE-DismCmdlets.cab", 6),
    };

    public static WinPeFeature? ByName(string name) =>
        All.FirstOrDefault(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Build the ordered list of candidate .cab paths (base component + its language pack) for the
    /// selected features, resolved against a WinPE_OCs root. Existence is checked by the caller.
    /// </summary>
    public static List<string> CabPaths(string ocRoot, string language, IEnumerable<string> featureNames)
    {
        var result = new List<string>();
        var features = featureNames
            .Select(ByName)
            .Where(f => f is not null)!
            .Cast<WinPeFeature>()
            .OrderBy(f => f.Order);

        foreach (var f in features)
        {
            // Base component first, then its language pack (e.g. en-us\WinPE-NetFx_en-us.cab).
            result.Add(System.IO.Path.Combine(ocRoot, f.Cab));
            var baseName = System.IO.Path.GetFileNameWithoutExtension(f.Cab);
            result.Add(System.IO.Path.Combine(ocRoot, language, $"{baseName}_{language}.cab"));
        }
        return result;
    }
}
