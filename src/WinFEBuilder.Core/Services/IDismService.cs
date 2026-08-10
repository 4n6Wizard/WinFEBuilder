using WinFEBuilder.Core.Models;

namespace WinFEBuilder.Core.Services;

public interface IDismService
{
    /// <summary>Resolve a usable DISM executable path (ADK DISM preferred, in-box fallback).</summary>
    string? ResolveDismPath();

    /// <summary>Inspect a .wim read-only via DISM /Get-WimInfo (does not mount the image).</summary>
    Task<WimInfo> GetWimInfoAsync(string wimPath, CancellationToken ct = default);
}
