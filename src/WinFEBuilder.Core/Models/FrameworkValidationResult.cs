namespace WinFEBuilder.Core.Models;

/// <summary>Result of validating a selected WinFE framework directory.</summary>
public sealed class FrameworkValidationResult
{
    public string SourcePath { get; init; } = string.Empty;
    public bool IsValid { get; set; }
    public CheckStatus Status { get; set; } = CheckStatus.NotConfigured;

    public bool DirectoryExists { get; set; }
    public bool DirectoryReadable { get; set; }
    public bool SupportsX64 { get; set; }

    /// <summary>True when the selected path appears to be a parent that contains a single nested framework folder.</summary>
    public bool PossibleDoubleNesting { get; set; }

    /// <summary>Batch build scripts discovered in the framework.</summary>
    public List<DiscoveredFile> BuildScripts { get; } = new();

    /// <summary>WinFE executables / components discovered.</summary>
    public List<DiscoveredFile> Components { get; } = new();

    /// <summary>Configuration files discovered.</summary>
    public List<DiscoveredFile> ConfigFiles { get; } = new();

    public List<string> ExpectedItemsFound { get; } = new();
    public List<string> ExpectedItemsMissing { get; } = new();

    public List<string> Warnings { get; } = new();
    public string? RecommendedAction { get; set; }
    public string Summary { get; set; } = string.Empty;
}

/// <summary>A file discovered during framework inspection, with hash + size metadata.</summary>
public sealed class DiscoveredFile
{
    public required string Name { get; init; }
    public required string FullPath { get; init; }
    public required string RelativePath { get; init; }
    public long SizeBytes { get; init; }
    public bool IsZeroBytes => SizeBytes == 0;
    public string? Sha256 { get; set; }
    public string Category { get; init; } = "File";
}
