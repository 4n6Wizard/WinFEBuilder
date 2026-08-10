namespace WinFEBuilder.Core.Logging;

/// <summary>
/// Writes both a human-readable text log and a structured JSON (JSONL) log.
/// Raises <see cref="EntryLogged"/> so the UI can show a live log panel.
/// </summary>
public interface ILogService
{
    /// <summary>Fired for every entry so the UI can append to the live log panel.</summary>
    event EventHandler<LogEntry>? EntryLogged;

    string TextLogPath { get; }
    string JsonLogPath { get; }

    void Log(LogEntry entry);

    void Debug(string operation, string message);
    void Info(string operation, string message);
    void Pass(string operation, string message);
    void Warning(string operation, string message, string? recommendedAction = null);
    void Fail(string operation, string message, string? exception = null);
    void Error(string operation, string message, Exception ex);
}
