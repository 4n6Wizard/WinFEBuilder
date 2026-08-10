using WinFEBuilder.Core.Models;
using WinFEBuilder.Core.Validation;

namespace WinFEBuilder.Core.Services;

public interface IDriverService
{
    /// <summary>
    /// Enumerate .inf files under a folder and detect architecture, class, and which Windows builds
    /// their device entries apply to.
    /// </summary>
    /// <param name="targetBuild">
    /// Windows build of the image being serviced — 17763 for a WinPE 1809 image. A driver whose devices
    /// are listed only for a newer build installs cleanly and never binds, so it is flagged here rather
    /// than discovered as missing hardware on a booted machine.
    /// </param>
    Task<List<DriverInfo>> EnumerateDriversAsync(string folder, string targetArch,
        int targetBuild = InfOsApplicability.Adk1809Build, CancellationToken ct = default);

    /// <summary>
    /// Inject selected drivers into a COPY of boot.wim via DISM (mount → add → commit → unmount),
    /// always cleaning up the mount even on failure. Recomputes the hash and re-validates the WIM.
    /// </summary>
    Task<DriverInjectionResult> InjectAsync(string bootWimPath, IEnumerable<DriverInfo> drivers, bool forceUnsigned, IProgress<string>? progress, CancellationToken ct = default);

    /// <summary>
    /// Add WinPE optional components (.cab packages, e.g. WinPE-NetFx) to boot.wim via DISM, with the
    /// same mount → add → commit → unmount safety and guaranteed cleanup as driver injection.
    /// Only cab paths that exist on disk are added.
    /// </summary>
    /// <summary>
    /// Installs WinPE optional components into <paramref name="bootWimPath"/>, then replays
    /// <paramref name="reapplyRegistryPatches"/> before committing.
    /// </summary>
    /// <param name="reapplyRegistryPatches">
    /// The framework's own offline-registry settings (write protection, shell namespace). DISM
    /// package servicing reverts hand-injected registry values, so WinFE requires these to be
    /// written last. Pass null to skip the replay.
    /// </param>
    Task<DriverInjectionResult> AddWinPeFeaturesAsync(
        string bootWimPath,
        IEnumerable<string> cabPaths,
        IProgress<string>? progress,
        IReadOnlyList<WinFeRegistryOperation>? reapplyRegistryPatches = null,
        CancellationToken ct = default);

    /// <summary>Report currently mounted images (DISM /Get-MountedImageInfo).</summary>
    Task<string> GetMountedImagesAsync(CancellationToken ct = default);

    /// <summary>Clean up stale/corrupt mount points (DISM /Cleanup-Mountpoints).</summary>
    Task<bool> CleanupMountsAsync(CancellationToken ct = default);
}
