namespace WinFEBuilder.Core.Models;

/// <summary>Read-only information about a Windows Imaging (.wim) file, gathered via DISM.</summary>
public sealed class WimInfo
{
    public string WimPath { get; init; } = string.Empty;
    public long SizeBytes { get; set; }
    public string? Sha256 { get; set; }

    /// <summary>Architecture reported by DISM for image index 1 (e.g. "x64").</summary>
    public string? Architecture { get; set; }

    public int ImageCount => Images.Count;
    public List<WimImage> Images { get; } = new();

    /// <summary>True when the DISM /Get-WimInfo command completed successfully.</summary>
    public bool DismSucceeded { get; set; }

    public int? DismExitCode { get; set; }
    public string? DismRawOutput { get; set; }
    public string? Error { get; set; }
}

/// <summary>A single image (index) inside a .wim.</summary>
public sealed class WimImage
{
    public int Index { get; init; }
    public string? Name { get; init; }
    public string? Description { get; init; }
    public string? Architecture { get; init; }
    public long? SizeBytes { get; init; }
}
