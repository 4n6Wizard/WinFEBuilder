using WinFEBuilder.Core.Configuration;
using WinFEBuilder.Core.Models;
using WinFEBuilder.Core.Services;
using WinFEBuilder.Core.Validation;

namespace WinFEBuilder.App.ViewModels;

/// <summary>UI-agnostic wrapper over disk enumeration, eligibility, and guarded USB creation.</summary>
public sealed class UsbViewModel
{
    private readonly IDiskService _disk;
    private readonly ISettingsService _settings;

    public UsbViewModel(IDiskService disk, ISettingsService settings)
    {
        _disk = disk;
        _settings = settings;
    }

    public bool SimulationMode => _disk.SimulationMode;

    public Task<List<DiskInfo>> EnumerateAsync(bool includeNonRemovable, CancellationToken ct)
        => _disk.EnumerateDisksAsync(includeNonRemovable, ct);

    public DiskEligibility Evaluate(DiskInfo disk, bool allowNonRemovable)
        => _disk.Evaluate(disk, allowNonRemovable);

    public Task<DiskInfo?> RefreshAsync(int number, CancellationToken ct)
        => _disk.RefreshDiskAsync(number, ct);

    public Task<UsbCreationResult> CreateAsync(UsbBuildRequest request, IProgress<string> progress, CancellationToken ct)
        => _disk.CreateUsbAsync(request, progress, ct);

    /// <summary>Run a sequential multi-USB batch (never parallel).</summary>
    public Task<UsbBatchResult> RunBatchAsync(UsbBatchRequest request, IProgress<string> log, IProgress<UsbBatchProgress> batch, CancellationToken ct)
        => _disk.RunUsbBatchAsync(request, log, batch, ct);

    public string ExpectedPhrase(int diskNumber) => ConfirmationPhraseValidator.BuildExpectedPhrase(diskNumber);
    public bool PhraseValid(string? typed, int diskNumber) => ConfirmationPhraseValidator.IsValid(typed, diskNumber);

    /// <summary>Batch-aware expected phrase: "ERASE DISK n" for one, "ERASE N DISKS" for many.</summary>
    public string ExpectedBatchPhrase(IReadOnlyList<int> diskNumbers) => BatchConfirmationValidator.Expected(diskNumbers);
    public bool BatchPhraseValid(string? typed, IReadOnlyList<int> diskNumbers) => BatchConfirmationValidator.IsValid(typed, diskNumbers);

    /// <summary>Best-effort auto-detect of the newest built, deployable media root under the workspace.</summary>
    public string? AutoDetectMediaSource()
    {
        try
        {
            var ws = _settings.Settings.WorkspaceRoot;
            if (!Directory.Exists(ws)) return null;

            // Find the newest boot.wim, then walk up to the folder that holds Boot\ EFI\ Sources\
            // (the deployable media root). This handles combined multi-arch layouts where boot.wim
            // lives in a nested <arch>\sources folder.
            var newestBootWim = Directory.EnumerateFiles(ws, "boot.wim", SearchOption.AllDirectories)
                .Where(f => f.Replace('/', '\\').EndsWith(@"\sources\boot.wim", StringComparison.OrdinalIgnoreCase))
                .Select(f => new FileInfo(f))
                .OrderByDescending(fi => fi.LastWriteTimeUtc)
                .FirstOrDefault();
            if (newestBootWim is null) return null;

            // Identify the build folder (directly under the workspace root) that owns this boot.wim.
            var buildRoot = newestBootWim.Directory;
            while (buildRoot?.Parent is not null &&
                   !string.Equals(buildRoot.Parent.FullName.TrimEnd('\\'), ws.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
            {
                buildRoot = buildRoot.Parent;
            }

            // Prefer the SHALLOWEST media root (the combined x86+x64 root, e.g. USB\x86-x64),
            // not a nested single-arch sub-media. FindMediaRoot orders by path length.
            var scope = buildRoot?.FullName ?? ws;
            var allDirs = Directory.EnumerateDirectories(scope, "*", SearchOption.AllDirectories);
            var combinedRoot = MediaLocator.FindMediaRoot(allDirs);
            if (combinedRoot is not null) return combinedRoot;

            // Fallback: the folder directly above \sources.
            return MediaLocator.MediaRootFromBootWim(newestBootWim.FullName);
        }
        catch
        {
            return null;
        }
    }
}
