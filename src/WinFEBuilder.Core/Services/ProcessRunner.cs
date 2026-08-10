using System.Diagnostics;
using System.Text;
using WinFEBuilder.Core.Logging;

namespace WinFEBuilder.Core.Services;

/// <summary>
/// Safe external-process runner. Uses ProcessStartInfo with UseShellExecute=false,
/// redirected stdout/stderr, hidden window, explicit working directory, and an argument
/// list (never a concatenated shell string).
/// </summary>
public sealed class ProcessRunner : IProcessRunner
{
    private readonly ILogService _log;

    public ProcessRunner(ILogService log) => _log = log;

    public async Task<ProcessRunResult> RunAsync(
        string fileName,
        IEnumerable<string> arguments,
        string? workingDirectory = null,
        int? timeoutMs = null,
        Action<string>? onOutputLine = null,
        Action<string>? onErrorLine = null,
        bool closeStandardInput = false,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);

        var argList = arguments?.ToList() ?? new List<string>();

        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = closeStandardInput,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        foreach (var a in argList)
            psi.ArgumentList.Add(a);

        if (!string.IsNullOrWhiteSpace(workingDirectory) && Directory.Exists(workingDirectory))
            psi.WorkingDirectory = workingDirectory;

        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        var start = DateTimeOffset.Now;
        bool timedOut = false;
        bool canceled = false;

        using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            stdout.AppendLine(e.Data);
            onOutputLine?.Invoke(e.Data);
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            stderr.AppendLine(e.Data);
            onErrorLine?.Invoke(e.Data);
        };

        _log.Info("Process", $"Starting: {fileName} {string.Join(' ', argList)}");

        try
        {
            if (!process.Start())
                throw new InvalidOperationException($"Failed to start process '{fileName}'.");

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            if (closeStandardInput)
            {
                // Send EOF so interactive prompts (pause / set /p) do not block the build.
                try { process.StandardInput.Close(); } catch { /* ignore */ }
            }

            using var timeoutCts = new CancellationTokenSource();
            if (timeoutMs is > 0)
                timeoutCts.CancelAfter(timeoutMs.Value);

            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

            try
            {
                await process.WaitForExitAsync(linked.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                timedOut = timeoutCts.IsCancellationRequested;
                canceled = ct.IsCancellationRequested;
                TryKill(process);
            }
        }
        catch (Exception ex)
        {
            _log.Error("Process", $"Process '{fileName}' failed to run.", ex);
            var finishErr = DateTimeOffset.Now;
            return new ProcessRunResult
            {
                FileName = fileName,
                Arguments = string.Join(' ', argList),
                WorkingDirectory = psi.WorkingDirectory,
                ExitCode = -1,
                StandardOutput = stdout.ToString(),
                StandardError = stderr.ToString() + Environment.NewLine + ex.Message,
                StartTime = start,
                FinishTime = finishErr
            };
        }

        var finish = DateTimeOffset.Now;
        int exit = -1;
        try { exit = process.HasExited ? process.ExitCode : -1; } catch { /* ignore */ }

        var result = new ProcessRunResult
        {
            FileName = fileName,
            Arguments = string.Join(' ', argList),
            WorkingDirectory = psi.WorkingDirectory,
            ExitCode = exit,
            StandardOutput = stdout.ToString(),
            StandardError = stderr.ToString(),
            StartTime = start,
            FinishTime = finish,
            TimedOut = timedOut,
            Canceled = canceled
        };

        _log.Log(new LogEntry
        {
            Severity = result.Succeeded ? LogSeverity.Pass : LogSeverity.Warning,
            Operation = "Process",
            Message = $"Finished '{fileName}' exit={exit} ({result.Duration.TotalMilliseconds:F0} ms)"
                      + (timedOut ? " [TIMED OUT]" : "") + (canceled ? " [CANCELED]" : ""),
            Command = result.CommandLine,
            ExitCode = exit,
            DurationMs = result.Duration.TotalMilliseconds
        });

        return result;
    }

    public Task<ProcessRunResult> RunPowerShellScriptAsync(
        string powerShellExe,
        string scriptPath,
        IDictionary<string, string>? parameters = null,
        string? workingDirectory = null,
        int? timeoutMs = null,
        Action<string>? onOutputLine = null,
        Action<string>? onErrorLine = null,
        CancellationToken ct = default)
    {
        Validation.PathValidator.EnsureExistingFile(scriptPath);

        var args = new List<string>
        {
            "-NoProfile",
            "-NonInteractive",
            "-ExecutionPolicy", "Bypass",
            "-File", scriptPath
        };

        if (parameters is not null)
        {
            foreach (var kvp in parameters)
            {
                args.Add("-" + kvp.Key);
                args.Add(kvp.Value); // passed as a discrete argument, not concatenated
            }
        }

        return RunAsync(powerShellExe, args, workingDirectory, timeoutMs, onOutputLine, onErrorLine,
            closeStandardInput: false, ct: ct);
    }

    public Task<ProcessRunResult> RunBatchFileAsync(
        string batchFilePath,
        string? workingDirectory = null,
        int? timeoutMs = null,
        Action<string>? onOutputLine = null,
        Action<string>? onErrorLine = null,
        CancellationToken ct = default)
    {
        Validation.PathValidator.EnsureExistingFile(batchFilePath);

        var comspec = Environment.GetEnvironmentVariable("ComSpec")
                      ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe");

        // cmd.exe /c "<batch>" — path passed as a discrete argument, not concatenated into a shell string.
        var args = new List<string> { "/c", batchFilePath };

        var workDir = workingDirectory
                      ?? Path.GetDirectoryName(Path.GetFullPath(batchFilePath));

        return RunAsync(comspec, args, workDir, timeoutMs, onOutputLine, onErrorLine,
            closeStandardInput: true, ct: ct);
    }

    private static void TryKill(Process p)
    {
        try { if (!p.HasExited) p.Kill(entireProcessTree: true); }
        catch { /* best effort */ }
    }
}
