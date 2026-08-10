using WinFEBuilder.Core.Configuration;
using WinFEBuilder.Core.Hashing;
using WinFEBuilder.Core.Logging;
using WinFEBuilder.Core.Models;

namespace WinFEBuilder.Core.Services;

/// <summary>
/// Copies folders into a boot.wim with DISM. Mirrors <see cref="DriverService"/>'s discipline: the
/// mount is always released — commit on success, discard on any failure — so an image is never left
/// mounted silently.
/// <para>
/// This exists for content Microsoft ships no WinPE package for. The driving case is modern .NET:
/// <c>WinPE-NetFx</c> installs .NET <b>Framework</b> 4.x, and nothing installs .NET 5/6/8/9/10, so a
/// tool built on it must have its runtime placed into the image. Arsenal Recon documents exactly this
/// for AIM Remote Agent: copy the runtime to <c>Program Files\dotnet</c> and the tools to
/// <c>Program Files\AIMTools</c>.
/// </para>
/// </summary>
public sealed class ImageContentService : IImageContentService
{
    private readonly ILogService _log;
    private readonly IProcessRunner _runner;
    private readonly IDismService _dism;
    private readonly IHashService _hash;
    private readonly AppPaths _paths;

    public ImageContentService(ILogService log, IProcessRunner runner, IDismService dism, IHashService hash, AppPaths paths)
    {
        _log = log;
        _runner = runner;
        _dism = dism;
        _hash = hash;
        _paths = paths;
    }

    public ImageContentItem Describe(string sourcePath, string destinationRelative, string? label = null)
    {
        var item = new ImageContentItem
        {
            SourcePath = sourcePath,
            DestinationRelative = destinationRelative.Replace('/', '\\').Trim('\\'),
            Label = label
        };

        try
        {
            if (Directory.Exists(sourcePath))
            {
                var files = Directory.EnumerateFiles(sourcePath, "*", SearchOption.AllDirectories).ToList();
                item.FileCount = files.Count;
                item.Bytes = files.Sum(f => { try { return new FileInfo(f).Length; } catch { return 0L; } });
            }
        }
        catch (Exception ex)
        {
            _log.Debug("Image", $"Could not size '{sourcePath}': {ex.Message}");
        }

        return item;
    }

