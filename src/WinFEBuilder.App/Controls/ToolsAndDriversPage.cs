using System.Drawing;
using WinFEBuilder.App.ViewModels;
using WinFEBuilder.Core.Logging;
using WinFEBuilder.Core.Models;
using WinFEBuilder.Core.Services;
using WinFEBuilder.Core.Validation;

namespace WinFEBuilder.App.Controls;

/// <summary>
/// Tools, Drivers, and Windows Components. The Drivers/Components experience is capability-focused:
/// Microsoft package names and DISM internals live only in the collapsible Advanced area / logs.
/// Backend behavior (enumeration, injection, WinPE servicing) is unchanged.
/// </summary>
public sealed class ToolsAndDriversPage : UserControl
{
    private readonly ToolsAndDriversViewModel _vm;
    private readonly ILogService _log;
    private readonly LiveLogPanel _liveLog;

    // Tools
    private readonly ListView _tools = new();
    private readonly ComboBox _toolArch = new();

    // Drivers
    private readonly Label _driverFolder = new();
    private readonly ListView _driverCategories = new();
    private readonly Label _driverTotal = new();
    private readonly Label _driverStatus = new();
    private readonly Button _driverOpenLog = new();
    private Button _injectBtn = new();

    // Image content — folders copied straight into boot.wim, for things WinPE has no package for.
    private readonly ListView _contentList = new();
    private readonly Label _contentStatus = new();
    private readonly Label _contentTotal = new();
    private Button _contentApplyBtn = new();

    private readonly List<ImageContentItem> _content = new();

    private List<DriverInfo> _driverList = new();
    private string? _lastDismLogPath;
    private CancellationTokenSource? _cts;

    public ToolsAndDriversPage(ToolsAndDriversViewModel vm, ILogService log)
    {
        _vm = vm;
        _log = log;
        _liveLog = new LiveLogPanel(log);

        Dock = DockStyle.Fill;
        BackColor = UiTheme.Background;
        Padding = new Padding(16);

        Controls.Add(BuildBody());
        Controls.Add(BuildHeader());

        RefreshTools();
    }

    private Control BuildHeader()
    {
        var p = new Panel { Dock = DockStyle.Top, Height = 52 };
        p.Controls.Add(new Label { Text = "Tools and Drivers", Font = UiTheme.Heading, ForeColor = UiTheme.TextPrimary, AutoSize = true, Location = new Point(0, 0) });
        p.Controls.Add(new Label
        {
            Text = "Add forensic tools, hardware drivers, and Windows capabilities to your WinFE workspace.",
            Font = UiTheme.Body, ForeColor = UiTheme.TextSecondary, AutoSize = true, Location = new Point(0, 28)
        });
        return p;
    }

    private Control BuildBody()
    {
        // Three sections plus a log cannot fit on a laptop-height window. Percentages crushed the last
        // section to a sliver, and putting the log in the scroll area meant it scrolled out of sight.
        // So: the log is pinned to the bottom, and the three sections scroll above it.
        var root = new Panel { Dock = DockStyle.Fill };

        _liveLog.Dock = DockStyle.Bottom;
        _liveLog.Height = 130;
        _liveLog.Margin = new Padding(0, 8, 0, 0);

        var scroll = new Panel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            Padding = new Padding(0, 0, SystemInformation.VerticalScrollBarWidth, 8)
        };

        // Dock=Top stacks in reverse order of addition, so add bottom section first.
        var content = BuildImageContentGroup();
        content.Dock = DockStyle.Top;
        content.Height = 290;

        var drivers = BuildDriversGroup();
        drivers.Dock = DockStyle.Top;
        drivers.Height = 250;

        var tools = BuildToolsGroup();
        tools.Dock = DockStyle.Top;
        tools.Height = 190;

        scroll.Controls.Add(content);
        scroll.Controls.Add(drivers);
        scroll.Controls.Add(tools);

