namespace WinFEBuilder.Core.Models;

/// <summary>
/// Structured result returned by every non-trivial operation, per the project rules.
/// Carries success/status, human message, technical detail, timing, exit code,
/// output paths, warnings, and recommended corrective action.
/// </summary>
public sealed class OperationResult
{
    public bool Success { get; init; }
    public CheckStatus Status { get; init; } = CheckStatus.NotConfigured;
    public string Message { get; init; } = string.Empty;
    public string? TechnicalDetails { get; init; }
    public string? ExceptionDetails { get; init; }
    public int? ExitCode { get; init; }
    public DateTimeOffset? StartTime { get; init; }
    public DateTimeOffset? FinishTime { get; init; }

    public TimeSpan? Duration =>
        (StartTime.HasValue && FinishTime.HasValue) ? FinishTime - StartTime : null;

    public IReadOnlyList<string> OutputPaths { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();

    /// <summary>Actionable guidance to show the user when things fail.</summary>
    public string? RecommendedAction { get; init; }

    public static OperationResult Ok(string message, string? technical = null,
        IReadOnlyList<string>? outputs = null, IReadOnlyList<string>? warnings = null,
        DateTimeOffset? start = null, DateTimeOffset? finish = null, int? exitCode = null) => new()
    {
        Success = true,
        Status = (warnings is { Count: > 0 }) ? CheckStatus.Warning : CheckStatus.Pass,
        Message = message,
        TechnicalDetails = technical,
        OutputPaths = outputs ?? Array.Empty<string>(),
        Warnings = warnings ?? Array.Empty<string>(),
        StartTime = start,
        FinishTime = finish,
        ExitCode = exitCode
    };

    public static OperationResult Fail(string message, string? technical = null,
        string? recommendedAction = null, string? exception = null,
        DateTimeOffset? start = null, DateTimeOffset? finish = null, int? exitCode = null) => new()
    {
        Success = false,
        Status = CheckStatus.Fail,
        Message = message,
        TechnicalDetails = technical,
        RecommendedAction = recommendedAction,
        ExceptionDetails = exception,
        StartTime = start,
        FinishTime = finish,
        ExitCode = exitCode
    };
}
