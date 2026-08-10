using WinFEBuilder.Core.Models;

namespace WinFEBuilder.Core.Services;

public interface IDiskService
{
    /// <summary>Enumerate physical disks (read-only). Includes fake disks when simulation is on.</summary>
    Task<List<DiskInfo>> EnumerateDisksAsync(bool includeNonRemovable, CancellationToken ct = default);

    /// <summary>Re-read a single disk by number (used to revalidate immediately before writing).</summary>
    Task<DiskInfo?> RefreshDiskAsync(int number, CancellationToken ct = default);

    /// <summary>Build the set of protected volumes/paths from the system + app settings.</summary>
    ProtectedContext BuildProtectedContext();

    /// <summary>Evaluate whether a disk is an eligible USB target.</summary>
    DiskEligibility Evaluate(DiskInfo disk, bool allowNonRemovable);

    /// <summary>
    /// Create (or, in simulation mode, only plan) the WinFE USB. Re-verifies disk identity and
    /// eligibility, checks the confirmation phrase + acknowledgement, and NEVER executes a
    /// destructive command in simulation mode or against a simulated/ineligible disk.
    /// </summary>
    Task<UsbCreationResult> CreateUsbAsync(UsbBuildRequest request, IProgress<string>? progress, CancellationToken ct);

    /// <summary>
    /// Shared core: create one WinFE USB on a supplied target disk (confirmation already satisfied by
    /// the caller). Enforces identity/eligibility re-checks and simulation mode. Throws on cancellation.
    /// </summary>
    Task<UsbCreationResult> CreateSingleUsbAsync(DiskInfo target, string mediaSourcePath, string label, IProgress<string>? progress, CancellationToken ct, bool allowFixedDisk = false);

    /// <summary>
    /// Create WinFE USBs on multiple targets sequentially (never in parallel). Per-disk failures do
    /// not stop the batch; global problems (bad media, wrong phrase, no acknowledgement) do.
    /// </summary>
    Task<UsbBatchResult> RunUsbBatchAsync(UsbBatchRequest request, IProgress<string>? log, IProgress<UsbBatchProgress>? batch, CancellationToken ct);

    /// <summary>True when simulation mode is active (no destructive command will run).</summary>
    bool SimulationMode { get; }
}
