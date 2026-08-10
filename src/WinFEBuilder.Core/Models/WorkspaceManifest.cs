namespace WinFEBuilder.Core.Models;

/// <summary>
/// Manifest written into every created workspace, documenting the copy operation
/// and providing an auditable record (hashes, counts, byte totals, metadata).
/// </summary>
public sealed class WorkspaceManifest
{
    public string ManifestVersion { get; init; } = "1.0";
    public string ApplicationVersion { get; init; } = "1.0.0";

    public string OriginalSourcePath { get; init; } = string.Empty;
    public string WorkspacePath { get; init; } = string.Empty;
    public DateTimeOffset CopyDateUtc { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset CopyDateLocal { get; init; } = DateTimeOffset.Now;

    public int FileCount { get; set; }
    public long TotalBytes { get; set; }

    public List<FileHashEntry> Hashes { get; init; } = new();

    /// <summary>Framework metadata detected during validation (scripts, components, x64 support).</summary>
    public FrameworkMetadata Framework { get; init; } = new();

    public string? ComputerName { get; init; }
    public string? Operator { get; init; }
}

/// <summary>Lightweight framework metadata embedded in the manifest.</summary>
public sealed class FrameworkMetadata
{
    public bool SupportsX64 { get; set; }
    public List<string> BuildScripts { get; init; } = new();
    public List<string> Components { get; init; } = new();
    public List<string> Warnings { get; init; } = new();
}
