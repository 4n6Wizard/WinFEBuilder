using System.Drawing;
using WinFEBuilder.Core.Models;

namespace WinFEBuilder.App.Controls;

/// <summary>
/// A clickable status card showing an item's name, colored status badge, and summary.
/// Clicking (or pressing Enter/Space when focused) raises <see cref="DetailsRequested"/>.
/// </summary>
public sealed class StatusCard : Panel
{
    private readonly Label _badge = new();
    private readonly Label _name = new();
    private readonly Label _summary = new();

    public AuditItem? Item { get; private set; }

    public event EventHandler<AuditItem>? DetailsRequested;

    public StatusCard()
    {
        Margin = new Padding(6);
        Padding = new Padding(12);
        Width = 300;
        Height = 96;
        BackColor = UiTheme.Surface;
        BorderStyle = BorderStyle.FixedSingle;
        Cursor = Cursors.Hand;
        TabStop = true;

        _badge.AutoSize = true;
        _badge.Font = new Font("Segoe UI Semibold", 8.5f);
        _badge.ForeColor = Color.White;
        _badge.Padding = new Padding(6, 2, 6, 2);
        _badge.Location = new Point(12, 12);
        _badge.Text = "NOT CONFIGURED";

        _name.AutoSize = true;
        _name.Font = UiTheme.Subheading;
        _name.ForeColor = UiTheme.TextPrimary;
        _name.Location = new Point(12, 40);
        _name.MaximumSize = new Size(276, 0);

        _summary.AutoSize = true;
        _summary.Font = UiTheme.Body;
        _summary.ForeColor = UiTheme.TextSecondary;
        _summary.Location = new Point(12, 64);
        _summary.MaximumSize = new Size(276, 0);

        Controls.Add(_badge);
        Controls.Add(_name);
        Controls.Add(_summary);

        Click += (_, _) => Raise();
        _badge.Click += (_, _) => Raise();
        _name.Click += (_, _) => Raise();
        _summary.Click += (_, _) => Raise();
        KeyDown += (_, e) =>
        {
            if (e.KeyCode is Keys.Enter or Keys.Space) Raise();
        };
    }

    private void Raise()
    {
        if (Item is not null) DetailsRequested?.Invoke(this, Item);
    }

    public void Bind(AuditItem item)
    {
        Item = item;
        AccessibleName = $"{item.Name}: {UiTheme.StatusText(item.Status)}";
        AccessibleDescription = item.Summary;

        _name.Text = item.Name;
        _summary.Text = Truncate(item.Summary, 120);
        _badge.Text = UiTheme.StatusText(item.Status);
        _badge.BackColor = UiTheme.StatusColor(item.Status);
        Invalidate();
    }

    private static string Truncate(string s, int max) =>
        string.IsNullOrEmpty(s) || s.Length <= max ? s : s[..max] + "…";
}
