using System.Drawing;
using WinFEBuilder.App.ViewModels;
using WinFEBuilder.Core.Logging;
using WinFEBuilder.Core.Models;

namespace WinFEBuilder.App.Controls;

/// <summary>Framework page: select, validate, and copy a WinFE framework to a workspace.</summary>
public sealed class FrameworkPage : UserControl
{
    private readonly FrameworkViewModel _vm;
    private readonly ILogService _log;

    private readonly TextBox _path = new();
    private readonly Button _browse = new();
    private readonly Button _openExplorer = new();
    private readonly Button _validate = new();
    private readonly Button _copy = new();
    private readonly Button _cancel = new();
    private readonly ProgressBar _progress = new();
    private readonly Label _status = new();
    private readonly Label _summary = new();
    private readonly ListView _files = new();
    private readonly TextBox _warnings = new();

    private CancellationTokenSource? _cts;

    public FrameworkPage(FrameworkViewModel vm, ILogService log)
    {
        _vm = vm;
        _log = log;

        Dock = DockStyle.Fill;
        BackColor = UiTheme.Background;
        Padding = new Padding(16);

        Controls.Add(BuildBody());
        Controls.Add(BuildSelector());
        Controls.Add(BuildHeader());

        if (!string.IsNullOrWhiteSpace(_vm.LastFrameworkPath))
            _path.Text = _vm.LastFrameworkPath;
    }

    private Control BuildHeader()
    {
        var panel = new Panel { Dock = DockStyle.Top, Height = 60 };
        panel.Controls.Add(new Label
        {
            Text = "WinFE Source",
            Font = UiTheme.Heading,
            ForeColor = UiTheme.TextPrimary,
            AutoSize = true,
            Location = new Point(0, 0)
        });
        panel.Controls.Add(new Label
        {
            // Says plainly which step is required and which is not — the copy button reads like part
            // of a sequence, and nothing previously mentioned that Build does it for you.
            Text = "Select the extracted official WinFE framework folder, then Validate. The original is " +
                   "never modified. Copying to the workspace is optional — Build does it automatically.",
            Font = UiTheme.Body,
            ForeColor = UiTheme.TextSecondary,
            AutoSize = true,
            Location = new Point(0, 30)
        });
        return panel;
    }

