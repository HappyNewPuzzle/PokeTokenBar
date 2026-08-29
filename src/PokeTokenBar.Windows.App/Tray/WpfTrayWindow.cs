using System.ComponentModel;
using System.Windows;
using System.Windows.Media;
using Forms = System.Windows.Forms;
using Point = System.Windows.Point;
using Size = System.Windows.Size;

namespace PokeTokenBar.Windows.App.Tray;

internal sealed class WpfTrayWindow : ITrayWindow
{
    private const double PopupMargin = 8;
    private readonly Window _window;
    private readonly double _preferredWidth;
    private readonly double _preferredHeight;

    public WpfTrayWindow(Window window)
    {
        _window = window ?? throw new ArgumentNullException(nameof(window));
        _preferredWidth = window.Width;
        _preferredHeight = window.Height;
    }

    public event CancelEventHandler? Closing
    {
        add => _window.Closing += value;
        remove => _window.Closing -= value;
    }

    public event EventHandler? Deactivated
    {
        add => _window.Deactivated += value;
        remove => _window.Deactivated -= value;
    }

    public bool IsVisible => _window.IsVisible;

    public bool IsMinimized => _window.WindowState == WindowState.Minimized;

    public void ShowNearTray()
    {
        var cursor = Forms.Cursor.Position;
        var screen = Forms.Screen.FromPoint(cursor);
        var dpi = VisualTreeHelper.GetDpi(_window);
        var scaleX = dpi.DpiScaleX > 0 ? dpi.DpiScaleX : 1;
        var scaleY = dpi.DpiScaleY > 0 ? dpi.DpiScaleY : 1;
        var area = screen.WorkingArea;
        var workingArea = new Rect(
            area.Left / scaleX,
            area.Top / scaleY,
            area.Width / scaleX,
            area.Height / scaleY);
        var anchor = new Point(cursor.X / scaleX, cursor.Y / scaleY);
        var placement = PopupPositioner.Calculate(
            workingArea,
            new Size(_preferredWidth, _preferredHeight),
            anchor,
            PopupMargin);

        _window.Width = placement.Width;
        _window.Height = placement.Height;
        _window.Left = placement.Left;
        _window.Top = placement.Top;
        _window.Show();
    }

    public void Hide() => _window.Hide();

    public void Restore() => _window.WindowState = WindowState.Normal;

    public void Activate() => _window.Activate();

    public void Close() => _window.Close();
}
