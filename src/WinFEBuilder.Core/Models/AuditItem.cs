namespace WinFEBuilder.Core.Models;

/// <summary>A single dashboard/environment audit line item.</summary>
public sealed class AuditItem
{
    public required string Key { get; init; }
    public required string Name { get; init; }
    public CheckStatus Status { get; set; } = CheckStatus.NotConfigured;

    /// <summary>Short one-line summary shown on the card.</summary>
    public string Summary { get; set; } = string.Empty;

    /// <summary>Full explanation shown when the user clicks the item.</summary>
    public string Details { get; set; } = string.Empty;

    /// <summary>Actionable guidance shown when the item is WARNING/FAIL.</summary>
    public string? RecommendedAction { get; set; }
}
