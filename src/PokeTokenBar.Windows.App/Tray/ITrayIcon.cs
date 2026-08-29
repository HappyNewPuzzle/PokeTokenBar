namespace PokeTokenBar.Windows.App.Tray;

internal interface ITrayIcon : IDisposable
{
    event EventHandler? OpenRequested;

    event EventHandler? RefreshRequested;

    event EventHandler? ExitRequested;

    bool Visible { get; set; }
}
