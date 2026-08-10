using System.Drawing;
using WinFEBuilder.App.ViewModels;
using WinFEBuilder.Core.Logging;
using WinFEBuilder.Core.Models;

namespace WinFEBuilder.App.Controls;

/// <summary>
/// USB page: safely create one or many bootable WinFE USBs. Multiple eligible removable disks can be
/// checked and created SEQUENTIALLY (never in parallel). All existing safety gates are preserved:
/// protected disks can't be selected, a batch-aware typed confirmation is required, and each disk's
/// identity is revalidated immediately before it is written.
/// </summary>
public sealed class UsbPage : UserControl
{
    private readonly UsbViewModel _vm;
    private readonly ILogService _log;
    private readonly LiveLogPanel _liveLog;

    private readonly Label _banner = new();
    private readonly TextBox _media = new();
    private readonly Button _autoDetect = new();
    private readonly Button _browseMedia = new();
    private readonly CheckBox _advanced = new();
    private readonly Button _scan = new();

    private readonly ListView _disks = new();
    private readonly Button _selectAll = new();
    private readonly Button _clearSel = new();
    private readonly Label _selectedCount = new();

    private readonly TextBox _identity = new();
    private readonly Label _eligibility = new();
    private readonly Label _phraseHint = new();
    private readonly TextBox _phrase = new();
    private readonly CheckBox _ack = new();
    private readonly Button _create = new();
    private readonly Button _cancel = new();
    private readonly ProgressBar _progress = new();
    private readonly TextBox _results = new();

    private CancellationTokenSource? _cts;
    private List<DiskInfo> _current = new();
    private bool _isBusy;

    public UsbPage(UsbViewModel vm, ILogService log)
    {
        _vm = vm;
        _log = log;
        _liveLog = new LiveLogPanel(log);

        Dock = DockStyle.Fill;
        BackColor = UiTheme.Background;
        Padding = new Padding(16);

        Controls.Add(BuildBody());
        Controls.Add(BuildConfig());
        Controls.Add(BuildBanner());
        Controls.Add(BuildHeader());

        _media.Text = _vm.AutoDetectMediaSource() ?? string.Empty;
        UpdateCreateEnabled();
    }

    private Control BuildHeader()
    {
        var p = new Panel { Dock = DockStyle.Top, Height = 52 };
        p.Controls.Add(new Label { Text = "USB", Font = UiTheme.Heading, ForeColor = UiTheme.TextPrimary, AutoSize = true, Location = new Point(0, 0) });
        p.Controls.Add(new Label
        {
            Text = "Create one or more bootable WinFE USBs. Protected disks are blocked; a typed confirmation is required.",
            Font = UiTheme.Body, ForeColor = UiTheme.TextSecondary, AutoSize = true, Location = new Point(0, 28)
        });
        return p;
    }

    private Control BuildBanner()
    {
        _banner.Dock = DockStyle.Top;
        _banner.Height = 30;
        _banner.TextAlign = ContentAlignment.MiddleCenter;
        _banner.Font = new Font("Segoe UI Semibold", 10f);
        if (_vm.SimulationMode)
        {
            // DEBUG builds only — SettingsService forces simulation on so an IDE run cannot erase a
            // disk. Operators never see this: the released Release build always writes for real.
            _banner.Text = "DEBUG BUILD — simulated. No disk will be modified; the DiskPart script is generated for review only.";
            _banner.BackColor = Color.FromArgb(220, 252, 231);
            _banner.ForeColor = UiTheme.Pass;
        }
        else
        {
            _banner.Text = "WRITES ARE REAL AND DESTRUCTIVE — the selected disk will be erased. Verify the model, serial and capacity before confirming.";
            _banner.BackColor = Color.FromArgb(254, 226, 226);
            _banner.ForeColor = UiTheme.Fail;
        }
        return _banner;
    }