        root.Controls.Add(scroll);
        root.Controls.Add(_liveLog);
        return root;
    }

    // ================================================================= Tools
    private Control BuildToolsGroup()
    {
        var panel = new Panel { Dock = DockStyle.Fill };

        var bar = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = true };
        _toolArch.DropDownStyle = ComboBoxStyle.DropDownList;
        _toolArch.Items.AddRange(new object[] { "x64", "x86" });
        _toolArch.SelectedIndex = 0;
        _toolArch.Width = 70;
        _toolArch.Margin = new Padding(3, 5, 3, 3);
        var add = MakeButton("Add tool to framework…", accent: true); add.Click += OnAddToFramework;
        var open = MakeButton("Open tools folder"); open.Click += OnOpenToolsFolder;
        var refresh = MakeButton("Refresh"); refresh.Click += (_, _) => RefreshTools();
        var remove = MakeButton("Remove selected"); remove.Click += OnRemoveTool;
        bar.Controls.AddRange(new Control[]
        {
            new Label { Text = "Architecture:", AutoSize = true, Margin = new Padding(3, 9, 3, 3), Font = UiTheme.Body },
            _toolArch, add, open, refresh, remove
        });

        _tools.View = View.Details;
        _tools.Dock = DockStyle.Fill;
        _tools.FullRowSelect = true;
        _tools.GridLines = true;
        _tools.Font = UiTheme.Body;
        _tools.Columns.Add("Arch", 55);
        _tools.Columns.Add("Tool", 220);
        _tools.Columns.Add("Files", 60, HorizontalAlignment.Right);
        _tools.Columns.Add("Size", 90, HorizontalAlignment.Right);
        _tools.Columns.Add("Location (in framework)", 420);

        var note = new Label
        {
            Dock = DockStyle.Top, Height = 20, Font = UiTheme.Body, ForeColor = UiTheme.TextSecondary,
            Text = "Which forensic tools do you want?  Tools are copied into the framework and baked into the next Build."
        };

        panel.Controls.Add(_tools);
        panel.Controls.Add(note);
        panel.Controls.Add(bar);
        return panel;
    }

    // =============================================================== Drivers
    private Control BuildDriversGroup()
    {
        var panel = new Panel { Dock = DockStyle.Fill };

        // Top action bar
        var bar = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = true };
        var select = MakeButton("Select Driver Folder…", accent: true); select.Click += OnSelectDrivers;
        _injectBtn = MakeButton("Inject Drivers"); _injectBtn.Click += OnInject; _injectBtn.Enabled = false;
        _driverOpenLog.Text = "Open log";
        _driverOpenLog.AutoSize = true;
        _driverOpenLog.Padding = new Padding(10, 4, 10, 4);
        _driverOpenLog.Margin = new Padding(3);
        _driverOpenLog.Visible = false;
        _driverOpenLog.Click += (_, _) => OpenPathOrLogs(_lastDismLogPath);
        bar.Controls.AddRange(new Control[] { select, _injectBtn, _driverOpenLog });

        _driverFolder.Dock = DockStyle.Top;
        _driverFolder.Height = 20;
        _driverFolder.Font = UiTheme.Body;
        _driverFolder.ForeColor = UiTheme.TextSecondary;
        _driverFolder.Text = "Selected folder: (none)";

        _driverStatus.Dock = DockStyle.Top;
        _driverStatus.Height = 22;
        _driverStatus.Font = UiTheme.Subheading;
        _driverStatus.ForeColor = UiTheme.TextSecondary;
        _driverStatus.Text = "";

        _driverTotal.Dock = DockStyle.Top;
        _driverTotal.Height = 20;
        _driverTotal.Font = UiTheme.Body;
        _driverTotal.ForeColor = UiTheme.TextPrimary;
        _driverTotal.Text = "";

        _driverCategories.View = View.Details;
        _driverCategories.Dock = DockStyle.Fill;
        _driverCategories.CheckBoxes = true;
        _driverCategories.FullRowSelect = true;
        _driverCategories.GridLines = true;
        _driverCategories.Font = UiTheme.Body;
        _driverCategories.Columns.Add("Detected drivers", 260);
        _driverCategories.Columns.Add("Count", 80, HorizontalAlignment.Right);
        _driverCategories.Columns.Add("Usable on this image", 320);
        _driverCategories.AccessibleName = "Detected driver categories";

        var title = new Label { Text = "Windows Drivers", Dock = DockStyle.Top, Height = 22, Font = UiTheme.Subheading };
        var subtitle = new Label { Text = "Which hardware drivers do you want to include?", Dock = DockStyle.Top, Height = 18, Font = UiTheme.Body, ForeColor = UiTheme.TextSecondary };

        // Dock order (added first = innermost/bottom): categories fill; tops added last are at the top.
        panel.Controls.Add(_driverCategories);
        panel.Controls.Add(_driverTotal);
        panel.Controls.Add(_driverStatus);
        panel.Controls.Add(_driverFolder);
        panel.Controls.Add(bar);
        panel.Controls.Add(subtitle);
        panel.Controls.Add(title);
        return panel;
    }

    // ================================================== Add to image (inside boot.wim)
    private Control BuildImageContentGroup()
    {
        var panel = new Panel { Dock = DockStyle.Fill };

        var bar = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = true };
        var aim = MakeButton("Add AIM Remote Agent + .NET…", accent: true); aim.Click += OnAddAimPreset;
        var folder = MakeButton("Add folder…"); folder.Click += OnAddContentFolder;
        var remove = MakeButton("Remove selected"); remove.Click += OnRemoveContent;
        _contentApplyBtn = MakeButton("Apply to image"); _contentApplyBtn.Click += OnApplyContent; _contentApplyBtn.Enabled = false;
        bar.Controls.AddRange(new Control[] { aim, folder, remove, _contentApplyBtn });

        // Two decisions used to live here as checkboxes; both had exactly one sensible answer, so the
        // app makes them:
        //   * the image is always compacted after servicing — shipping orphaned data has no upside
        //   * the Desktop Runtime is included only when a queued tool's runtimeconfig.json declares
        //     Microsoft.WindowsDesktop.App, which the app can read
        // Knowing what a tool links against is not the operator's job.

        _contentStatus.Dock = DockStyle.Top;
        _contentStatus.Height = 22;
        _contentStatus.Font = UiTheme.Body;
        _contentStatus.ForeColor = UiTheme.TextSecondary;
        _contentStatus.Text = "";

        _contentTotal.Dock = DockStyle.Top;
        _contentTotal.Height = 20;
        _contentTotal.Font = UiTheme.Body;
        _contentTotal.ForeColor = UiTheme.TextPrimary;
        _contentTotal.Text = "";

        _contentList.View = View.Details;
        _contentList.Dock = DockStyle.Fill;
        _contentList.CheckBoxes = true;
        _contentList.FullRowSelect = true;
        _contentList.GridLines = true;
        _contentList.Font = UiTheme.Body;
        // Narrower than the section so no horizontal scrollbar appears at the default window size.
        _contentList.Columns.Add("What", 150);
        _contentList.Columns.Add("Copied from", 250);
        _contentList.Columns.Add("To (inside boot.wim)", 250);
        _contentList.Columns.Add("Files", 55, HorizontalAlignment.Right);
        _contentList.Columns.Add("Size", 70, HorizontalAlignment.Right);
        _contentList.AccessibleName = "Folders to copy into boot.wim";
        _contentList.ItemChecked += (_, args) =>
        {
            if (args.Item?.Tag is ImageContentItem i) i.Selected = args.Item.Checked;
            UpdateContentTotals();
        };

        var title = new Label { Text = "Add to Image", Dock = DockStyle.Top, Height = 22, Font = UiTheme.Subheading };
        var subtitle = new Label
        {
            Text = "Copy folders inside boot.wim — for tools needing modern .NET (5/6/8/9/10), which no WinPE component provides.",
            Dock = DockStyle.Top, Height = 18, Font = UiTheme.Body, ForeColor = UiTheme.TextSecondary
        };

        panel.Controls.Add(_contentList);
        panel.Controls.Add(_contentTotal);
        panel.Controls.Add(_contentStatus);
        panel.Controls.Add(bar);
        panel.Controls.Add(subtitle);
        panel.Controls.Add(title);
        return panel;
    }

    private void OnAddAimPreset(object? sender, EventArgs e)
    {
        var arch = _toolArch.SelectedItem as string ?? "x64";
        var found = _vm.FindAimRemoteFolder(arch);

        if (found is null)
        {
            using var dlg = new FolderBrowserDialog
            {
                Description = $"Select the AIM-Remote_{arch} folder (from the Arsenal Image Mounter download's 'remote' folder)",
                UseDescriptionForTitle = true
            };
            if (dlg.ShowDialog(FindForm()) != DialogResult.OK) return;
            found = dlg.SelectedPath;
        }

        var (items, problem, summary) = _vm.BuildDotNetToolPreset(
            found, $@"Program Files\AIMTools\{Path.GetFileName(found)}");

        if (problem is not null)
        {
            MessageBox.Show(problem, "Cannot add AIM Remote Agent", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _log.Warning("Image", problem);
            return;
        }

        foreach (var i in items) AddContentItem(i);
        _contentStatus.Text = summary ?? "";
        if (summary is not null) _log.Info("Image", summary);
    }

    private void OnAddContentFolder(object? sender, EventArgs e)
    {
        using var dlg = new FolderBrowserDialog { Description = "Select the folder to copy into boot.wim", UseDescriptionForTitle = true };
        if (dlg.ShowDialog(FindForm()) != DialogResult.OK) return;
        var src = dlg.SelectedPath;

        var suggested = $@"Program Files\{Path.GetFileName(src.TrimEnd(Path.DirectorySeparatorChar))}";
        var dest = Prompt.Show(FindForm(), "Destination inside the image",
            "Path relative to the image root (X:\\ when booted):", suggested);
        if (string.IsNullOrWhiteSpace(dest)) return;

        if (!ImageContentItem.IsSafeDestination(dest, out var why))
        {
            MessageBox.Show(why, "Invalid destination", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        AddContentItem(_vm.DescribeContent(src, dest));
    }

    private void AddContentItem(ImageContentItem item)
    {
        // Replace an existing entry for the same destination rather than copying twice.
        var existing = _content.FindIndex(c =>
            string.Equals(c.DestinationRelative, item.DestinationRelative, StringComparison.OrdinalIgnoreCase));
        if (existing >= 0) _content[existing] = item; else _content.Add(item);
        RenderContent();
    }

    private void OnRemoveContent(object? sender, EventArgs e)
    {
        foreach (ListViewItem row in _contentList.SelectedItems)
            if (row.Tag is ImageContentItem i) _content.Remove(i);
        RenderContent();
    }

    private void RenderContent()
    {
        _contentList.BeginUpdate();
        _contentList.Items.Clear();
        foreach (var c in _content)
        {
            var row = new ListViewItem(c.Label ?? c.SourceName) { Checked = c.Selected, Tag = c };
            // Where it comes FROM, in words. Showing only a name made it look as though the tool was
            // being taken from the machine's own Program Files, when the runtime comes from there and
            // the tool comes from the WinFE framework. Two very different origins.
            row.SubItems.Add(DescribeSource(c.SourcePath));
            row.SubItems.Add("X:\\" + c.DestinationRelative);
            row.SubItems.Add(c.FileCount.ToString());
            row.SubItems.Add($"{c.Bytes / 1024d / 1024d:F1} MB");
            row.ToolTipText = $"From:  {c.SourcePath}\r\nTo:    X:\\{c.DestinationRelative}  (inside boot.wim)";
            _contentList.Items.Add(row);
        }
        _contentList.EndUpdate();
        UpdateContentTotals();
    }

    /// <summary>
    /// Plain-language origin for a source path: the installed .NET, the WinFE framework, or elsewhere.
    /// The full path is still in the row tooltip.
    /// </summary>
    private static string DescribeSource(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return "(unknown)";
        var p = path.Replace('/', '\\');

        if (p.StartsWith(DotNetRuntimeLocator.DefaultDotnetRoot, StringComparison.OrdinalIgnoreCase))
            return "installed .NET  (C:\\Program Files\\dotnet)";

        // Staged copies of single files (e.g. dotnet.exe) live in a temp folder we created.
        if (p.StartsWith(Path.GetTempPath(), StringComparison.OrdinalIgnoreCase))
            return "installed .NET  (C:\\Program Files\\dotnet)";

        if (p.Contains(@"\USB\", StringComparison.OrdinalIgnoreCase) &&
            p.Contains(@"\tools\", StringComparison.OrdinalIgnoreCase))
            return $"WinFE framework  (…\\tools\\{Path.GetFileName(p.TrimEnd('\\'))})";

        var parent = Path.GetDirectoryName(p.TrimEnd('\\'));
        return parent is null ? p : $"{Path.GetFileName(p.TrimEnd('\\'))}  (in {Path.GetFileName(parent)})";
    }

    private void UpdateContentTotals()
    {
        var bytes = _content.Where(c => c.Selected).Sum(c => c.Bytes);
        _contentTotal.Text = _content.Count == 0
            ? ""
            : $"{_content.Count(c => c.Selected)} item(s), {bytes / 1024d / 1024d:F1} MB — WinPE loads boot.wim into RAM, so this is added memory at every boot.";
        _contentApplyBtn.Enabled = _content.Any(c => c.Selected);
    }

    private void OnApplyContent(object? sender, EventArgs e)
    {
        // Must use the same resolver as driver injection: newest-first alone can pick the x86 image,
        // which would put the runtime and tools in the wrong architecture's boot.wim.
        var wim = ResolveTargetBootWim();
        if (wim is null)
        {
            MessageBox.Show("No WinFE boot image was found. Run a Build first, then add content to the workspace image.",
                "Add to Image", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var selected = _content.Where(c => c.Selected).ToList();
        if (selected.Count == 0) return;

        var mb = selected.Sum(c => c.Bytes) / 1024d / 1024d;
        var confirm = MessageBox.Show(
            $"Copy {selected.Count} item(s) ({mb:F1} MB) into:\n\n{wim}\n\n" +
            "This modifies the built image, which happens after the framework wrote its write-protection " +
            "keys — re-verify write protection on a scratch disk afterwards.\n\nContinue?",
            "Apply to image", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning);
        if (confirm != DialogResult.OK) return;

        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        _contentApplyBtn.Enabled = false;
        _contentStatus.ForeColor = UiTheme.TextSecondary;
        _contentStatus.Text = "Applying… (mount, copy, commit — this takes several minutes)";

        // Status line only: ImageContentService logs every progress message itself.
        var progress = new Progress<string>(m => _contentStatus.Text = m);
        Task.Run(async () =>
        {
            var r = await _vm.ApplyContentAsync(wim, selected, compact: true, progress, _cts.Token);
            BeginInvoke(new Action(() => RenderContentResult(r)));
        });
    }

    private void RenderContentResult(ImageContentResult r)
    {
        _contentApplyBtn.Enabled = _content.Any(c => c.Selected);
        _lastDismLogPath = r.DismLogPath ?? _lastDismLogPath;
        _driverOpenLog.Visible = _lastDismLogPath is not null;

        if (r.Success)
        {
            _contentStatus.ForeColor = UiTheme.Pass;
            var compact = r.Compacted ? $", compacted (reclaimed {r.BytesReclaimed / 1024d / 1024d:F1} MB)" : "";
            _contentStatus.Text = $"Added {r.ItemsCopied} item(s){compact}. Image now {r.BytesAfter / 1024d / 1024d:F1} MB.";

            MessageBox.Show(
                $"Added {r.ItemsCopied} item(s) to the image.\n\n" +
                $"boot.wim: {r.BytesBefore / 1024d / 1024d:F1} MB → {r.BytesAfter / 1024d / 1024d:F1} MB\n" +
                (r.Compacted ? $"Compacted, reclaimed {r.BytesReclaimed / 1024d / 1024d:F1} MB\n" : "") +
                $"SHA-256 before: {r.Sha256Before}\nSHA-256 after:  {r.Sha256After}\n\n" +
                "Write the USB to put this on your media, then re-verify write protection on a scratch disk.",
                "Applied to image", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        else
        {
            _contentStatus.ForeColor = UiTheme.Fail;
            _contentStatus.Text = r.Errors.FirstOrDefault() ?? "Failed.";
            MessageBox.Show(
                string.Join("\n", r.Errors.Concat(r.Warnings)) +
                (r.RecommendedAction is null ? "" : "\n\n" + r.RecommendedAction),
                "Apply failed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    // ---------------------------------------------------------------- Tools handlers
    private void OnAddToFramework(object? sender, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_vm.FrameworkPath))
        {
            MessageBox.Show("Select and validate a framework on the Framework page first.",
                "No framework selected", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        using var dlg = new FolderBrowserDialog { Description = "Select the portable tool folder to add (e.g. the copied 'FTK Imager' folder)", UseDescriptionForTitle = true };
        if (dlg.ShowDialog(FindForm()) != DialogResult.OK) return;
        var src = dlg.SelectedPath;
        var arch = _toolArch.SelectedItem as string ?? "x64";

        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        var progress = new Progress<string>(m => _log.Info("Tools", m));
        Task.Run(async () =>
        {
            var r = await _vm.AddToolToFrameworkAsync(src, arch, progress, _cts.Token);
            BeginInvoke(new Action(() =>
            {
                RefreshTools();
                if (r.Success) MessageBox.Show(r.Message + "\n\nCopied to:\n" + r.TechnicalDetails, "Tool added to framework", MessageBoxButtons.OK, MessageBoxIcon.Information);
                else MessageBox.Show(r.Message + (r.RecommendedAction is null ? "" : "\n\n" + r.RecommendedAction), "Add failed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }));
        });
    }

    private void OnOpenToolsFolder(object? sender, EventArgs e)
    {
        var arch = _toolArch.SelectedItem as string ?? "x64";
        var dir = _vm.FrameworkToolsDir(arch);
        if (dir is null) { MessageBox.Show("No framework selected, or its tools folder couldn't be located.", "Open tools folder", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
        OpenPathOrLogs(dir);
    }

    private void OnRemoveTool(object? sender, EventArgs e)
    {
        if (_tools.SelectedItems.Count == 0 || _tools.SelectedItems[0].Tag is not FrameworkTool t) return;
        if (MessageBox.Show($"Remove '{t.Name}' ({t.Architecture}) from the framework?\n\n{t.Path}", "Remove tool", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        try { _vm.RemoveFrameworkTool(t.Path); RefreshTools(); }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Remove failed", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private void RefreshTools()
    {
        _tools.BeginUpdate();
        _tools.Items.Clear();
        foreach (var t in _vm.FrameworkTools())
        {
            var item = new ListViewItem(t.Architecture) { Tag = t };
            item.SubItems.Add(t.Name);
            item.SubItems.Add(t.FileCount.ToString());
            item.SubItems.Add(t.SizeText);
            item.SubItems.Add(t.Path);
            _tools.Items.Add(item);
        }
        _tools.EndUpdate();
    }

    // ---------------------------------------------------------------- Drivers handlers

    /// <summary>Automatically pick the target boot.wim (prefer x64) from the newest build. No UI needed.</summary>
    private string? ResolveTargetBootWim()
    {
        var wims = _vm.WorkspaceBootWims();
        return wims.FirstOrDefault(w => w.Replace('/', '\\').Contains(@"\x64\", StringComparison.OrdinalIgnoreCase)) ?? wims.FirstOrDefault();
    }

    private static string TargetArchFor(string? bootWimPath)
        => (bootWimPath ?? "").Replace('/', '\\').Contains(@"\x86\", StringComparison.OrdinalIgnoreCase) ? "x86" : "amd64";

    private void OnSelectDrivers(object? sender, EventArgs e)
    {
        using var dlg = new FolderBrowserDialog { Description = "Select a folder containing hardware drivers (.inf files)", UseDescriptionForTitle = true };
        if (dlg.ShowDialog(FindForm()) != DialogResult.OK) return;

        var folder = dlg.SelectedPath;
        var targetArch = TargetArchFor(ResolveTargetBootWim());
        _driverStatus.Text = "";
        _driverOpenLog.Visible = false;
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        Task.Run(async () =>
        {
            try
            {
                var list = await _vm.EnumerateDriversAsync(folder, targetArch, _cts.Token);
                BeginInvoke(new Action(() => RenderDrivers(folder, list)));
            }
            catch (Exception ex)
            {
                _log.Error("Drivers", "Driver scan failed.", ex);
                BeginInvoke(new Action(() => MessageBox.Show(ex.Message, "Driver scan failed", MessageBoxButtons.OK, MessageBoxIcon.Error)));
            }
        });
    }

    private void RenderDrivers(string folder, List<DriverInfo> list)
    {
        _driverList = list;
        _driverFolder.Text = "Selected folder: " + folder;

        var byCategory = list.GroupBy(DriverCategorizer.Categorize).ToDictionary(g => g.Key, g => g.ToList());
        _driverCategories.BeginUpdate();
        _driverCategories.Items.Clear();
        foreach (var cat in DriverCategorizer.Order)
        {
            if (!byCategory.TryGetValue(cat, out var drivers) || drivers.Count == 0) continue;

            // A category is only worth ticking if something in it can actually load on this image.
            var blockedHere = drivers.Where(d => d.HasOsSupportWarning).ToList();
            var usable = drivers.Count - blockedHere.Count;

            var item = new ListViewItem(cat) { Checked = usable > 0, Tag = cat };
            item.SubItems.Add(drivers.Count.ToString());
            item.SubItems.Add(blockedHere.Count == 0
                ? "yes"
                : usable == 0
                    ? $"NO — needs Windows build {blockedHere.Min(d => d.OsSupport?.LowestRestrictedBuild ?? 0)}+"
                    : $"{usable} of {drivers.Count}");
            if (usable == 0) item.ForeColor = UiTheme.Fail;
            item.ToolTipText = blockedHere.Count > 0 ? blockedHere[0].OsSupportWarning : null;
            _driverCategories.Items.Add(item);
        }
        _driverCategories.EndUpdate();

        _driverTotal.Text = $"Total Drivers: {list.Count}";
        _injectBtn.Enabled = list.Count > 0;

        // Surface the failure that is otherwise invisible until the media is booted: a driver whose
        // devices are listed only for a newer Windows installs fine and never binds.
        var blocked = list.Where(d => d.HasOsSupportWarning).ToList();
        if (blocked.Count > 0)
        {
            _driverStatus.ForeColor = UiTheme.Fail;
            _driverStatus.Text = blocked.Count == 1
                ? $"{blocked[0].InfName}: {blocked[0].OsSupport?.Summary}"
                : $"{blocked.Count} driver(s) require a newer Windows than this image — they would install but never load.";

            MessageBox.Show(
                string.Join("\n\n", blocked.Select(d => $"{d.InfName}\n{d.OsSupportWarning}")) +
                "\n\nThese have been left unticked. Injecting them would report success and change nothing on " +
                "the booted machine.",
                "Driver not usable on this image", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        else
        {
            _driverStatus.ForeColor = UiTheme.TextSecondary;
            _driverStatus.Text = list.Count > 0
                ? "Review the detected drivers, then Inject Drivers."
                : "No .inf drivers found in that folder.";
        }
    }

    private void OnInject(object? sender, EventArgs e)
    {
        var wim = ResolveTargetBootWim();
        if (wim is null)
        {
            MessageBox.Show("No WinFE boot image was found. Run a Build first so drivers can be added to the workspace.",
                "Inject Drivers", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var checkedCats = _driverCategories.Items.Cast<ListViewItem>().Where(i => i.Checked).Select(i => (string)i.Tag!).ToHashSet();
        foreach (var d in _driverList)
            d.Selected = checkedCats.Contains(DriverCategorizer.Categorize(d)) && d.CompatibleWithTarget;
        var selected = _driverList.Where(d => d.Selected).ToList();
        if (selected.Count == 0)
        {
            MessageBox.Show("No drivers are selected. Tick at least one driver category to include.", "Inject Drivers", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        // Last chance to stop a driver that provably cannot bind. Injecting reports success either
        // way, so without this the operator finds out only after building and booting.
        var unusable = selected.Where(d => d.HasOsSupportWarning).ToList();
        if (unusable.Count > 0)
        {
            var proceed = MessageBox.Show(
                string.Join("\n\n", unusable.Select(d => $"{d.InfName}\n{d.OsSupportWarning}")) +
                "\n\nInject anyway?",
                "Driver will not load on this image", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning);
            if (proceed != DialogResult.OK) return;
        }

        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        _driverStatus.ForeColor = UiTheme.TextSecondary;
        _driverStatus.Text = "Injecting drivers… (this can take a few minutes)";
        _driverOpenLog.Visible = false;
        // Show progress in the status line only — DriverService already writes every one of these
        // messages to the log, so logging them here too printed each line twice in the live log.
        var progress = new Progress<string>(m => _driverStatus.Text = m);
        Task.Run(async () =>
        {
            // Signed drivers are the norm for forensic hardware; unsigned support is intentionally
            // not exposed in the simplified UI.
            var r = await _vm.InjectAsync(wim, selected, forceUnsigned: false, progress, _cts.Token);
            BeginInvoke(new Action(() => RenderInjectionResult(r)));
        });
    }

    private void RenderInjectionResult(DriverInjectionResult r)
    {
        _lastDismLogPath = r.DismLogPath;   // used by the on-failure "Open log" button

        if (r.Success)
        {
            _driverStatus.ForeColor = UiTheme.Pass;
            _driverStatus.Text = $"✔ Drivers successfully injected into the WinFE workspace ({r.DriversAdded} added).";
            _driverOpenLog.Visible = false;
        }
        else
        {
            _driverStatus.ForeColor = UiTheme.Fail;
            var reason = r.Errors.Count > 0 ? r.Errors[0] : "See the log for details.";
            _driverStatus.Text = "Driver injection failed: " + reason;
            _driverOpenLog.Visible = true;
        }
    }

    // ------------------------------------------------------------------ helpers
    private void OpenPathOrLogs(string? path)
    {
        try
        {
            var target = (path is not null && File.Exists(path)) ? path
                        : (path is not null && Directory.Exists(path)) ? path
                        : null;
            if (target is null) { MessageBox.Show("Nothing to open yet.", "Open", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = File.Exists(target) ? "explorer.exe" : target,
                Arguments = File.Exists(target) ? $"/select,\"{target}\"" : "",
                UseShellExecute = true
            });
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Open failed", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private static Button MakeButton(string text, bool accent = false)
    {
        var b = new Button { Text = text, AutoSize = true, Padding = new Padding(10, 4, 10, 4), Margin = new Padding(3, 3, 3, 3) };
        if (accent)
        {
            b.BackColor = UiTheme.Accent; b.ForeColor = Color.White; b.FlatStyle = FlatStyle.Flat; b.FlatAppearance.BorderSize = 0;
            b.Font = UiTheme.Subheading;
        }
        return b;
    }
}
