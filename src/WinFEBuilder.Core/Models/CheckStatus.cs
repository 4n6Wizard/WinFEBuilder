namespace WinFEBuilder.Core.Models;

/// <summary>
/// Status displayed on dashboard status cards and audit items.
/// Deliberately distinct from operational validation states (see <see cref="ValidationStatus"/>).
/// </summary>
public enum CheckStatus
{
    NotConfigured = 0,
    Pass = 1,
    Warning = 2,
    Fail = 3
}