    private Control BuildConfig()
    {
        var grid = new TableLayoutPanel { Dock = DockStyle.Top, Height = 74, ColumnCount = 4, RowCount = 2, Padding = new Padding(0, 6, 0, 4) };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        _media.Dock = DockStyle.Fill;
        _media.Font = UiTheme.Body;
        _media.Margin = new Padding(0, 2, 8, 2);
        _media.PlaceholderText = @"Media folder containing Boot\ EFI\ Sources\boot.wim";
        _media.TextChanged += (_, _) => UpdateCreateEnabled();

        _autoDetect.Text = "Auto-detect";
        _autoDetect.AutoSize = true;
        _autoDetect.Margin = new Padding(0, 0, 6, 0);
        _autoDetect.Click += (_, _) =>
        {
            var m = _vm.AutoDetectMediaSource();
            if (m is null) MessageBox.Show("No built media found under the workspace yet. Run a build first, or Browse.", "Auto-detect", MessageBoxButtons.OK, MessageBoxIcon.Information);
            else _media.Text = m;
        };

        _browseMedia.Text = "Browse…";
        _browseMedia.AutoSize = true;
        _browseMedia.Click += (_, _) =>
        {
            using var dlg = new FolderBrowserDialog { Description = "Select the WinFE media folder", UseDescriptionForTitle = true };
            if (dlg.ShowDialog(FindForm()) == DialogResult.OK) _media.Text = dlg.SelectedPath;
        };

        _advanced.Text = "Advanced: show non-removable disks (protected disks are still blocked)";
        _advanced.AutoSize = true;
        _advanced.Anchor = AnchorStyles.Left;
        _advanced.CheckedChanged += async (_, _) => { if (!_isBusy) await ScanAsync(); };

        _scan.Text = "Scan Disks";
        _scan.Font = UiTheme.Subheading;
        _scan.BackColor = UiTheme.Accent;
        _scan.ForeColor = Color.White;
        _scan.FlatStyle = FlatStyle.Flat;
        _scan.FlatAppearance.BorderSize = 0;
        _scan.AutoSize = true;
        _scan.Padding = new Padding(12, 4, 12, 4);
        _scan.Click += async (_, _) => await ScanAsync();

        grid.Controls.Add(MakeCaption("Media source:"), 0, 0);
        grid.Controls.Add(_media, 1, 0);
        grid.Controls.Add(_autoDetect, 2, 0);
        grid.Controls.Add(_browseMedia, 3, 0);
        grid.Controls.Add(_advanced, 1, 1);
        grid.Controls.Add(_scan, 3, 1);
        return grid;
    }

