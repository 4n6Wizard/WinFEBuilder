namespace WinFEBuilder.Core.Services;

public interface IProcessRunner
{
    /// <summary>
    /// Run an external process capturing stdout/stderr/exit code and timing.
    /// Arguments are passed as a list (no shell string concatenation).
    /// </summary>
    Task<ProcessRunResult> RunAsync(
        string fileName,
        IEnumerable<string> arguments,
        string? workingDirectory = null,
        int? timeoutMs = null,
        Action<string>? onOutputLine = null,
        Action<string>? onErrorLine = null,
        bool closeStandardInput = false,
        CancellationToken ct = default);

    /// <summary>
    /// Run a Windows batch (.bat/.cmd) file via cmd.exe /c, capturing output. Standard input is
    /// redirected and closed so interactive prompts (e.g. "pause") receive EOF instead of hanging.
    /// </summary>
    Task<ProcessRunResult> RunBatchFileAsync(
        string batchFilePath,
        string? workingDirectory = null,
        int? timeoutMs = null,
        Action<string>? onOutputLine = null,
        Action<string>? onErrorLine = null,
        CancellationToken ct = default);

    /// <summary>Run a PowerShell script file with named parameters, capturing output.</summary>
    Task<ProcessRunResult> RunPowerShellScriptAsync(
        string powerShellExe,
        string scriptPath,
        IDictionary<string, string>? parameters = null,
        string? workingDirectory = null,
        int? timeoutMs = null,
        Action<string>? onOutputLine = null,
        Action<string>? onErrorLine = null,
        CancellationToken ct = default);
}