    private Control BuildSelector()
    {
        // Deterministic layout: row 0 = path + Browse, row 1 = action buttons.
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 88,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(0, 6, 0, 6)
        };
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));

        // --- Path row: textbox fills, Browse + Open in Explorer auto-size on the right ---
        var pathRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, Margin = new Padding(0) };
        pathRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        pathRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        pathRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        _path.Font = UiTheme.Body;
        _path.Dock = DockStyle.Fill;
        _path.Margin = new Padding(0, 2, 6, 0);
        _path.PlaceholderText = @"e.g. C:\WinFE\M46_WinFE_x64";
        _path.AccessibleName = "Framework folder path";

        _browse.Text = "Browse...";
        _browse.AutoSize = true;
        _browse.Padding = new Padding(12, 3, 12, 3);
        _browse.Margin = new Padding(0, 0, 6, 0);
        _browse.Click += OnBrowse;

        _openExplorer.Text = "Open in Explorer";
        _openExplorer.AutoSize = true;
        _openExplorer.Padding = new Padding(12, 3, 12, 3);
        _openExplorer.Margin = new Padding(0);
        _openExplorer.AccessibleName = "Open the framework folder in File Explorer";
        _openExplorer.Click += OnOpenInExplorer;

        pathRow.Controls.Add(_path, 0, 0);
        pathRow.Controls.Add(_browse, 1, 0);
        pathRow.Controls.Add(_openExplorer, 2, 0);

        // --- Button row ---
        var btnRow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = new Padding(0, 4, 0, 0)
        };

        _validate.Text = "Validate WinFE Source";
        _validate.Font = UiTheme.Subheading;
        _validate.BackColor = UiTheme.Accent;
        _validate.ForeColor = Color.White;
        _validate.FlatStyle = FlatStyle.Flat;
        _validate.FlatAppearance.BorderSize = 0;
        _validate.AutoSize = true;
        _validate.Padding = new Padding(12, 6, 12, 6);
        _validate.Margin = new Padding(0, 0, 8, 0);
        _validate.Click += async (_, _) => await ValidateAsync();

        // Labelled optional on purpose: Build already creates the workspace and copies the framework
        // in (BuildService steps 3-4). This button exists only to do the copy-and-hash on its own, to
        // produce a manifest and show the original is untouched without waiting for a full build.
        // Reading it as a required step is what makes the page look like a sequence it isn't.
        _copy.Text = "Copy + hash framework (optional)";
        _copy.Font = UiTheme.Subheading;
        _copy.AutoSize = true;
        _copy.Enabled = false;
        _copy.Padding = new Padding(12, 6, 12, 6);
        _copy.Margin = new Padding(0, 0, 8, 0);
        _copy.AccessibleDescription =
            "Optional. Build does this automatically. Use it to verify the framework and produce a " +
            "hashed manifest without running a build.";
        _copy.Click += async (_, _) => await CopyAsync();

        var copyTip = new ToolTip { AutoPopDelay = 20000, InitialDelay = 400, ShowAlways = true };
        copyTip.SetToolTip(_copy, _copy.AccessibleDescription);

        _cancel.Text = "Cancel";
        _cancel.AutoSize = true;
        _cancel.Enabled = false;
        _cancel.Padding = new Padding(10, 6, 10, 6);
        _cancel.Margin = new Padding(0, 0, 12, 0);
        _cancel.Click += (_, _) => _cts?.Cancel();

        _progress.Style = ProgressBarStyle.Marquee;
        _progress.MarqueeAnimationSpeed = 0;
        _progress.Visible = false;
        _progress.Width = 200;
        _progress.Height = 18;
        _progress.Margin = new Padding(0, 8, 0, 0);

        btnRow.Controls.Add(_validate);
        btnRow.Controls.Add(_copy);
        btnRow.Controls.Add(_cancel);
        btnRow.Controls.Add(_progress);

        panel.Controls.Add(pathRow, 0, 0);
        panel.Controls.Add(btnRow, 0, 1);
        return panel;
    }

    private Control BuildBody()
    {
        // Deterministic layout: fixed-height status band on top, files (left) + warnings (right) below.
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 84));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        // --- Status band ---
        var statusPanel = new Panel { Dock = DockStyle.Fill, BackColor = UiTheme.Surface, BorderStyle = BorderStyle.FixedSingle, Padding = new Padding(12, 8, 12, 8) };
        var statusStack = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false };

        _status.Text = "Not validated";
        _status.Font = UiTheme.Subheading;
        _status.ForeColor = UiTheme.TextSecondary;
        _status.AutoSize = true;
        _status.Margin = new Padding(0, 0, 0, 4);

        _summary.Font = UiTheme.Body;
        _summary.ForeColor = UiTheme.TextPrimary;
        _summary.AutoSize = true;
        _summary.MaximumSize = new Size(1000, 0);

        statusStack.Controls.Add(_status);
        statusStack.Controls.Add(_summary);
        statusPanel.Controls.Add(statusStack);

        // --- Files (left) + warnings (right) ---
        var lower = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = new Padding(0, 8, 0, 0) };
        lower.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 68));
        lower.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 32));

        _files.View = View.Details;
        _files.Dock = DockStyle.Fill;
        _files.FullRowSelect = true;
        _files.GridLines = true;
        _files.Font = UiTheme.Body;
        _files.Columns.Add("Category", 100);
        _files.Columns.Add("File", 220);
        _files.Columns.Add("Size", 80, HorizontalAlignment.Right);
        _files.Columns.Add("SHA-256", 460);
        _files.AccessibleName = "Discovered framework files";
        _files.MultiSelect = false;
        _files.ShowItemToolTips = true;
        _files.ItemActivate += (_, _) => RevealSelectedFile();

        var reveal = new ToolStripMenuItem("Reveal in File Explorer");
        reveal.Click += (_, _) => RevealSelectedFile();
        var copyPath = new ToolStripMenuItem("Copy full path");
        copyPath.Click += (_, _) =>
        {
            if (_files.SelectedItems.Count > 0 && _files.SelectedItems[0].Tag is string p)
                Clipboard.SetText(p);
        };
        _files.ContextMenuStrip = new ContextMenuStrip();
        _files.ContextMenuStrip.Items.Add(reveal);
        _files.ContextMenuStrip.Items.Add(copyPath);

        var warnPanel = new Panel { Dock = DockStyle.Fill, Margin = new Padding(8, 0, 0, 0) };
        _warnings.Multiline = true;
        _warnings.ReadOnly = true;
        _warnings.ScrollBars = ScrollBars.Vertical;
        _warnings.Dock = DockStyle.Fill;
        _warnings.Font = UiTheme.Mono;
        _warnings.BackColor = Color.FromArgb(249, 250, 251);
        _warnings.AccessibleName = "Warnings and notes";
        warnPanel.Controls.Add(_warnings);
        warnPanel.Controls.Add(new Label { Text = "Warnings / notes", Dock = DockStyle.Top, Font = UiTheme.Subheading, Height = 24 });

        lower.Controls.Add(_files, 0, 0);
        lower.Controls.Add(warnPanel, 1, 0);

        root.Controls.Add(statusPanel, 0, 0);
        root.Controls.Add(lower, 0, 1);
        return root;
    }

    private void OnBrowse(object? sender, EventArgs e)
    {
        using var dlg = new FolderBrowserDialog { Description = "Select the extracted WinFE framework folder", UseDescriptionForTitle = true };
        if (!string.IsNullOrWhiteSpace(_path.Text) && Directory.Exists(_path.Text))
            dlg.SelectedPath = _path.Text;
        if (dlg.ShowDialog(FindForm()) == DialogResult.OK)
            _path.Text = dlg.SelectedPath;
    }

    private void OnOpenInExplorer(object? sender, EventArgs e)
    {
        var path = _path.Text.Trim();
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
        {
            MessageBox.Show(
                "Enter or select an existing framework folder first.",
                "Open in Explorer", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        OpenFolder(path);
    }

    /// <summary>Reveal (and select) the currently selected file row in File Explorer.</summary>
    private void RevealSelectedFile()
    {
        if (_files.SelectedItems.Count == 0) return;
        if (_files.SelectedItems[0].Tag is not string full) return;

        if (File.Exists(full))
            RevealInExplorer(full);
        else if (Directory.Exists(full))
            OpenFolder(full);
        else
            MessageBox.Show("That item no longer exists on disk.", "Reveal in File Explorer",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }

    private void OpenFolder(string folder)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = folder,
                UseShellExecute = true // let the shell open the folder window
            });
        }
        catch (Exception ex)
        {
            _log.Error("WinFE Source", $"Failed to open folder: {folder}", ex);
            MessageBox.Show(ex.Message, "Open in Explorer", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void RevealInExplorer(string file)
    {
        try
        {
            // explorer.exe /select,"<file>" opens the containing folder with the file highlighted.
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"/select,\"{file}\"",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            _log.Error("WinFE Source", $"Failed to reveal file: {file}", ex);
            MessageBox.Show(ex.Message, "Reveal in File Explorer", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async Task ValidateAsync()
    {
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        SetBusy(true);
        _copy.Enabled = false;

        try
        {
            var result = await _vm.ValidateAsync(_path.Text.Trim(), _cts.Token);
            Render(result);
            _copy.Enabled = result.IsValid;
        }
        catch (OperationCanceledException)
        {
            _status.Text = "Validation canceled";
            _status.ForeColor = UiTheme.Warning;
        }
        catch (Exception ex)
        {
            _log.Error("WinFE Source", "Validation failed.", ex);
            MessageBox.Show(ex.Message, "Validation failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task CopyAsync()
    {
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        SetBusy(true);

        var progress = new Progress<string>(msg => _log.Info("WinFE Source", msg));
        try
        {
            var result = await _vm.CopyToWorkspaceAsync(progress, _cts.Token);
            if (result.Success)
            {
                var choice = MessageBox.Show(
                    result.Message + Environment.NewLine + Environment.NewLine + result.TechnicalDetails
                        + Environment.NewLine + Environment.NewLine + "Open the workspace folder in File Explorer?",
                    "Copy complete", MessageBoxButtons.YesNo, MessageBoxIcon.Information);

                var workspace = result.OutputPaths.FirstOrDefault(Directory.Exists);
                if (choice == DialogResult.Yes && workspace is not null)
                    OpenFolder(workspace);
            }
            else
            {
                MessageBox.Show(
                    result.Message + (result.RecommendedAction is null ? "" : Environment.NewLine + Environment.NewLine + "Action: " + result.RecommendedAction),
                    "Copy failed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
        catch (Exception ex)
        {
            _log.Error("WinFE Source", "Copy failed.", ex);
            MessageBox.Show(ex.Message, "Copy failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void Render(FrameworkValidationResult r)
    {
        _status.Text = "Status: " + UiTheme.StatusText(r.Status);
        _status.ForeColor = UiTheme.StatusColor(r.Status);
        _summary.Text = r.Summary;

        _files.BeginUpdate();
        _files.Items.Clear();
        AddFiles(r.BuildScripts);
        AddFiles(r.Components);
        AddFiles(r.ConfigFiles);
        _files.EndUpdate();

        var notes = new List<string>();
        if (r.PossibleDoubleNesting)
            notes.Add("NOTE: Possible double-nesting - the scripts may be in a subfolder. Consider selecting the inner folder.");
        notes.Add($"x64 support: {(r.SupportsX64 ? "yes" : "not confirmed")}");
        if (r.ExpectedItemsFound.Count > 0)
            notes.Add("Found: " + string.Join(", ", r.ExpectedItemsFound));
        if (r.ExpectedItemsMissing.Count > 0)
            notes.Add("Missing: " + string.Join(", ", r.ExpectedItemsMissing));
        notes.AddRange(r.Warnings);
        if (!string.IsNullOrWhiteSpace(r.RecommendedAction))
            notes.Add("Recommended action: " + r.RecommendedAction);

        _warnings.Text = string.Join(Environment.NewLine, notes);
    }

    private void AddFiles(IEnumerable<DiscoveredFile> files)
    {
        foreach (var f in files)
        {
            var item = new ListViewItem(f.Category);
            item.SubItems.Add(f.RelativePath);
            item.SubItems.Add(f.SizeBytes < 0 ? "?" : FormatBytes(f.SizeBytes));
            item.SubItems.Add(f.Sha256 ?? (f.IsZeroBytes ? "(zero bytes)" : " - "));
            item.Tag = f.FullPath; // used by "Reveal in File Explorer" / double-click
            item.ToolTipText = "Double-click to reveal in File Explorer";
            if (f.IsZeroBytes) item.ForeColor = UiTheme.Fail;
            _files.Items.Add(item);
        }
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB" };
        double v = bytes; int u = 0;
        while (v >= 1024 && u < units.Length - 1) { v /= 1024; u++; }
        return $"{v:0.#} {units[u]}";
    }

    private void SetBusy(bool busy)
    {
        _validate.Enabled = !busy;
        _browse.Enabled = !busy;
        _path.Enabled = !busy;
        _copy.Enabled = !busy && _copy.Enabled;
        _cancel.Enabled = busy;
        _progress.Visible = busy;
        _progress.MarqueeAnimationSpeed = busy ? 30 : 0;
    }
}
