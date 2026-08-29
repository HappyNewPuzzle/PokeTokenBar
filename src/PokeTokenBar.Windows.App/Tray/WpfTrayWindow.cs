using System.ComponentModel;
using System.Windows;

namespace PokeTokenBar.Windows.App.Tray;

internal sealed class WpfTrayWindow(Window window) : ITrayWindow
{
    public event CancelEventHandler? Closing
    {
        add => window.Closing += value;
        remove => window.Closing -= value;
    }

    public bool IsVisible => window.IsVisible;

    public bool IsMinimized => window.WindowState == WindowState.Minimized;

    public void Show() => window.Show();

    public void Hide() => window.Hide();

    public void Restore() => window.WindowState = WindowState.Normal;

    public void Activate() => window.Activate();

    public void Close() => window.Close();
}
