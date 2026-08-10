namespace WinFEBuilder.Core.Models;

/// <summary>Result of verifying the WinFE media (boot structure + boot.wim) produced by the build.</summary>
public sealed class MediaValidationResult
{
    /// <summary>Root folder that contains the boot structure (the folder holding \sources\boot.wim).</summary>
    public string? MediaRoot { get; set; }

    /// <summary>Expected boot components and whether each was found.</summary>
    public Dictionary<string, bool> Expected { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>True when every expected boot component is present.</summary>
    public bool StructureValid =>
        Expected.Count > 0 && Expected.Values.All(v => v);

    public string? BootWimPath { get; set; }
    public WimInfo? Wim { get; set; }

    public CheckStatus Status { get; set; } = CheckStatus.NotConfigured;
    public string Summary { get; set; } = string.Empty;
    public List<string> Warnings { get; } = new();
    public string? RecommendedAction { get; set; }
}
