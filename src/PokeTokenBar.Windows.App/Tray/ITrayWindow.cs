using System.ComponentModel;

namespace PokeTokenBar.Windows.App.Tray;

internal interface ITrayWindow
{
    event CancelEventHandler? Closing;

    bool IsVisible { get; }

    bool IsMinimized { get; }

    void Show();

    void Hide();

    void Restore();

    void Activate();

    void Close();
}
