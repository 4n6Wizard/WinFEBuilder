namespace WinFEBuilder.Core.Models;

/// <summary>Result of locating and validating the generated ISO, and copying it to output.</summary>
public sealed class IsoValidationResult
{
    public bool Found { get; set; }
    public bool Valid { get; set; }

    public string? SourcePath { get; set; }
    public string? DestinationPath { get; set; }
    public long SizeBytes { get; set; }
    public string? Sha256 { get; set; }
    public DateTimeOffset? Timestamp { get; set; }

    public CheckStatus Status { get; set; } = CheckStatus.NotConfigured;
    public string Summary { get; set; } = string.Empty;
    public string? RecommendedAction { get; set; }
}
