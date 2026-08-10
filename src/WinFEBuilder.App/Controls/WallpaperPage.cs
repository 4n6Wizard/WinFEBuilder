using System.Drawing;
using System.Drawing.Imaging;
using WinFEBuilder.App.ViewModels;
using WinFEBuilder.Core.Logging;

namespace WinFEBuilder.App.Controls;

/// <summary>Wallpaper page: pick an image; it's saved as wallpaper.jpg into the framework x64 &amp; x86 folders.</summary>
public sealed class WallpaperPage : UserControl
{
    private readonly WallpaperViewModel _vm;
    private readonly ILogService _log;

    private readonly Label _frameworkLabel = new();
    private readonly Label _status = new();
    private readonly PictureBox _selectedPreview = new();
    private readonly PictureBox _currentPreview = new();
    private readonly Button _browse = new();
    private readonly Button _set = new();

    private string? _selectedPath;

    public WallpaperPage(WallpaperViewModel vm, ILogService log)
    {
        _vm = vm;
        _log = log;
        Dock = DockStyle.Fill;
        BackColor = UiTheme.Background;
        Padding = new Padding(16);

        Controls.Add(BuildBody());
        Controls.Add(BuildHeader());

        _frameworkLabel.Text = string.IsNullOrWhiteSpace(_vm.FrameworkPath)
            ? "(no framework selected — set one on the Framework page)"
            : _vm.FrameworkPath!;
        LoadCurrent();
    }

    private Control BuildHeader()
    {
        var p = new Panel { Dock = DockStyle.Top, Height = 78 };
        p.Controls.Add(new Label { Text = "Wallpaper", Font = UiTheme.Heading, ForeColor = UiTheme.TextPrimary, AutoSize = true, Location = new Point(0, 0) });
        p.Controls.Add(new Label
        {
            Text = "Pick an image; it's saved as wallpaper.jpg into the framework's x64 and x86 folders and applied on the next Build.",
            Font = UiTheme.Body, ForeColor = UiTheme.TextSecondary, AutoSize = true, Location = new Point(0, 28)
        });
        _frameworkLabel.Font = UiTheme.Body;
        _frameworkLabel.ForeColor = UiTheme.TextPrimary;
        _frameworkLabel.AutoSize = true;
        _frameworkLabel.Location = new Point(0, 52);
        p.Controls.Add(new Label { Text = "Framework:", Font = UiTheme.Body, ForeColor = UiTheme.TextSecondary, AutoSize = true, Location = new Point(0, 52) });
        _frameworkLabel.Location = new Point(75, 52);
        p.Controls.Add(_frameworkLabel);
        return p;
    }

    private Control BuildBody()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 3 };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));

        root.Controls.Add(new Label { Text = "Selected image", Font = UiTheme.Subheading, Dock = DockStyle.Fill }, 0, 0);
        root.Controls.Add(new Label { Text = "Current wallpaper (in framework)", Font = UiTheme.Subheading, Dock = DockStyle.Fill }, 1, 0);

        ConfigurePreview(_selectedPreview);
        ConfigurePreview(_currentPreview);
        root.Controls.Add(_selectedPreview, 0, 1);
        root.Controls.Add(_currentPreview, 1, 1);

        _browse.Text = "Browse image…";
        _browse.AutoSize = true;
        _browse.Padding = new Padding(10, 4, 10, 4);
        _browse.Click += OnBrowse;

        _set.Text = "Set wallpaper (x64 + x86)";
        _set.Font = UiTheme.Subheading;
        _set.BackColor = UiTheme.Accent;
        _set.ForeColor = Color.White;
        _set.FlatStyle = FlatStyle.Flat;
        _set.FlatAppearance.BorderSize = 0;
        _set.AutoSize = true;
        _set.Enabled = false;
        _set.Padding = new Padding(12, 5, 12, 5);
        _set.Margin = new Padding(8, 0, 0, 0);
        _set.Click += OnSet;

        _status.AutoSize = true;
        _status.Font = UiTheme.Body;
        _status.ForeColor = UiTheme.TextSecondary;
        _status.Margin = new Padding(16, 8, 0, 0);

        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
        actions.Controls.Add(_browse);
        actions.Controls.Add(_set);
        actions.Controls.Add(_status);
        root.Controls.Add(actions, 0, 2);
        root.SetColumnSpan(actions, 2);
        return root;
    }

    private static void ConfigurePreview(PictureBox pb)
    {
        pb.Dock = DockStyle.Fill;
        pb.SizeMode = PictureBoxSizeMode.Zoom;
        pb.BackColor = Color.FromArgb(17, 24, 39);
        pb.Margin = new Padding(0, 0, 6, 6);
        pb.BorderStyle = BorderStyle.FixedSingle;
    }

    private static Image? LoadUnlocked(string path)
    {
        try
        {
            using var ms = new MemoryStream(File.ReadAllBytes(path));
            return Image.FromStream(ms);
        }
        catch { return null; }
    }

    private void LoadCurrent()
    {
        var cur = _vm.CurrentWallpaper();
        _currentPreview.Image?.Dispose();
        _currentPreview.Image = cur is null ? null : LoadUnlocked(cur);
    }

    private void OnBrowse(object? sender, EventArgs e)
    {
        using var dlg = new OpenFileDialog
        {
            Title = "Select a wallpaper image",
            Filter = "Images (*.jpg;*.jpeg;*.png;*.bmp)|*.jpg;*.jpeg;*.png;*.bmp|All files (*.*)|*.*"
        };
        if (dlg.ShowDialog(FindForm()) != DialogResult.OK) return;

        var img = LoadUnlocked(dlg.FileName);
        if (img is null)
        {
            MessageBox.Show("That file could not be read as an image.", "Invalid image", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        _selectedPath = dlg.FileName;
        _selectedPreview.Image?.Dispose();
        _selectedPreview.Image = img;
        _set.Enabled = !string.IsNullOrWhiteSpace(_vm.FrameworkPath);
        _status.Text = Path.GetFileName(dlg.FileName);
    }

    private void OnSet(object? sender, EventArgs e)
    {
        if (_selectedPath is null) return;

        // Normalize to JPEG (the framework expects wallpaper.jpg) regardless of source format.
        string tempJpeg = Path.Combine(Path.GetTempPath(), $"winfe_wallpaper_{Guid.NewGuid():N}.jpg");
        try
        {
            using (var src = LoadUnlocked(_selectedPath))
            {
                if (src is null) { MessageBox.Show("Could not read the selected image.", "Set wallpaper", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
                using var bmp = new Bitmap(src);
                bmp.Save(tempJpeg, ImageFormat.Jpeg);
            }

            var r = _vm.SetWallpaper(tempJpeg);
            if (r.Success)
            {
                LoadCurrent();
                MessageBox.Show(r.Message + Environment.NewLine + Environment.NewLine + r.TechnicalDetails,
                    "Wallpaper set", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            else
            {
                MessageBox.Show(r.Message + (r.RecommendedAction is null ? "" : Environment.NewLine + Environment.NewLine + r.RecommendedAction),
                    "Set wallpaper failed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
        catch (Exception ex)
        {
            _log.Error("Wallpaper", "Set wallpaper failed.", ex);
            MessageBox.Show(ex.Message, "Set wallpaper failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            try { if (File.Exists(tempJpeg)) File.Delete(tempJpeg); } catch { }
        }
    }
}
