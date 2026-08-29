using System.ComponentModel;

namespace PokeTokenBar.Windows.App.Tray;

internal interface ITrayWindow
{
    event CancelEventHandler? Closing;

    event EventHandler? Deactivated;

    bool IsVisible { get; }

    bool IsMinimized { get; }

    void ShowNearTray();

    void Hide();

    void Restore();

    void Activate();

    void Close();
}
