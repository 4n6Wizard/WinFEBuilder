using System.Drawing;
using WinFEBuilder.App.ViewModels;
using WinFEBuilder.Core.Logging;
using WinFEBuilder.Core.Models;
using WinFEBuilder.Core.Services;

namespace WinFEBuilder.App.Controls;

/// <summary>Build page: runs the official WinFE media + ISO build and validates the output.</summary>
public sealed class BuildPage : UserControl
{
    private readonly BuildViewModel _vm;
    private readonly ILogService _log;
    private readonly LiveLogPanel _liveLog;

    private readonly Label _frameworkLabel = new();
    private readonly ComboBox _mediaScript = new();
    private readonly ComboBox _isoScript = new();
    private readonly NumericUpDown _timeout = new();
    private readonly CheckBox _skipIso = new();
    private readonly CheckBox _includeComponents = new();
    private readonly Button _refresh = new();
    private readonly Button _start = new();
    private readonly Button _cancel = new();
    private readonly ProgressBar _progress = new();

    private readonly ListView _stages = new();
    private readonly TextBox _results = new();

    private CancellationTokenSource? _cts;
    private BuildResult? _last;

    /// <summary>
    /// Shown as the tooltip and accessible description for the Windows-components option. Spells out
    /// the .NET Framework / modern .NET distinction, which is the single most common source of
    /// "my tool won't start in WinFE" confusion.
    /// </summary>
    private const string ComponentsHelpText =
        "Installs the WinPE optional components WinPE-NetFx (.NET Framework 4.x), WinPE-WMI and "
        + "WinPE-Scripting into boot.wim.\r\n\r\n"
        + "This is what tools like FTK Imager need — without it they fail with "
        + "\"mscoree.dll was not found\".\r\n\r\n"
        + "It does NOT install modern .NET (5, 6, 8, 9, 10). Microsoft publishes no WinPE component "
        + "for that, so a tool built on modern .NET — for example Arsenal Image Mounter "
        + "(aim_cli / aim_remote) — must carry its own runtime. A .NET 9 tool will still fail with "
        + "\"You must install or update .NET to run this application\" even with this option on. Look "
        + "for a runtimeconfig.json beside the tool's .exe: that means modern .NET.\r\n\r\n"
        + "When cleared, the build runs only the framework's own scripts and adds nothing.";

    public BuildPage(BuildViewModel vm, ILogService log)
    {
        _vm = vm;
        _log = log;
        _liveLog = new LiveLogPanel(log);

        Dock = DockStyle.Fill;
        BackColor = UiTheme.Background;
        Padding = new Padding(16);

        Controls.Add(BuildBody());
        Controls.Add(BuildConfig());
        Controls.Add(BuildHeader());

        RefreshFramework();
    }

    private Control BuildHeader()
    {
        var panel = new Panel { Dock = DockStyle.Top, Height = 56 };
        panel.Controls.Add(new Label
        {
            Text = "Build", Font = UiTheme.Heading, ForeColor = UiTheme.TextPrimary,
            AutoSize = true, Location = new Point(0, 0)
        });
        panel.Controls.Add(new Label
        {
            Text = "Runs the official WinFE build batch files in a fresh workspace, then validates the media and ISO.",
            Font = UiTheme.Body, ForeColor = UiTheme.TextSecondary, AutoSize = true, Location = new Point(0, 30)
        });
        return panel;
    }

