using System.Text.RegularExpressions;

namespace WinFEBuilder.Core.Logging;

/// <summary>
/// Decides which lines of a child process's output are worth logging.
/// <para>
/// DISM redraws an ASCII progress bar on every update, so a single operation emits hundreds of lines
/// like <c>[====================100.0%====================]</c>. Logged verbatim they bury the messages
/// that matter, and the live log becomes unreadable — which defeats the point of having one.
/// </para>
/// Completion is already reported by the services' own messages, so nothing is lost by dropping these.
/// </summary>
public static class ProcessOutputFilter
{
    // A progress bar: brackets around any mix of '=', '.', spaces and a percentage.
    private static readonly Regex ProgressBar = new(
        @"^\s*\[[=\.\s]*\d+([.,]\d+)?%[=\.\s]*\]\s*$",
        RegexOptions.Compiled);

    // A bare percentage, with or without decorations, e.g. "100.0%" or "  45% ".
    private static readonly Regex BarePercent = new(
        @"^\s*\d+([.,]\d+)?%\s*$",
        RegexOptions.Compiled);

    // Filler lines DISM and robocopy emit while drawing: only dots, dashes, equals or spaces.
    private static readonly Regex FillerOnly = new(
        @"^[\s\.\-=]+$",
        RegexOptions.Compiled);

    /// <summary>True when a line is progress redraw rather than information.</summary>
    public static bool IsNoise(string? line)
    {
        if (line is null) return true;
        if (line.Trim().Length == 0) return true;

        return ProgressBar.IsMatch(line)
            || BarePercent.IsMatch(line)
            || FillerOnly.IsMatch(line);
    }

    /// <summary>
    /// Wraps a log action so progress redraw is discarded. Use in place of a raw
    /// <c>onOutputLine</c> handler:
    /// <code>onOutputLine: ProcessOutputFilter.Wrap(l => _log.Debug("Build", l))</code>
    /// </summary>
    public static Action<string> Wrap(Action<string> log) =>
        line => { if (!IsNoise(line)) log(line); };
}
