namespace WinFEBuilder.Core.Models;

/// <summary>A tool folder that lives inside the framework's USB\x86-x64\tools\&lt;arch&gt; folder.</summary>
public sealed class FrameworkTool
{
    public required string Name { get; init; }
    public required string Architecture { get; init; }   // "x64" or "x86"
    public required string Path { get; init; }
    public int FileCount { get; init; }
    public long SizeBytes { get; init; }

    public string SizeText => SizeBytes <= 0 ? "—" : $"{SizeBytes / 1024d / 1024d:F1} MB";
}
