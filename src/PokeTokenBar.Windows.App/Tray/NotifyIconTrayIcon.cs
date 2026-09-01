using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Media.Imaging;
using PokeTokenBar.Windows.App.Sprites;
using PokeTokenBar.Windows.Core;
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
    private Icon? _companionIcon;
    private PokemonSpritePresentation? _lastPresentation;

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

    public string Text
    {
        get => _notifyIcon.Text;
        set => _notifyIcon.Text = string.IsNullOrWhiteSpace(value) ? "PokeTokenBar" : value[..Math.Min(value.Length, 63)];
    }

    public void ShowNotification(NotificationMessage message)
    {
        _notifyIcon.BalloonTipTitle = message.Title;
        _notifyIcon.BalloonTipText = message.Body;
        _notifyIcon.BalloonTipIcon = message.Kind == NotificationKind.LimitCritical
            ? Forms.ToolTipIcon.Warning
            : Forms.ToolTipIcon.Info;
        _notifyIcon.ShowBalloonTip(5000);
    }

    public void SetMenuText(string open, string refresh, string exit)
    {
        _openItem.Text = open;
        _refreshItem.Text = refresh;
        _exitItem.Text = exit;
    }

    public void SetCompanion(PokemonSpritePresentation? presentation)
    {
        if (ReferenceEquals(_lastPresentation, presentation)) return;
        _lastPresentation = presentation;
        if (presentation?.StaticImage is not { } source)
        {
            _notifyIcon.Icon = SystemIcons.Application;
            Interlocked.Exchange(ref _companionIcon, null)?.Dispose();
            return;
        }
        try
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(source));
            using var stream = new MemoryStream();
            encoder.Save(stream);
            stream.Position = 0;
            using var bitmap = new Bitmap(stream);
            using var scaled = new Bitmap(bitmap, new Size(32, 32));
            var handle = scaled.GetHicon();
            try
            {
                var icon = (Icon)Icon.FromHandle(handle).Clone();
                _notifyIcon.Icon = icon;
                Interlocked.Exchange(ref _companionIcon, icon)?.Dispose();
            }
            finally { DestroyIcon(handle); }
        }
        catch (Exception) { }
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
        _companionIcon?.Dispose();
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

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(nint handle);
}