    public async Task<ImageContentResult> ApplyAsync(
        string bootWimPath,
        IEnumerable<ImageContentItem> items,
        bool compactAfterwards,
        IProgress<string>? progress,
        CancellationToken ct = default)
    {
        var result = new ImageContentResult { BootWimPath = bootWimPath };
        void Report(string m) { progress?.Report(m); _log.Info("Image", m); }

        var selected = items?.Where(i => i.Selected).ToList() ?? new List<ImageContentItem>();

        var dism = _dism.ResolveDismPath();
        if (dism is null)
        {
            result.Errors.Add("DISM not found.");
            result.RecommendedAction = "Install the Windows ADK (1803 or 1809).";
            return result;
        }
        if (!File.Exists(bootWimPath)) { result.Errors.Add("boot.wim not found."); return result; }
        if (selected.Count == 0) { result.Errors.Add("Nothing selected to add."); return result; }

        // Validate every destination before touching the image: a rooted or traversing path would
        // write to the host filesystem instead of the mount.
        foreach (var i in selected)
        {
            if (!Directory.Exists(i.SourcePath))
            {
                result.Errors.Add($"Source folder not found: {i.SourcePath}");
                return result;
            }
            if (!ImageContentItem.IsSafeDestination(i.DestinationRelative, out var why))
            {
                result.Errors.Add($"Unsafe destination '{i.DestinationRelative}': {why}");
                return result;
            }
        }

        var logPath = Path.Combine(_paths.SessionLogDir, $"dism-imagecontent_{DateTime.Now:HHmmss}.log");
        result.DismLogPath = logPath;

        try { result.BytesBefore = new FileInfo(bootWimPath).Length; } catch { }
        try { result.Sha256Before = await _hash.ComputeSha256Async(bootWimPath, ct).ConfigureAwait(false); } catch { }

        Report("Cleaning up any stale DISM mounts…");
        await CleanupMountsAsync(ct).ConfigureAwait(false);

        var mountDir = Path.Combine(Path.GetTempPath(), $"winfe_content_{Guid.NewGuid():N}");
        Directory.CreateDirectory(mountDir);
        result.MountDirectory = mountDir;

        try
        {
            Report($"Mounting boot.wim → {mountDir}");
            var mount = await RunDismAsync(new[] { "/Mount-Wim", $"/WimFile:{bootWimPath}", "/index:1", $"/MountDir:{mountDir}", $"/LogPath:{logPath}" }, ct).ConfigureAwait(false);
            if (mount.ExitCode != 0)
            {
                result.Errors.Add($"Mount failed (exit {mount.ExitCode}).");
                result.RecommendedAction = "Ensure the boot.wim isn't in use and DISM mounts are clean.";
                return result;
            }
            result.ImageMounted = true;

            foreach (var item in selected)
            {
                ct.ThrowIfCancellationRequested();
                var dest = Path.Combine(mountDir, item.DestinationRelative);

                // Belt and braces: the combined path must still be inside the mount.
                var mountFull = Path.GetFullPath(mountDir).TrimEnd('\\') + "\\";
                if (!Path.GetFullPath(dest).StartsWith(mountFull, StringComparison.OrdinalIgnoreCase))
                {
                    result.Copied.Add(new ImageContentCopyResult
                    {
                        SourcePath = item.SourcePath,
                        DestinationRelative = item.DestinationRelative,
                        Success = false,
                        Error = "Destination resolved outside the mounted image."
                    });
                    result.Warnings.Add($"Skipped '{item.DestinationRelative}' — resolved outside the image.");
                    continue;
                }

                Report($"Copying {item.Label ?? item.SourceName} → \\{item.DestinationRelative}");
                var copy = CopyTree(item.SourcePath, dest, ct);
                copy.SourcePath = item.SourcePath;
                copy.DestinationRelative = item.DestinationRelative;
                result.Copied.Add(copy);

                if (!copy.Success)
                    result.Warnings.Add($"'{item.DestinationRelative}' failed: {copy.Error}");
            }

            if (result.ItemsCopied == 0)
            {
                result.Errors.Add("No content was copied; discarding changes.");
                await DiscardAsync(mountDir, logPath, ct).ConfigureAwait(false);
                result.ImageUnmounted = true;
                return result;
            }

            Report($"Committing changes and unmounting ({result.AddedMegabytes:F1} MB added)…");
            var unmount = await RunDismAsync(new[] { "/Unmount-Wim", $"/MountDir:{mountDir}", "/Commit", $"/LogPath:{logPath}" }, ct).ConfigureAwait(false);
            if (unmount.ExitCode != 0)
            {
                result.Errors.Add($"Commit/unmount failed (exit {unmount.ExitCode}); discarding changes.");
                await DiscardAsync(mountDir, logPath, ct).ConfigureAwait(false);
                result.ImageUnmounted = true;
                result.RecommendedAction = "See the DISM log; changes were discarded to protect the image.";
                return result;
            }
            result.Committed = true;
            result.ImageUnmounted = true;

            if (compactAfterwards)
                await CompactAsync(result, bootWimPath, logPath, Report, ct).ConfigureAwait(false);

            try { result.BytesAfter = new FileInfo(bootWimPath).Length; } catch { }
            try { result.Sha256After = await _hash.ComputeSha256Async(bootWimPath, ct).ConfigureAwait(false); } catch { }

            result.Success = true;
            Report($"Added {result.ItemsCopied} item(s) to boot.wim; {result.ItemsFailed} failed. " +
                   $"Image is now {result.BytesAfter / 1024d / 1024d:F1} MB — loaded into RAM at every boot.");
            return result;
        }
        catch (OperationCanceledException)
        {
            result.Errors.Add("Canceled — discarding changes.");
            await SafeDiscardAsync(result, mountDir, logPath).ConfigureAwait(false);
            return result;
        }
        catch (Exception ex)
        {
            _log.Error("Image", "Adding content to the image failed.", ex);
            result.Errors.Add(ex.Message);
            await SafeDiscardAsync(result, mountDir, logPath).ConfigureAwait(false);
            return result;
        }
        finally
        {
            if (result.ImageMounted && !result.ImageUnmounted)
            {
                result.Warnings.Add("Image was still mounted at cleanup — discarding.");
                await SafeDiscardAsync(result, mountDir, logPath).ConfigureAwait(false);
            }
            try { if (Directory.Exists(mountDir) && !Directory.EnumerateFileSystemEntries(mountDir).Any()) Directory.Delete(mountDir); } catch { }
        }
    }

