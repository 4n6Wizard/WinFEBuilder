using WinFEBuilder.Core.Models;

namespace WinFEBuilder.Core.Services;

public interface IAdkDetectionService
{
    /// <summary>Detect the Windows ADK + WinPE add-on across registry, Program Files, and env vars.</summary>
    AdkInstallation Detect();
}
