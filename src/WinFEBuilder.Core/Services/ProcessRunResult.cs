namespace WinFEBuilder.Core.Services;

/// <summary>Captured output of an external process invocation.</summary>
public sealed class ProcessRunResult
{
    public required string FileName { get; init; }
    public required string Arguments { get; init; }
    public string? WorkingDirectory { get; init; }

    public int ExitCode { get; init; }
    public string StandardOutput { get; init; } = string.Empty;
    public string StandardError { get; init; } = string.Empty;

    public DateTimeOffset StartTime { get; init; }
    public DateTimeOffset FinishTime { get; init; }
    public TimeSpan Duration => FinishTime - StartTime;

    public bool TimedOut { get; init; }
    public bool Canceled { get; init; }

    public bool Succeeded => !TimedOut && !Canceled && ExitCode == 0;

    /// <summary>Best-effort single-line command description for logging (never executed).</summary>
    public string CommandLine => $"{FileName} {Arguments}".Trim();
}