    private Control BuildConfig()
    {
        var grid = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 156,
            ColumnCount = 4,
            RowCount = 4,
            Padding = new Padding(0, 6, 0, 6)
        };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 140));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        _frameworkLabel.AutoSize = true;
        _frameworkLabel.Font = UiTheme.Body;
        _frameworkLabel.ForeColor = UiTheme.TextPrimary;
        _frameworkLabel.Anchor = AnchorStyles.Left;

        _mediaScript.DropDownStyle = ComboBoxStyle.DropDownList;
        _mediaScript.Dock = DockStyle.Fill;
        _mediaScript.Margin = new Padding(0, 2, 8, 2);

        _isoScript.DropDownStyle = ComboBoxStyle.DropDownList;
        _isoScript.Dock = DockStyle.Fill;
        _isoScript.Margin = new Padding(0, 2, 8, 2);

        _timeout.Minimum = 5;
        _timeout.Maximum = 240;
        _timeout.Value = 45;
        _timeout.Anchor = AnchorStyles.Left;
        _timeout.Width = 70;

        _skipIso.Text = "Skip ISO (media only)";
        _skipIso.AutoSize = true;
        _skipIso.Anchor = AnchorStyles.Left;

        // ".NET Framework" spelled out deliberately. This installs WinPE-NetFx (.NET Framework 4.x,
        // the mscoree.dll runtime that FTK Imager needs). It does NOT provide modern .NET
        // (5/6/8/9/10) — Microsoft publishes no WinPE component for that, so tools like Arsenal
        // Image Mounter must carry their own runtime. Labelling this ".NET" cost real debugging time.
        _includeComponents.Text = "Prepare Windows components (.NET Framework, WMI)";
        _includeComponents.AutoSize = true;
        _includeComponents.Anchor = AnchorStyles.Left;
        _includeComponents.Checked = true;
        // Unchecking this makes the build a plain run of the framework's own scripts, with nothing
        // added afterwards — useful for comparing against a manually built image.
        _includeComponents.AccessibleDescription = ComponentsHelpText;

        var componentTip = new ToolTip
        {
            AutoPopDelay = 30000,
            InitialDelay = 400,
            ReshowDelay = 200,
            ShowAlways = true
        };
        componentTip.SetToolTip(_includeComponents, ComponentsHelpText);

        _refresh.Text = "Refresh scripts";
        _refresh.AutoSize = true;
        _refresh.Padding = new Padding(10, 4, 10, 4);
        _refresh.Click += async (_, _) => await RefreshScriptsAsync();

        _start.Text = "Start Build";
        _start.Font = UiTheme.Subheading;
        _start.BackColor = UiTheme.Accent;
        _start.ForeColor = Color.White;
        _start.FlatStyle = FlatStyle.Flat;
        _start.FlatAppearance.BorderSize = 0;
        _start.AutoSize = true;
        _start.Padding = new Padding(14, 6, 14, 6);
        _start.Click += async (_, _) => await StartBuildAsync();

        _cancel.Text = "Cancel";
        _cancel.AutoSize = true;
        _cancel.Enabled = false;
        _cancel.Padding = new Padding(10, 6, 10, 6);
        _cancel.Click += (_, _) => _cts?.Cancel();

        _progress.Style = ProgressBarStyle.Marquee;
        _progress.MarqueeAnimationSpeed = 0;
        _progress.Visible = false;
        _progress.Width = 200;
        _progress.Height = 18;
        _progress.Anchor = AnchorStyles.Left;

        // Row 0: framework
        grid.Controls.Add(MakeCaption("Framework:"), 0, 0);
        grid.Controls.Add(_frameworkLabel, 1, 0);
        grid.SetColumnSpan(_frameworkLabel, 3);

        // Row 1: media script + refresh
        grid.Controls.Add(MakeCaption("Media script:"), 0, 1);
        grid.Controls.Add(_mediaScript, 1, 1);
        grid.Controls.Add(_refresh, 3, 1);

        // Row 2: iso script + timeout
        grid.Controls.Add(MakeCaption("ISO script:"), 0, 2);
        grid.Controls.Add(_isoScript, 1, 2);
        grid.Controls.Add(MakeCaption("Timeout (min):"), 2, 2);
        grid.Controls.Add(_timeout, 3, 2);

        // Row 3: options + actions.
        _skipIso.Margin = new Padding(0, 4, 12, 0);
        _includeComponents.Margin = new Padding(0, 4, 12, 0);
        var options = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, AutoSize = true };
        options.Controls.Add(_skipIso);
        options.Controls.Add(_includeComponents);
        grid.Controls.Add(options, 1, 3);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = new Padding(0, 4, 0, 0) };
        _start.Margin = new Padding(0, 0, 8, 0);
        _cancel.Margin = new Padding(0, 0, 8, 0);
        _progress.Margin = new Padding(0, 6, 0, 0);
        actions.Controls.Add(_start);
        actions.Controls.Add(_cancel);
        actions.Controls.Add(_progress);
        grid.Controls.Add(actions, 2, 3);
        grid.SetColumnSpan(actions, 2);

        return grid;
    }

    private static Label MakeCaption(string text) => new()
    {
        Text = text, AutoSize = true, Font = UiTheme.Body, ForeColor = UiTheme.TextSecondary,
        Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 6, 0)
    };

    private Control BuildBody()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 190));

        var upper = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
        upper.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58));
        upper.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42));

        // Stages
        _stages.View = View.Details;
        _stages.Dock = DockStyle.Fill;
        _stages.FullRowSelect = true;
        _stages.GridLines = true;
        _stages.Font = UiTheme.Body;
        _stages.Columns.Add("Stage", 220);
        _stages.Columns.Add("Status", 90);
        _stages.Columns.Add("Detail", 380);
        _stages.AccessibleName = "Build stages";

        // Results
        var resultPanel = new Panel { Dock = DockStyle.Fill, Margin = new Padding(8, 0, 0, 0) };
        _results.Multiline = true;
        _results.ReadOnly = true;
        _results.ScrollBars = ScrollBars.Vertical;
        _results.Dock = DockStyle.Fill;
        _results.Font = UiTheme.Mono;
        _results.BackColor = UiTheme.Surface;
        _results.AccessibleName = "Build results summary";
        _results.Text = "No build run yet.";
        resultPanel.Controls.Add(_results);
        resultPanel.Controls.Add(new Label { Text = "Result summary", Dock = DockStyle.Top, Height = 24, Font = UiTheme.Subheading });

        upper.Controls.Add(_stages, 0, 0);
        upper.Controls.Add(resultPanel, 1, 0);

        _liveLog.Dock = DockStyle.Fill;
        _liveLog.Margin = new Padding(0, 8, 0, 0);

        root.Controls.Add(upper, 0, 0);
        root.Controls.Add(_liveLog, 0, 1);
        return root;
    }

    private void RefreshFramework()
    {
        _frameworkLabel.Text = string.IsNullOrWhiteSpace(_vm.FrameworkPath)
            ? "(none selected — set one on the Framework page)"
            : _vm.FrameworkPath!;
    }

    private async Task RefreshScriptsAsync()
    {
        RefreshFramework();
        SetBusy(true);
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        try
        {
            var (ok, message, scripts) = await _vm.DiscoverScriptsAsync(_cts.Token);
            _mediaScript.Items.Clear();
            _isoScript.Items.Clear();
            if (!ok)
            {
                MessageBox.Show(message, "Cannot load scripts", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            foreach (var s in scripts) { _mediaScript.Items.Add(s); _isoScript.Items.Add(s); }

            // Sensible defaults via the same heuristics the service uses.
            var iso = Core.Validation.BuildScriptSelector.SelectIsoScript(scripts);
            var media = Core.Validation.BuildScriptSelector.SelectMediaScript(scripts);
            if (media is not null) _mediaScript.SelectedItem = media;
            if (iso is not null) _isoScript.SelectedItem = iso;
        }
        catch (Exception ex)
        {
            _log.Error("Build", "Failed to load scripts.", ex);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task StartBuildAsync()
    {
        if (_mediaScript.SelectedItem is null)
        {
            var proceed = MessageBox.Show(
                "No media build script is selected. Click 'Refresh scripts' first, or continue and let the app auto-select?",
                "Start Build", MessageBoxButtons.OKCancel, MessageBoxIcon.Question);
            if (proceed != DialogResult.OK) return;
        }

        var confirm = MessageBox.Show(
            "This will create a new workspace, copy the framework, and run the official WinFE build batch files. Continue?",
            "Start Build", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
        if (confirm != DialogResult.Yes) return;

        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        SetBusy(true);
        _stages.Items.Clear();
        _results.Text = "Building…";

        var request = new BuildRequest
        {
            MediaScriptName = _mediaScript.SelectedItem as string,
            IsoScriptName = _isoScript.SelectedItem as string,
            TimeoutMinutes = (int)_timeout.Value,
            SkipIso = _skipIso.Checked,
            IncludeDotNet = _includeComponents.Checked
        };

        var progress = new Progress<string>(_ => RenderStages(_last));
        try
        {
            _last = await _vm.RunBuildAsync(request, progress, _cts.Token);
            RenderStages(_last);
            RenderResults(_last);
        }
        catch (Exception ex)
        {
            _log.Error("Build", "Build failed.", ex);
            MessageBox.Show(ex.Message, "Build failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void RenderStages(BuildResult? r)
    {
        if (r is null) return;
        _stages.BeginUpdate();
        _stages.Items.Clear();
        foreach (var s in r.Stages)
        {
            var item = new ListViewItem(s.Name);
            item.SubItems.Add(UiTheme.StatusText(s.Status));
            item.SubItems.Add(s.Detail);
            item.ForeColor = UiTheme.StatusColor(s.Status);
            _stages.Items.Add(item);
        }
        _stages.EndUpdate();
    }

    private void RenderResults(BuildResult r)
    {
        var lines = new List<string>
        {
            $"Overall:            {(r.Success ? "SUCCESS" : "INCOMPLETE / WARNINGS")}",
            $"Duration:           {r.Duration.TotalSeconds:F0} s",
            "",
            "── Operational status (build vs. forensic) ──",
            $"Build:              {(r.MediaBuildRun?.ExitCode == 0 ? "Successful" : "Completed with warnings")}",
            $"Boot Structure:     {((r.Media?.StructureValid ?? false) ? "Validated" : "Not validated")}",
            $"Boot Test:          {r.BootTestStatus}",
            $"Write-Protection:   {r.WriteProtectionTestStatus}",
            "",
        };

        if (r.Media?.Wim is not null)
        {
            lines.Add("── boot.wim ──");
            lines.Add($"Path:    {r.Media.BootWimPath}");
            lines.Add($"Arch:    {r.Media.Wim.Architecture ?? "unknown"}");
            lines.Add($"Images:  {r.Media.Wim.ImageCount}");
            lines.Add($"Size:    {r.Media.Wim.SizeBytes / 1024d / 1024d:F0} MB");
            lines.Add($"SHA-256: {r.Media.Wim.Sha256}");
            lines.Add("");
        }
        if (r.Iso is not null)
        {
            lines.Add("── ISO ──");
            lines.Add($"Source:  {r.Iso.SourcePath}");
            lines.Add($"Output:  {r.Iso.DestinationPath}");
            lines.Add($"Size:    {r.Iso.SizeBytes / 1024d / 1024d:F0} MB");
            lines.Add($"SHA-256: {r.Iso.Sha256}");
            lines.Add("");
        }
        if (r.ManifestPath is not null) lines.Add($"Manifest: {r.ManifestPath}");
        if (r.Warnings.Count > 0) { lines.Add(""); lines.Add("Warnings:"); lines.AddRange(r.Warnings.Select(w => "  • " + w)); }
        if (r.Errors.Count > 0) { lines.Add(""); lines.Add("Errors:"); lines.AddRange(r.Errors.Select(e => "  • " + e)); }
        if (!string.IsNullOrWhiteSpace(r.RecommendedAction)) { lines.Add(""); lines.Add("Recommended action: " + r.RecommendedAction); }

        _results.Text = string.Join(Environment.NewLine, lines);
    }

    private void SetBusy(bool busy)
    {
        _start.Enabled = !busy;
        _refresh.Enabled = !busy;
        _mediaScript.Enabled = !busy;
        _isoScript.Enabled = !busy;
        _timeout.Enabled = !busy;
        _skipIso.Enabled = !busy;
        _includeComponents.Enabled = !busy;
        _cancel.Enabled = busy;
        _progress.Visible = busy;
        _progress.MarqueeAnimationSpeed = busy ? 30 : 0;
    }
}
