using System.Drawing;
using System.Runtime.InteropServices;
using WinFEBuilder.Core.Logging;

namespace WinFEBuilder.App.Controls;

/// <summary>Live log panel that subscribes to <see cref="ILogService"/> and appends entries safely.</summary>
/// <remarks>
/// Entries arrive from background build threads at a very high rate (DISM and oscdimg
/// emit thousands of lines). Two things therefore matter here:
/// <list type="bullet">
/// <item>Entries are buffered and flushed on a UI timer instead of marshaling one
/// callback per entry, which used to saturate the message queue and hang the form.</item>
/// <item>The visible buffer is bounded. An unbounded RichTextBox eventually fails
/// inside the native RichEdit control, which surfaced as an AccessViolationException
/// when setting SelectionStart. The full log always remains on disk at
/// <see cref="ILogService.TextLogPath"/>.</item>
/// </list>
/// </remarks>
public sealed class LiveLogPanel : UserControl
{
    /// <summary>Maximum entries kept in the visible buffer.</summary>
    private const int MaxEntries = 2000;

    /// <summary>Entries discarded at once when <see cref="MaxEntries"/> is exceeded.</summary>
    private const int TrimChunk = 500;

    /// <summary>Flush cadence. Fast enough to read as "live", slow enough to batch.</summary>
    private const int FlushIntervalMs = 100;

    private readonly ILogService _log;
    private readonly RichTextBox _box = new();
    private readonly System.Windows.Forms.Timer _flushTimer = new();

    /// <summary>Entries received but not yet rendered. Guarded by <see cref="_pendingLock"/>.</summary>
    private readonly List<LogEntry> _pending = new();
    private readonly object _pendingLock = new();

    /// <summary>The entries currently rendered, oldest first. UI thread only.</summary>
    private readonly Queue<LogEntry> _visible = new();

    private bool _trimmed;

    public LiveLogPanel(ILogService log)
    {
        _log = log;

        var header = new Label
        {
            Text = "Live log",
            Dock = DockStyle.Top,
            Font = UiTheme.Subheading,
            ForeColor = UiTheme.TextPrimary,
            Height = 26,
            Padding = new Padding(4, 4, 0, 0)
        };

        _box.Dock = DockStyle.Fill;
        _box.ReadOnly = true;
        _box.Font = UiTheme.Mono;
        _box.BackColor = Color.FromArgb(17, 24, 39);
        _box.ForeColor = Color.FromArgb(229, 231, 235);
        _box.BorderStyle = BorderStyle.FixedSingle;
        _box.WordWrap = false;
        _box.DetectUrls = false;
        _box.MaxLength = int.MaxValue;
        _box.AccessibleName = "Live log output";

        Controls.Add(_box);
        Controls.Add(header);

        _flushTimer.Interval = FlushIntervalMs;
        _flushTimer.Tick += (_, _) => Flush();

        _log.EntryLogged += OnEntryLogged;
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        _flushTimer.Start();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _log.EntryLogged -= OnEntryLogged;
            _flushTimer.Stop();
            _flushTimer.Dispose();
        }

        base.Dispose(disposing);
    }

    /// <summary>
    /// Called on arbitrary threads. Does no UI work: it only queues the entry so the
    /// caller (a build worker) is never blocked by rendering.
    /// </summary>
    private void OnEntryLogged(object? sender, LogEntry e)
    {
        lock (_pendingLock)
        {
            _pending.Add(e);

            // The producer can outrun the flush timer during a long DISM stage. Cap the
            // queue so a stalled UI thread cannot grow it without bound.
            if (_pending.Count > MaxEntries * 4)
            {
                _pending.RemoveRange(0, _pending.Count - MaxEntries);
                _trimmed = true;
            }
        }
    }

    /// <summary>Renders everything queued since the previous tick. UI thread only.</summary>
    private void Flush()
    {
        if (IsDisposed || _box.IsDisposed || !_box.IsHandleCreated) return;

        LogEntry[] batch;
        bool trimmedWhileQueued;

        lock (_pendingLock)
        {
            if (_pending.Count == 0) return;
            batch = _pending.ToArray();
            _pending.Clear();
            trimmedWhileQueued = _trimmed;
            _trimmed = false;
        }

        foreach (var entry in batch)
        {
            _visible.Enqueue(entry);
        }

        // Drop in chunks rather than one entry at a time so the full redraw below is
        // amortized over TrimChunk entries instead of running on every flush.
        var needsRedraw = trimmedWhileQueued;
        if (_visible.Count > MaxEntries)
        {
            var target = Math.Max(0, MaxEntries - TrimChunk);
            while (_visible.Count > target)
            {
                _visible.Dequeue();
            }
            needsRedraw = true;
        }

        try
        {
            if (needsRedraw)
            {
                Render(_visible.ToArray(), replaceAll: true);
            }
            else
            {
                Render(batch, replaceAll: false);
            }
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or ExternalException)
        {
            // The live panel is a convenience view; the authoritative log is on disk.
            // Never let a rendering failure take down a build in progress.
            _box.Clear();
            _visible.Clear();
            _box.AppendText($"[live log reset after render error: {ex.Message}]{Environment.NewLine}");
        }
    }

    /// <summary>
    /// Appends <paramref name="entries"/>, coalescing consecutive entries that share a
    /// color into a single colored run. Each run costs one selection change, so a batch
    /// of 500 same-severity lines becomes one operation rather than 500.
    /// </summary>
    private void Render(IReadOnlyList<LogEntry> entries, bool replaceAll)
    {
        if (entries.Count == 0 && !replaceAll) return;

        if (replaceAll)
        {
            _box.Clear();
        }

        var run = new System.Text.StringBuilder();
        var runColor = entries.Count > 0 ? ColorFor(entries[0].Severity) : Color.Empty;

        foreach (var entry in entries)
        {
            var color = ColorFor(entry.Severity);
            if (color != runColor && run.Length > 0)
            {
                AppendRun(run, runColor);
                runColor = color;
            }

            run.Append(entry.Timestamp.ToString("HH:mm:ss"))
               .Append(" [")
               .Append(entry.Severity.ToString().ToUpperInvariant())
               .Append("] ")
               .Append(entry.Message)
               .Append(Environment.NewLine);
        }

        if (run.Length > 0)
        {
            AppendRun(run, runColor);
        }

        // One scroll per flush instead of one per entry; ScrollToCaret forces a layout
        // pass and was the single most expensive part of the old per-line append.
        _box.SelectionStart = _box.TextLength;
        _box.SelectionLength = 0;
        _box.ScrollToCaret();
    }

    private void AppendRun(System.Text.StringBuilder run, Color color)
    {
        _box.SelectionStart = _box.TextLength;
        _box.SelectionLength = 0;
        _box.SelectionColor = color;
        _box.AppendText(run.ToString());
        run.Clear();
    }

    private static Color ColorFor(LogSeverity severity) => severity switch
    {
        LogSeverity.Pass => Color.FromArgb(74, 222, 128),
        LogSeverity.Warning => Color.FromArgb(250, 204, 21),
        LogSeverity.Fail or LogSeverity.Error => Color.FromArgb(248, 113, 113),
        LogSeverity.Debug => Color.FromArgb(148, 163, 184),
        _ => Color.FromArgb(229, 231, 235)
    };
}
