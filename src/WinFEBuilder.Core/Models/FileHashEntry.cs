namespace WinFEBuilder.Core.Models;

/// <summary>SHA-256 hash record for a single file.</summary>
public sealed class FileHashEntry
{
    public required string RelativePath { get; init; }
    public required string FullPath { get; init; }
    public long SizeBytes { get; init; }
    public string Sha256 { get; init; } = string.Empty;
    public DateTimeOffset LastWriteUtc { get; init; }
}
