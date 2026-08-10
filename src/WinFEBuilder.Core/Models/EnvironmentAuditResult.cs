namespace WinFEBuilder.Core.Models;

/// <summary>Aggregate result of a full environment audit.</summary>
public sealed class EnvironmentAuditResult
{
    public DateTimeOffset TimestampUtc { get; init; } = DateTimeOffset.UtcNow;
    public List<AuditItem> Items { get; init; } = new();

    public AdkInstallation? Adk { get; set; }

    public CheckStatus Overall
    {
        get
        {
            if (Items.Count == 0) return CheckStatus.NotConfigured;
            if (Items.Any(i => i.Status == CheckStatus.Fail)) return CheckStatus.Fail;
            if (Items.Any(i => i.Status == CheckStatus.Warning)) return CheckStatus.Warning;
            if (Items.All(i => i.Status == CheckStatus.NotConfigured)) return CheckStatus.NotConfigured;
            return CheckStatus.Pass;
        }
    }

    public AuditItem? Find(string key) =>
        Items.FirstOrDefault(i => string.Equals(i.Key, key, StringComparison.OrdinalIgnoreCase));
}
