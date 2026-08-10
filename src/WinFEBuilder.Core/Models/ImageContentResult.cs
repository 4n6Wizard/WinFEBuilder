namespace WinFEBuilder.Core.Models;

/// <summary>Outcome of copying folders into a boot.wim.</summary>
public sealed class ImageContentResult
{
    public string BootWimPath { get; set; } = "";
    public string? MountDirectory { get; set; }
    public string? DismLogPath { get; set; }

    public bool ImageMounted { get; set; }
    public bool ImageUnmounted { get; set; }
    public bool Committed { get; set; }
    public bool Success { get; set; }

    /// <summary>boot.wim hash before servicing — recorded so the change is explainable later.</summary>
    public string? Sha256Before { get; set; }

    /// <summary>boot.wim hash after commit (and after compaction, if it ran).</summary>
    public string? Sha256After { get; set; }

    public long BytesBefore { get; set; }
    public long BytesAfter { get; set; }

    /// <summary>Set when the image was rebuilt with /Export-Image to drop orphaned data.</summary>
    public bool Compacted { get; set; }
    public long BytesReclaimed { get; set; }

    public List<ImageContentCopyResult> Copied { get; } = new();
    public List<string> Warnings { get; } = new();
    public List<string> Errors { get; } = new();
    public string? RecommendedAction { get; set; }

    public int ItemsCopied => Copied.Count(c => c.Success);
    public int ItemsFailed => Copied.Count(c => !c.Success);
    public long TotalBytesAdded => Copied.Where(c => c.Success).Sum(c => c.Bytes);

    /// <summary>
    /// How much RAM this adds at every boot. WinPE loads boot.wim into memory, so content added here
    /// is not free — worth stating plainly rather than discovering it on low-memory hardware.
    /// </summary>
    public double AddedMegabytes => TotalBytesAdded / 1024d / 1024d;
}

/// <summary>Per-item copy outcome.</summary>
public sealed class ImageContentCopyResult
{
    public string SourcePath { get; set; } = "";
    public string DestinationRelative { get; set; } = "";
    public int FileCount { get; set; }
    public long Bytes { get; set; }
    public bool Success { get; set; }
    public string? Error { get; set; }
}