    private static Label MakeCaption(string text) => new()
    { Text = text, AutoSize = true, Font = UiTheme.Body, ForeColor = UiTheme.TextSecondary, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 6, 0) };

    private Control BuildBody()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 150));

        var upper = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
        upper.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        upper.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));

        upper.Controls.Add(BuildDiskPanel(), 0, 0);
        upper.Controls.Add(BuildRightPanel(), 1, 0);

        _liveLog.Dock = DockStyle.Fill;
        _liveLog.Margin = new Padding(0, 8, 0, 0);

        root.Controls.Add(upper, 0, 0);
        root.Controls.Add(_liveLog, 0, 1);
        return root;
    }

    private Control BuildDiskPanel()
    {
        var panel = new Panel { Dock = DockStyle.Fill };

        _disks.View = View.Details;
        _disks.Dock = DockStyle.Fill;
        _disks.FullRowSelect = true;
        _disks.GridLines = true;
        _disks.CheckBoxes = true;              // multi-select via checkboxes
        _disks.MultiSelect = false;            // single row focus for the identity panel
        _disks.Font = UiTheme.Body;
        _disks.Columns.Add("#", 34);
        _disks.Columns.Add("Name", 150);
        _disks.Columns.Add("Capacity", 80, HorizontalAlignment.Right);
        _disks.Columns.Add("Bus", 60);
        _disks.Columns.Add("Target?", 110);
        _disks.AccessibleName = "Detected disks (check eligible disks to include)";
        _disks.SelectedIndexChanged += (_, _) => OnDiskFocused();
        _disks.ItemCheck += OnItemCheck;       // block ineligible / during-run checks
        _disks.ItemChecked += (_, _) => OnSelectionChanged();

        var bar = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = true };
        _selectAll.Text = "Select All Eligible"; _selectAll.AutoSize = true; _selectAll.Padding = new Padding(8, 3, 8, 3); _selectAll.Click += (_, _) => SelectAllEligible();
        _clearSel.Text = "Clear Selection"; _clearSel.AutoSize = true; _clearSel.Padding = new Padding(8, 3, 8, 3); _clearSel.Click += (_, _) => ClearSelection();
        _selectedCount.Text = "0 USB drives selected"; _selectedCount.AutoSize = true; _selectedCount.Font = UiTheme.Subheading; _selectedCount.Margin = new Padding(10, 8, 0, 0);
        bar.Controls.AddRange(new Control[] { _selectAll, _clearSel, _selectedCount });

        panel.Controls.Add(_disks);
        panel.Controls.Add(bar);
        return panel;
    }

    private Control BuildRightPanel()
    {
        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 8, Margin = new Padding(8, 0, 0, 0) };
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));   // label
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 42));    // identity
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));   // eligibility
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));   // phrase hint
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));   // phrase box
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));   // ack
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));   // buttons
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 58));    // results

        _identity.Multiline = true;
        _identity.ReadOnly = true;
        _identity.ScrollBars = ScrollBars.Vertical;
        _identity.Dock = DockStyle.Fill;
        _identity.Font = UiTheme.Mono;
        _identity.BackColor = UiTheme.Surface;
        _identity.Text = "Select (highlight) a disk to see its full identity. Check disks to include them.";

        _eligibility.Dock = DockStyle.Fill;
        _eligibility.Font = UiTheme.Body;
        _eligibility.ForeColor = UiTheme.TextSecondary;

        _phraseHint.Dock = DockStyle.Fill;
        _phraseHint.Font = UiTheme.Body;
        _phraseHint.ForeColor = UiTheme.TextSecondary;
        _phraseHint.Text = "Check one or more eligible disks to enable confirmation.";

        _phrase.Dock = DockStyle.Fill;
        _phrase.Font = UiTheme.Mono;
        _phrase.Enabled = false;
        _phrase.PlaceholderText = "Type the exact phrase here";
        _phrase.TextChanged += (_, _) => UpdateCreateEnabled();

        _ack.Text = "I understand that all data on the selected disk(s) will be destroyed.";
        _ack.AutoSize = true;
        _ack.Enabled = false;
        _ack.CheckedChanged += (_, _) => UpdateCreateEnabled();

        _create.Text = "Create Selected USBs";
        _create.Font = UiTheme.Subheading;
        _create.BackColor = UiTheme.Fail;
        _create.ForeColor = Color.White;
        _create.FlatStyle = FlatStyle.Flat;
        _create.FlatAppearance.BorderSize = 0;
        _create.AutoSize = true;
        _create.Padding = new Padding(12, 6, 12, 6);
        _create.Enabled = false;
        _create.Click += async (_, _) => await CreateBatchAsync();

        _cancel.Text = "Cancel";
        _cancel.AutoSize = true;
        _cancel.Enabled = false;
        _cancel.Padding = new Padding(10, 6, 10, 6);
        _cancel.Margin = new Padding(8, 0, 0, 0);
        _cancel.Click += (_, _) => _cts?.Cancel();

        _progress.Style = ProgressBarStyle.Marquee;
        _progress.MarqueeAnimationSpeed = 0;
        _progress.Visible = false;
        _progress.Width = 140;
        _progress.Margin = new Padding(8, 6, 0, 0);

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = new Padding(0) };
        buttons.Controls.Add(_create);
        buttons.Controls.Add(_cancel);
        buttons.Controls.Add(_progress);

        _results.Multiline = true;
        _results.ReadOnly = true;
        _results.ScrollBars = ScrollBars.Vertical;
        _results.Dock = DockStyle.Fill;
        _results.Font = UiTheme.Mono;
        _results.BackColor = UiTheme.Surface;

        panel.Controls.Add(new Label { Text = "Selected disk identity", Font = UiTheme.Subheading, Dock = DockStyle.Fill }, 0, 0);
        panel.Controls.Add(_identity, 0, 1);
        panel.Controls.Add(_eligibility, 0, 2);
        panel.Controls.Add(_phraseHint, 0, 3);
        panel.Controls.Add(_phrase, 0, 4);
        panel.Controls.Add(_ack, 0, 5);
        panel.Controls.Add(buttons, 0, 6);
        panel.Controls.Add(_results, 0, 7);
        return panel;
    }

    // ---------------------------------------------------------------- scanning
    private async Task ScanAsync()
    {
        SetBusy(true);
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        try
        {
            _current = await _vm.EnumerateAsync(_advanced.Checked, _cts.Token);
            _disks.BeginUpdate();
            _disks.Items.Clear();
            foreach (var d in _current)
            {
                var elig = _vm.Evaluate(d, allowNonRemovable: _advanced.Checked);
                var item = new ListViewItem(d.Number.ToString());
                item.SubItems.Add(d.FriendlyName ?? d.Model ?? "Unknown");
                item.SubItems.Add(d.CapacityText);
                item.SubItems.Add(d.BusType ?? "?");
                item.SubItems.Add(elig.CanTarget ? "Eligible" : "Blocked");
                item.ForeColor = elig.CanTarget ? UiTheme.Pass : UiTheme.Fail;
                item.Tag = d;
                _disks.Items.Add(item);
            }
            _disks.EndUpdate();
            if (_current.Count == 0) _log.Warning("USB", "No disks found for the current filter.");
        }
        catch (Exception ex)
        {
            _log.Error("USB", "Disk scan failed.", ex);
            MessageBox.Show(ex.Message, "Scan failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            SetBusy(false);
            ResetConfirm();
            OnSelectionChanged();
        }
    }

    // ---------------------------------------------------------------- selection
    private void OnItemCheck(object? sender, ItemCheckEventArgs e)
    {
        // Never allow selection changes while a batch is running.
        if (_isBusy) { e.NewValue = e.CurrentValue; return; }
        if (e.NewValue != CheckState.Checked) return;

        // Only eligible disks may be checked; blocked/protected disks can never be selected.
        var disk = _disks.Items[e.Index].Tag as DiskInfo;
        if (disk is null || !_vm.Evaluate(disk, _advanced.Checked).CanTarget)
            e.NewValue = CheckState.Unchecked;
    }

    private List<DiskInfo> CheckedDisks() =>
        _disks.Items.Cast<ListViewItem>().Where(i => i.Checked).Select(i => (DiskInfo)i.Tag!).ToList();

    private void SelectAllEligible()
    {
        _disks.BeginUpdate();
        foreach (ListViewItem item in _disks.Items)
            if (item.Tag is DiskInfo d && _vm.Evaluate(d, _advanced.Checked).CanTarget)
                item.Checked = true;
        _disks.EndUpdate();
        OnSelectionChanged();
    }

    private void ClearSelection()
    {
        _disks.BeginUpdate();
        foreach (ListViewItem item in _disks.Items) item.Checked = false;
        _disks.EndUpdate();
        OnSelectionChanged();
    }

    private void OnSelectionChanged()
    {
        var nums = CheckedDisks().Select(d => d.Number).ToList();
        _selectedCount.Text = $"{nums.Count} USB drive{(nums.Count == 1 ? "" : "s")} selected";

        // Selection changed → invalidate any previously typed confirmation.
        _phrase.Text = "";

        if (nums.Count == 0)
        {
            _phraseHint.Text = "Check one or more eligible disks to enable confirmation.";
            _phrase.Enabled = false;
            _ack.Enabled = false;
            _ack.Checked = false;
        }
        else
        {
            _phraseHint.Text = $"To confirm, type exactly:  {_vm.ExpectedBatchPhrase(nums)}";
            _phrase.Enabled = true;
            _ack.Enabled = true;
        }
        UpdateCreateEnabled();
    }

    private void OnDiskFocused()
    {
        var focused = _disks.SelectedItems.Count > 0 ? _disks.SelectedItems[0].Tag as DiskInfo : null;
        if (focused is null)
        {
            _identity.Text = "Select (highlight) a disk to see its full identity. Check disks to include them.";
            _eligibility.Text = "";
            return;
        }

        _identity.Text = string.Join(Environment.NewLine, new[]
        {
            $"Disk number   : {focused.Number}",
            $"Friendly name : {focused.FriendlyName}",
            $"Manufacturer  : {focused.Manufacturer}",
            $"Model         : {focused.Model}",
            $"Serial number : {focused.SerialNumber}",
            $"Unique ID     : {focused.UniqueId}",
            $"Bus type      : {focused.BusType}",
            $"Capacity      : {focused.CapacityText} ({focused.SizeBytes:N0} bytes)",
            $"Partitions    : {focused.PartitionCount}",
            $"Drive letters : {string.Join(", ", focused.DriveLetters)}",
            $"File systems  : {string.Join(", ", focused.FileSystems)}",
            $"Offline       : {focused.IsOffline}",
            $"Read-only     : {focused.IsReadOnly}",
            $"Health        : {focused.HealthStatus}",
            $"Removable     : {focused.IsRemovable}",
            $"System disk   : {focused.IsSystemDisk}",
            $"Boot disk     : {focused.IsBootDisk}",
            focused.IsSimulated ? "** SIMULATED DISK — for demonstration only **" : ""
        });

        var elig = _vm.Evaluate(focused, allowNonRemovable: _advanced.Checked);
        if (elig.CanTarget)
        {
            _eligibility.Text = "✔ Eligible target — tick its checkbox to include it." + (focused.IsSimulated ? " (simulated)" : "");
            _eligibility.ForeColor = UiTheme.Pass;
        }
        else
        {
            _eligibility.Text = "✖ Blocked: " + elig.BlockSummary;
            _eligibility.ForeColor = UiTheme.Fail;
        }
    }

    private void ResetConfirm()
    {
        _phrase.Text = "";
        _phrase.Enabled = false;
        _ack.Checked = false;
        _ack.Enabled = false;
    }

    private void UpdateCreateEnabled()
    {
        var nums = CheckedDisks().Select(d => d.Number).ToList();
        var ok = !_isBusy
                 && nums.Count >= 1
                 && _vm.BatchPhraseValid(_phrase.Text, nums)
                 && _ack.Checked
                 && !string.IsNullOrWhiteSpace(_media.Text);
        _create.Enabled = ok;
        _create.Text = _vm.SimulationMode ? "Create Selected USBs (simulate)" : "Create Selected USBs";
    }

    // ---------------------------------------------------------------- batch run
    private async Task CreateBatchAsync()
    {
        var disks = CheckedDisks();
        if (disks.Count == 0) return;

        if (!_vm.SimulationMode)
        {
            var list = string.Join("\n", disks.Select(d =>
                $"Disk {d.Number} — {d.FriendlyName ?? d.Model ?? "Unknown"} ({d.CapacityText}"
                + (string.IsNullOrWhiteSpace(d.SerialNumber) ? "" : $", SN {d.SerialNumber}") + ")"));
            var confirm = MessageBox.Show(
                $"You are about to ERASE {disks.Count} USB drive{(disks.Count == 1 ? "" : "s")}:\n\n{list}\n\n"
                + "This cannot be undone, and each disk will be written one at a time. Proceed?",
                "Final confirmation", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
            if (confirm != DialogResult.Yes) return;
        }

        SetBusy(true);
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        _results.Text = _vm.SimulationMode ? "Simulating…" : "Creating…";

        var request = new UsbBatchRequest
        {
            Targets = disks,
            MediaSourcePath = _media.Text.Trim(),
            ConfirmationPhrase = _phrase.Text,
            AcknowledgedDataLoss = _ack.Checked,
            // The "Advanced" toggle is the operator's explicit opt-in to allow fixed (non-removable) disks.
            AllowFixedDisk = _advanced.Checked
        };

        // Batch logs to the shared live log via the logger; UI progress is marshalled here.
        var log = new Progress<string>(_ => { });
        var batchProgress = new Progress<UsbBatchProgress>(RenderBatchProgress);

        UsbBatchResult? outcome = null;

        try
        {
            outcome = await _vm.RunBatchAsync(request, log, batchProgress, _cts.Token);
            RenderBatchSummary(outcome);
        }
        catch (Exception ex)
        {
            _log.Error("USB", "USB batch failed.", ex);
            MessageBox.Show(ex.Message, "USB batch failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            SetBusy(false);
            await ScanAsync();          // rescan + clears checks; requires a fresh confirmation for the next batch
        }

        // Reported only after the busy state is cleared and the rescan has finished, so the
        // dialog is not shown over a spinning progress bar and an enabled Cancel button.
        if (outcome is not null) ShowCompletionAlert(outcome);
    }

    /// <summary>
    /// Tells the operator the batch is finished. Without this the only signal was a quiet
    /// text update in the results box, which is easy to miss on a long run.
    /// </summary>
    private void ShowCompletionAlert(UsbBatchResult r)
    {
        if (r.GlobalAbort)
        {
            MessageBox.Show(
                "The USB batch could not start.\n\n" + (r.GlobalError ?? "Unknown error."),
                "USB batch did not run", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        var drives = r.Successful == 1 ? "USB drive" : "USB drives";
        var detail = string.Join(Environment.NewLine,
            r.Targets.Select(t => $"Disk {t.DiskNumber} — {t.StatusLine}"));

        string caption;
        string headline;
        MessageBoxIcon icon;

        if (r.Failed == 0 && r.Canceled == 0)
        {
            caption = "USB creation complete";
            headline = r.Simulated
                ? $"Simulation complete. {r.Successful} {drives} would have been created — no disk was modified."
                : $"Done. {r.Successful} bootable WinFE {drives} created successfully.";
            icon = MessageBoxIcon.Information;
        }
        else if (r.Successful == 0 && r.Failed > 0)
        {
            caption = "USB creation failed";
            headline = $"No USB drives were created. {r.Failed} failed.";
            icon = MessageBoxIcon.Error;
        }
        else if (r.Failed > 0)
        {
            caption = "USB creation finished with errors";
            headline = $"{r.Successful} succeeded, {r.Failed} failed"
                       + (r.Canceled > 0 ? $", {r.Canceled} canceled." : ".");
            icon = MessageBoxIcon.Warning;
        }
        else
        {
            caption = "USB creation canceled";
            headline = $"Canceled. {r.Successful} completed before stopping, {r.Canceled} not written.";
            icon = MessageBoxIcon.Warning;
        }

        MessageBox.Show(
            $"{headline}{Environment.NewLine}{Environment.NewLine}{detail}{Environment.NewLine}{Environment.NewLine}"
            + $"Full details: {_log.TextLogPath}",
            caption, MessageBoxButtons.OK, icon);
    }

    private void RenderBatchProgress(UsbBatchProgress p)
    {
        _results.Text = string.Join(Environment.NewLine, new[]
        {
            $"Creating USB {p.Current} of {p.Total}",
            $"Disk {p.Disk?.Number} — {p.Disk?.FriendlyName ?? p.Disk?.Model}",
            $"Stage: {p.Stage}",
            "",
            $"Completed: {p.Successful + p.Failed} of {p.Total}",
            $"Success: {p.Successful}   Failed: {p.Failed}"
        });
    }

    private void RenderBatchSummary(UsbBatchResult r) => _results.Text = r.SummaryText();

    private void SetBusy(bool busy)
    {
        _isBusy = busy;
        _scan.Enabled = !busy;
        _advanced.Enabled = !busy;
        _autoDetect.Enabled = !busy;
        _browseMedia.Enabled = !busy;
        _media.Enabled = !busy;
        _disks.Enabled = !busy;
        _selectAll.Enabled = !busy;
        _clearSel.Enabled = !busy;
        _phrase.Enabled = !busy && _phrase.Enabled;
        _ack.Enabled = !busy && _ack.Enabled;
        _cancel.Enabled = busy;
        _progress.Visible = busy;
        _progress.MarqueeAnimationSpeed = busy ? 30 : 0;
        if (busy) _create.Enabled = false; else UpdateCreateEnabled();
    }
}
