namespace WinFEBuilder.Core.Logging;

public enum LogSeverity
{
    Debug,
    Info,
    Pass,
    Warning,
    Fail,
    Error
}

/// <summary>Structured log entry serialized to the JSON log.</summary>
public sealed class LogEntry
{
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.Now;
    public LogSeverity Severity { get; init; } = LogSeverity.Info;
    public string Operation { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public string? Command { get; init; }
    public int? ExitCode { get; init; }
    public double? DurationMs { get; init; }
    public string? RelatedPath { get; init; }

    /// <summary>Disk identity, only populated for disk operations (later milestones).</summary>
    public string? DiskIdentity { get; init; }

    public string? ExceptionDetails { get; init; }
}