    /// <summary>
    /// Rebuilds the image so it contains only referenced data. Servicing leaves the previous versions
    /// of everything it touched in the WIM as orphaned resources (7-Zip shows them under [DELETED]).
    /// <para>
    /// /Compress:max is deliberate — it is LZX, which bootmgr can boot. /Compress:recovery (LZMS)
    /// produces a smaller file that will NOT boot, which is the worst kind of failure here: media
    /// that looks built and isn't.
    /// </para>
    /// Failure is non-fatal: the committed image is already valid, so a failed compaction is a warning.
    /// </summary>
    private async Task CompactAsync(ImageContentResult result, string bootWimPath, string logPath,
        Action<string> report, CancellationToken ct)
    {
        var temp = bootWimPath + ".compact";
        try
        {
            report("Compacting image (removing data orphaned by servicing)…");
            if (File.Exists(temp)) File.Delete(temp);

            var export = await RunDismAsync(new[]
            {
                "/Export-Image",
                $"/SourceImageFile:{bootWimPath}",
                "/SourceIndex:1",
                $"/DestinationImageFile:{temp}",
                "/Compress:max",
                $"/LogPath:{logPath}"
            }, ct).ConfigureAwait(false);

            if (export.ExitCode != 0 || !File.Exists(temp))
            {
                result.Warnings.Add($"Compaction skipped (DISM exit {export.ExitCode}); the committed image is unchanged and valid.");
                if (File.Exists(temp)) { try { File.Delete(temp); } catch { } }
                return;
            }

            // Validate the exported image before replacing the original.
            var info = await _dism.GetWimInfoAsync(temp, ct).ConfigureAwait(false);
            if (info is null)
            {
                result.Warnings.Add("Compaction produced an image DISM could not read; keeping the original.");
                try { File.Delete(temp); } catch { }
                return;
            }

            var before = new FileInfo(bootWimPath).Length;
            var after = new FileInfo(temp).Length;

            File.Delete(bootWimPath);
            File.Move(temp, bootWimPath);

            result.Compacted = true;
            result.BytesReclaimed = Math.Max(0, before - after);
            report($"Compacted: {before / 1024d / 1024d:F1} MB → {after / 1024d / 1024d:F1} MB " +
                   $"(reclaimed {result.BytesReclaimed / 1024d / 1024d:F1} MB).");
        }
        catch (Exception ex)
        {
            result.Warnings.Add($"Compaction failed ({ex.Message}); the committed image is unchanged and valid.");
            if (File.Exists(temp)) { try { File.Delete(temp); } catch { } }
        }
    }

    private ImageContentCopyResult CopyTree(string source, string destination, CancellationToken ct)
    {
        var res = new ImageContentCopyResult();
        try
        {
            Directory.CreateDirectory(destination);

            foreach (var dir in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
            {
                ct.ThrowIfCancellationRequested();
                Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, dir)));
            }

            foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            {
                ct.ThrowIfCancellationRequested();
                var target = Path.Combine(destination, Path.GetRelativePath(source, file));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(file, target, overwrite: true);
                res.FileCount++;
                try { res.Bytes += new FileInfo(target).Length; } catch { }
            }

            res.Success = res.FileCount > 0;
            if (!res.Success) res.Error = "Source folder contained no files.";
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            res.Success = false;
            res.Error = ex.Message;
        }
        return res;
    }

    private async Task SafeDiscardAsync(ImageContentResult result, string mountDir, string logPath)
    {
        try
        {
            await DiscardAsync(mountDir, logPath, CancellationToken.None).ConfigureAwait(false);
            result.ImageUnmounted = true;
        }
        catch (Exception ex)
        {
            _log.Error("Image", "Discard failed; running cleanup-mountpoints.", ex);
            try { await CleanupMountsAsync(CancellationToken.None).ConfigureAwait(false); } catch { }
        }
    }

    private Task DiscardAsync(string mountDir, string logPath, CancellationToken ct)
        => RunDismAsync(new[] { "/Unmount-Wim", $"/MountDir:{mountDir}", "/Discard", $"/LogPath:{logPath}" }, ct);

    private Task CleanupMountsAsync(CancellationToken ct)
        => RunDismAsync(new[] { "/Cleanup-Wim" }, ct);

    private Task<ProcessRunResult> RunDismAsync(IEnumerable<string> args, CancellationToken ct)
    {
        var dism = _dism.ResolveDismPath() ?? "dism.exe";
        var full = new List<string> { "/English" };
        full.AddRange(args);
        return _runner.RunAsync(dism, full, timeoutMs: 1_800_000,
            onOutputLine: ProcessOutputFilter.Wrap(l => _log.Debug("Image", l)),
            onErrorLine: l => _log.Warning("Image", l),
            ct: ct);
    }
}
