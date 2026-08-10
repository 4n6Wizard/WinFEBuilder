using System.Text;
using System.Text.Json;

namespace WinFEBuilder.Core.Logging;

/// <summary>
/// Thread-safe file logger. Human-readable log is line-formatted; the JSON log is
/// written as JSON Lines (one JSON object per line) so it can be appended safely and
/// parsed later for reports.
/// </summary>
public sealed class LogService : ILogService
{
    private readonly object _gate = new();
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public string TextLogPath { get; }
    public string JsonLogPath { get; }

    public event EventHandler<LogEntry>? EntryLogged;

    /// <summary>
    /// <paramref name="logDirectory"/> is the session folder (see <c>AppPaths.SessionLogDir</c>), so
    /// the file names carry no timestamp — the folder already does. Pass a
    /// <paramref name="sessionStamp"/> only when writing into a shared folder, where the names must
    /// stay unique.
    /// </summary>
    public LogService(string logDirectory, string? sessionStamp = null)
    {
        if (string.IsNullOrWhiteSpace(logDirectory))
            throw new ArgumentException("Log directory is required.", nameof(logDirectory));

        Directory.CreateDirectory(logDirectory);
        var suffix = string.IsNullOrWhiteSpace(sessionStamp) ? "" : $"_{sessionStamp}";
        TextLogPath = Path.Combine(logDirectory, $"winfebuilder{suffix}.log");
        JsonLogPath = Path.Combine(logDirectory, $"winfebuilder{suffix}.jsonl");
    }

    public void Log(LogEntry entry)
    {
        var line = $"{entry.Timestamp:yyyy-MM-dd HH:mm:ss} [{Format(entry.Severity)}] {entry.Message}";
        lock (_gate)
        {
            try
            {
                File.AppendAllText(TextLogPath, line + Environment.NewLine, Encoding.UTF8);
                File.AppendAllText(JsonLogPath, JsonSerializer.Serialize(entry, JsonOptions) + Environment.NewLine, Encoding.UTF8);
            }
            catch
            {
                // Logging must never throw and take down an operation.
            }
        }

        EntryLogged?.Invoke(this, entry);
    }

    private static string Format(LogSeverity s) => s switch
    {
        LogSeverity.Pass => "PASS",
        LogSeverity.Fail => "FAIL",
        LogSeverity.Warning => "WARNING",
        LogSeverity.Error => "ERROR",
        LogSeverity.Debug => "DEBUG",
        _ => "INFO"
    };

    public void Debug(string operation, string message) =>
        Log(new LogEntry { Severity = LogSeverity.Debug, Operation = operation, Message = message });

    public void Info(string operation, string message) =>
        Log(new LogEntry { Severity = LogSeverity.Info, Operation = operation, Message = message });

    public void Pass(string operation, string message) =>
        Log(new LogEntry { Severity = LogSeverity.Pass, Operation = operation, Message = message });

    public void Warning(string operation, string message, string? recommendedAction = null) =>
        Log(new LogEntry
        {
            Severity = LogSeverity.Warning,
            Operation = operation,
            Message = recommendedAction is null ? message : $"{message} (Action: {recommendedAction})"
        });

    public void Fail(string operation, string message, string? exception = null) =>
        Log(new LogEntry { Severity = LogSeverity.Fail, Operation = operation, Message = message, ExceptionDetails = exception });

    public void Error(string operation, string message, Exception ex) =>
        Log(new LogEntry
        {
            Severity = LogSeverity.Error,
            Operation = operation,
            Message = message,
            ExceptionDetails = ex.ToString()
        });
}
