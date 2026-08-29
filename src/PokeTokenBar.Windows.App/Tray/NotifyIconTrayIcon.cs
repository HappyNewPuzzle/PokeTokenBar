using System.Drawing;
using Forms = System.Windows.Forms;

namespace PokeTokenBar.Windows.App.Tray;

internal sealed class NotifyIconTrayIcon : ITrayIcon
{
    private readonly Forms.NotifyIcon _notifyIcon;
    private readonly Forms.ContextMenuStrip _menu;
    private readonly Forms.ToolStripMenuItem _openItem;
    private readonly Forms.ToolStripMenuItem _refreshItem;
    private readonly Forms.ToolStripMenuItem _exitItem;
    private bool _disposed;

    public NotifyIconTrayIcon()
    {
        _openItem = new Forms.ToolStripMenuItem("Open");
        _refreshItem = new Forms.ToolStripMenuItem("Refresh");
        _exitItem = new Forms.ToolStripMenuItem("Exit");
        _menu = new Forms.ContextMenuStrip();
        _menu.Items.AddRange([_openItem, _refreshItem, new Forms.ToolStripSeparator(), _exitItem]);

        _notifyIcon = new Forms.NotifyIcon
        {
            Text = "PokeTokenBar",
            Icon = SystemIcons.Application,
            ContextMenuStrip = _menu,
        };

        _notifyIcon.MouseDown += OnMouseDown;
        _openItem.Click += OnOpenClicked;
        _refreshItem.Click += OnRefreshClicked;
        _exitItem.Click += OnExitClicked;
    }

    public event EventHandler? ToggleRequested;

    public event EventHandler? OpenRequested;

    public event EventHandler? RefreshRequested;

    public event EventHandler? ExitRequested;

    public bool Visible
    {
        get => _notifyIcon.Visible;
        set => _notifyIcon.Visible = value;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _notifyIcon.Visible = false;
        _notifyIcon.MouseDown -= OnMouseDown;
        _openItem.Click -= OnOpenClicked;
        _refreshItem.Click -= OnRefreshClicked;
        _exitItem.Click -= OnExitClicked;
        _notifyIcon.Dispose();
        _menu.Dispose();
    }

    private void OnMouseDown(object? sender, Forms.MouseEventArgs e)
    {
        if (e.Button == Forms.MouseButtons.Left)
        {
            ToggleRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    private void OnOpenClicked(object? sender, EventArgs e) =>
        OpenRequested?.Invoke(this, EventArgs.Empty);

    private void OnRefreshClicked(object? sender, EventArgs e) =>
        RefreshRequested?.Invoke(this, EventArgs.Empty);

    private void OnExitClicked(object? sender, EventArgs e) =>
        ExitRequested?.Invoke(this, EventArgs.Empty);
}
