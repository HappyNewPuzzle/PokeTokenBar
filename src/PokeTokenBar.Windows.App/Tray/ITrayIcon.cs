using PokeTokenBar.Windows.Core;
using System.Windows.Media.Imaging;

namespace PokeTokenBar.Windows.App.Tray;

internal interface ITrayIcon : IDisposable
{
    event EventHandler? ToggleRequested;

    event EventHandler? OpenRequested;

    event EventHandler? RefreshRequested;

    event EventHandler? ExitRequested;

    bool Visible { get; set; }

    string Text { get; set; }

    void ShowNotification(NotificationMessage message);

    void SetMenuText(string open, string refresh, string exit);

    void SetCompanionFrame(BitmapSource? frame);
}
